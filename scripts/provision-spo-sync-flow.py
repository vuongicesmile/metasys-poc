"""Provision the FMC-Inbox scan flow and deploy the SharePoint sync button.

The flow uses SharePoint and Dataverse connection references. It stores only a
new or changed file version in fmc_spofile; the local CLI performs parsing.
"""
import argparse
import json
import subprocess
import sys
import uuid
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / ".artifacts/spo-sync"
ORG = "https://org06cbc9ec.crm5.dynamics.com"
ENV = "5abcb0e5-99b2-e51f-aa0e-90d84405798b"
ORG_ID = "ab191700-b99e-f111-aaa0-000d3a80bb96"
SOLUTION = "FMCentralBms"
API_NAME = "fmc_RequestSpoSync"
FLOW_NAME = "FMC - Request SPO Sync"
SQL_FLOW_NAME = "FMC - App Request BMS Sync Event"
FULL_FLOW_NAME = "FMC - Request Full Sync"
RESOURCE_NAME = "fmc_/pages/BmsEventDemo.html"
SITE = "https://titancorpvncom.sharepoint.com/sites/Powerplatform"
LIBRARY = "7d9282a4-c45d-4ef8-8719-18f78e71d0d0"
INBOX = "/Shared Documents/FMC-Inbox"
NAMESPACE = "bms_spo_dev01"
MAX_FILE_BYTES = 52_428_800
SP = "shared_sharepointonline"
DV = "shared_commondataserviceforapps"
CONNECTIONS = {
    SP: ("fmc_sharedsharepointonline", "shared-sharepointonl-c613b2d6"),
    DV: ("fmc_sharedcommondataserviceforapps", "shared-commondataser-ffc44f95-e5ab-44b0-acdf-47752fa72135"),
}


def after(name=None, statuses=None):
    return {name: statuses or ["Succeeded"]} if name else {}


def action(connector, operation, parameters, previous=None):
    return {
        "type": "OpenApiConnection",
        "runAfter": after(previous),
        "inputs": {
            "host": {
                "apiId": "/providers/Microsoft.PowerApps/apis/" + connector,
                "connectionName": connector,
                "operationId": operation,
            },
            "parameters": parameters,
        },
    }


def compose(value, previous=None):
    return {"type": "Compose", "inputs": value, "runAfter": after(previous)}


def set_variable(name, value, previous=None):
    return {
        "type": "SetVariable",
        "runAfter": after(previous),
        "inputs": {"name": name, "value": value},
    }


def build(entity_set, catalog_id, category_id):
    def update(values, previous=None):
        return action(
            DV,
            "UpdateOnlyRecord",
            {
                "entityName": entity_set,
                "recordId": "@variables('RowId')",
                **{"item/" + key: value for key, value in values.items()},
            },
            previous,
        )

    item = "items('For_each_file')"
    identifier = f"@{item}?['{{Identifier}}']"
    filename = f"@{item}?['{{FilenameWithExtension}}']"
    full_path = f"@{item}?['{{FullPath}}']"
    link = f"@{item}?['{{Link}}']"
    etag = "@body('Metadata_before')?['ETag']"
    size = "@int(body('Metadata_before')?['Size'])"
    source_key = (
        "@concat(parameters('SpoSourceNamespace'), '_', "
        "toLower(parameters('SpoLibraryId')), '_', string(" + item + "?['ID']))"
    )
    is_folder = item + "?['{IsFolder}']"
    item_name = item + "?['{FilenameWithExtension}']"
    item_path = item + "?['{FullPath}']"
    normalized_item_path = "concat('/', " + item_path + ")"
    supported = (
        "@and(equals(coalesce(" + is_folder + ", false), false),"
        "or(endsWith(toLower(" + item_name + "), '.csv'),"
        "endsWith(toLower(" + item_name + "), '.json'),"
        "endsWith(toLower(" + item_name + "), '.xlsx')),"
        + "or(startsWith(toLower(" + normalized_item_path + "), toLower(concat(parameters('SpoInboxPath'), '/01-Master/Building/'))),"
        "startsWith(toLower(" + normalized_item_path + "), toLower(concat(parameters('SpoInboxPath'), '/01-Master/Equipment/'))),"
        "startsWith(toLower(" + normalized_item_path + "), toLower(concat(parameters('SpoInboxPath'), '/01-Master/WaterMeter/'))),"
        "startsWith(toLower(" + normalized_item_path + "), toLower(concat(parameters('SpoInboxPath'), '/02-Telemetry/Electricity/'))),"
        "startsWith(toLower(" + normalized_item_path + "), toLower(concat(parameters('SpoInboxPath'), '/02-Telemetry/Water/')))))"
    )

    create_receipt = action(
        DV,
        "CreateRecord",
        {
            "entityName": entity_set,
            "item/fmc_name": filename,
            "item/fmc_filename": filename,
            "item/fmc_sourcekey": source_key,
            "item/fmc_status": 789110000,
            "item/fmc_importstatus": 789111000,
            "item/fmc_receivedat": "@utcNow()",
            "item/fmc_rowcount": 0,
        },
    )
    resolve_receipt = {
        "type": "If",
        "runAfter": after("Find_receipt"),
        "expression": {"equals": ["@length(body('Find_receipt')?['value'])", 0]},
        "actions": {
            "Create_receipt": create_receipt,
            "Set_created_id": set_variable(
                "RowId", "@body('Create_receipt')?['fmc_spofileid']", "Create_receipt"
            ),
        },
        "else": {
            "actions": {
                "Require_one_receipt": {
                    "type": "If",
                    "runAfter": {},
                    "expression": {"equals": ["@length(body('Find_receipt')?['value'])", 1]},
                    "actions": {
                        "Set_existing_id": set_variable(
                            "RowId", "@first(body('Find_receipt')?['value'])?['fmc_spofileid']"
                        ),
                        "Set_archived_etag": set_variable(
                            "ArchivedETag",
                            "@coalesce(first(body('Find_receipt')?['value'])?['fmc_etag'], '')",
                            "Set_existing_id",
                        ),
                    },
                    "else": {"actions": {"Duplicate_receipt": compose("SPO-001 duplicate source key")}},
                }
            }
        },
    }

    archive_changed = {
        "type": "If",
        "runAfter": after("Resolve_receipt"),
        "expression": {"not": {"equals": ["@variables('ArchivedETag')", etag]}},
        "actions": {
            "Require_valid_metadata": {
                "type": "If",
                "runAfter": {},
                "expression": {
                    "and": [
                        {"not": {"equals": [etag, ""]}},
                        {"not": {"equals": [etag, None]}},
                        {"greater": [size, 0]},
                        {"lessOrEquals": [size, MAX_FILE_BYTES]},
                    ]
                },
                "actions": {
                    "Mark_processing": update(
                        {
                            "fmc_status": 789110001,
                            "fmc_importstatus": 789111000,
                            "fmc_candidateetag": etag,
                            "fmc_name": filename,
                            "fmc_filename": filename,
                            "fmc_sharepointidentifier": identifier,
                            "fmc_sharepointpath": full_path,
                            "fmc_sharepointurl": link,
                            "fmc_filesize": size,
                            "fmc_receivedat": "@utcNow()",
                            "fmc_runid": "@workflow()?['run']?['name']",
                            "fmc_errormessage": None,
                        }
                    ),
                    "Get_content": action(
                        SP,
                        "GetFileContent",
                        {
                            "dataset": "@parameters('SpoSiteUrl')",
                            "id": identifier,
                            "inferContentType": False,
                        },
                        "Mark_processing",
                    ),
                    "Metadata_after": action(
                        SP,
                        "GetFileMetadata",
                        {"dataset": "@parameters('SpoSiteUrl')", "id": identifier},
                        "Get_content",
                    ),
                    "Require_stable_version": {
                        "type": "If",
                        "runAfter": after("Metadata_after"),
                        "expression": {"equals": [etag, "@body('Metadata_after')?['ETag']"]},
                        "actions": {
                            "Upload_file": action(
                                DV,
                                "UpdateEntityFileImageFieldContent",
                                {
                                    "entityName": entity_set,
                                    "recordId": "@variables('RowId')",
                                    "fileImageFieldName": "fmc_file",
                                    "item": "@body('Get_content')",
                                    "x-ms-file-name": filename,
                                },
                            ),
                            "Mark_archived": update(
                                {
                                    "fmc_status": 789110002,
                                    "fmc_importstatus": 789111000,
                                    "fmc_etag": etag,
                                    "fmc_candidateetag": None,
                                    "fmc_rowcount": 0,
                                    "fmc_processedat": "@utcNow()",
                                    "fmc_errormessage": None,
                                },
                                "Upload_file",
                            ),
                        },
                        "else": {
                            "actions": {
                                "Mark_version_changed": update(
                                    {
                                        "fmc_status": 789110003,
                                        "fmc_errormessage": "SPO-003: Source changed while its content was being read.",
                                    }
                                )
                            }
                        },
                    },
                },
                "else": {
                    "actions": {
                        "Reject_metadata": update(
                            {
                                "fmc_status": 789110003,
                                "fmc_errormessage": f"SPO-002: Missing ETag or file is outside 1..{MAX_FILE_BYTES} bytes.",
                            }
                        )
                    }
                },
            }
        },
        "else": {"actions": {"Skip_unchanged": compose("ETag already archived")}},
    }

    try_actions = {
        "Source_key": compose(source_key),
        "Metadata_before": action(
            SP,
            "GetFileMetadata",
            {"dataset": "@parameters('SpoSiteUrl')", "id": identifier},
            "Source_key",
        ),
        "Find_receipt": action(
            DV,
            "ListRecords",
            {
                "entityName": entity_set,
                "$select": "fmc_spofileid,fmc_etag,fmc_importedetag,fmc_importstatus",
                "$top": 2,
                "$filter": "@concat('fmc_sourcekey eq ', decodeUriComponent('%27'), outputs('Source_key'), decodeUriComponent('%27'))",
            },
            "Metadata_before",
        ),
        "Resolve_receipt": resolve_receipt,
        "Archive_changed_version": archive_changed,
    }
    supported_branch = {
        "type": "If",
        "runAfter": after("Reset_archived_etag"),
        "expression": supported,
        "actions": {
            "Try_file": {"type": "Scope", "runAfter": {}, "actions": try_actions},
            "Catch_file": {
                "type": "Scope",
                "runAfter": after("Try_file", ["Failed", "TimedOut"]),
                "actions": {
                    "Has_receipt": {
                        "type": "If",
                        "runAfter": {},
                        "expression": {"not": {"equals": ["@variables('RowId')", ""]}},
                        "actions": {
                            "Mark_failed": update(
                                {
                                    "fmc_status": 789110003,
                                    "fmc_runid": "@workflow()?['run']?['name']",
                                    "fmc_errormessage": "SPO-SCAN: Connector action failed; inspect this flow run.",
                                }
                            )
                        },
                        "else": {"actions": {}},
                    }
                },
            },
        },
        "else": {"actions": {"Skip_unsupported": compose("Not an enabled FMC-Inbox source file")}},
    }

    definition = {
        "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
        "contentVersion": "1.0.0.0",
        "parameters": {
            "$authentication": {"defaultValue": {}, "type": "SecureObject"},
            "$connections": {"defaultValue": {}, "type": "Object"},
        },
        "triggers": {
            "When_SPO_sync_requested": {
                "type": "OpenApiConnectionWebhook",
                "inputs": {
                    "host": {
                        "connectionName": DV,
                        "operationId": "BusinessEventsTrigger",
                        "apiId": "/providers/Microsoft.PowerApps/apis/" + DV,
                    },
                    "parameters": {
                        "catalog": catalog_id,
                        "category": category_id,
                        "subscriptionRequest/entityname": "none",
                        "subscriptionRequest/sdkmessagename": API_NAME,
                    },
                    "authentication": "@parameters('$authentication')",
                },
            }
        },
        "actions": {
            "Initialize_row_id": {
                "type": "InitializeVariable",
                "runAfter": {},
                "inputs": {"variables": [{"name": "RowId", "type": "string", "value": ""}]},
            },
            "Initialize_archived_etag": {
                "type": "InitializeVariable",
                "runAfter": after("Initialize_row_id"),
                "inputs": {"variables": [{"name": "ArchivedETag", "type": "string", "value": ""}]},
            },
            "List_files": action(
                SP,
                "GetFileItems",
                {
                    "dataset": "@parameters('SpoSiteUrl')",
                    "table": "@parameters('SpoLibraryId')",
                    "folderPath": "@parameters('SpoInboxPath')",
                    "viewScopeOption": "RecursiveAll",
                    "$top": 5000,
                },
                "Initialize_archived_etag",
            ),
            "For_each_file": {
                "type": "Foreach",
                "foreach": "@body('List_files')?['value']",
                "runAfter": after("List_files"),
                "runtimeConfiguration": {"concurrency": {"repetitions": 1}},
                "actions": {
                    "Reset_row_id": set_variable("RowId", ""),
                    "Reset_archived_etag": set_variable("ArchivedETag", "", "Reset_row_id"),
                    "Supported_source_file": supported_branch,
                },
            },
            "Scan_receipt": compose(
                {
                    "requestId": "@triggerBody()?['RequestId']",
                    "finishedAt": "@utcNow()",
                    "listed": "@length(body('List_files')?['value'])",
                },
                "For_each_file",
            ),
        },
    }
    for name, schema, value in [
        ("SpoSiteUrl", "fmc_SpoSiteUrl", SITE),
        ("SpoLibraryId", "fmc_SpoLibraryId", LIBRARY),
        ("SpoInboxPath", "fmc_SpoInboxPath", INBOX),
        ("SpoSourceNamespace", "fmc_SpoSourceNamespace", NAMESPACE),
    ]:
        definition["parameters"][name] = {
            "type": "String",
            "defaultValue": value,
            "metadata": {"schemaName": schema},
        }
    refs = {
        connector: {
            "runtimeSource": "embedded",
            "connection": {"name": connection, "connectionReferenceLogicalName": logical},
            "api": {"name": connector},
        }
        for connector, (logical, connection) in CONNECTIONS.items()
    }
    return {"properties": {"definition": definition, "connectionReferences": refs}, "schemaVersion": "1.0.0.0"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["render", "deploy", "enable", "disable", "verify", "test"])
    args = parser.parse_args()
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    appsettings = json.loads((ROOT / "DataverseSyncWorker/appsettings.json").read_text(encoding="utf-8-sig"))
    token_python = appsettings["Dataverse"]["DeveloperTokenPython"]
    token = subprocess.check_output(
        [token_python, str(ROOT / "scripts/get-dataverse-token.py")], text=True
    ).strip()
    session = requests.Session()
    session.headers.update(
        {
            "Authorization": "Bearer " + token,
            "Accept": "application/json",
            "OData-Version": "4.0",
            "OData-MaxVersion": "4.0",
        }
    )

    def api(method, path, **kwargs):
        response = session.request(method, ORG + "/api/data/v9.2/" + path, timeout=120, **kwargs)
        if not response.ok:
            raise RuntimeError(f"{method} {path}: {response.status_code} {response.text[:3000]}")
        return response.json() if response.content else {}

    def rows(table, filter_text, select):
        return api("GET", table, params={"$filter": filter_text, "$select": select})["value"]

    def ensure_row(table, key, filter_text, body):
        found = rows(table, filter_text, key)
        if len(found) > 1:
            raise RuntimeError(f"Ambiguous {table}: {filter_text}")
        if found:
            return found[0][key]
        row_id = str(uuid.uuid4())
        api("POST", table, json={key: row_id, **body})
        return row_id

    if api("GET", "WhoAmI")["OrganizationId"].lower() != ORG_ID:
        raise RuntimeError("Unexpected Dataverse organization")
    if len(rows("solutions", "uniquename eq 'FMCentralBms'", "solutionid")) != 1:
        raise RuntimeError("Expected one FMCentralBms solution")
    custom_apis = rows(
        "customapis", f"uniquename eq '{API_NAME}'", "customapiid,allowedcustomprocessingsteptype"
    )
    if len(custom_apis) != 1 or custom_apis[0]["allowedcustomprocessingsteptype"] != 1:
        raise RuntimeError("Deploy fmc_RequestSpoSync with Async Only processing first")
    custom_api_id = custom_apis[0]["customapiid"]
    catalog_id = ensure_row(
        "catalogs",
        "catalogid",
        "uniquename eq 'fmc_BmsDemoEvents'",
        {
            "name": "FMC BMS Demo Events",
            "displayname": "FMC BMS Demo",
            "description": "Business events exposed by the FMC BMS Demo solution.",
            "uniquename": "fmc_BmsDemoEvents",
        },
    )
    category_id = ensure_row(
        "catalogs",
        "catalogid",
        "uniquename eq 'fmc_BmsAppEvents'",
        {
            "name": "FMC BMS App Events",
            "displayname": "App Events",
            "description": "Events raised by commands in the FMC BMS Demo app.",
            "uniquename": "fmc_BmsAppEvents",
            "ParentCatalogId@odata.bind": f"/catalogs({catalog_id})",
        },
    )
    ensure_row(
        "catalogassignments",
        "catalogassignmentid",
        f"_catalogid_value eq {category_id} and _object_value eq {custom_api_id}",
        {
            "name": "Request SharePoint Sync",
            "CatalogId@odata.bind": f"/catalogs({category_id})",
            "CustomAPIId@odata.bind": f"/customapis({custom_api_id})",
        },
    )
    entity_set = api(
        "GET", "EntityDefinitions(LogicalName='fmc_spofile')", params={"$select": "EntitySetName"}
    )["EntitySetName"]
    clientdata = build(entity_set, catalog_id, category_id)
    rendered = ARTIFACTS / "clientdata.json"

    if args.mode == "render":
        rendered.write_text(json.dumps(clientdata, indent=2) + "\n", encoding="utf-8")
        refs = {
            connector: {
                "id": "/providers/Microsoft.PowerApps/apis/" + connector,
                "connectionName": connection,
                "source": "Embedded",
            }
            for connector, (_, connection) in CONNECTIONS.items()
        }
        (ARTIFACTS / "validate_flow.args.json").write_text(
            json.dumps({"definition": clientdata["properties"]["definition"], "connectionRefs": refs}, indent=2),
            encoding="utf-8",
        )
        (ARTIFACTS / "preflight_flow.args.json").write_text(
            json.dumps(
                {
                    "env": ENV,
                    "definition": clientdata["properties"]["definition"],
                    "connectionReferences": refs,
                },
                indent=2,
            ),
            encoding="utf-8",
        )
        print(json.dumps({"rendered": str(rendered), "entitySet": entity_set}))
        return

    flows = rows(
        "workflows",
        f"category eq 5 and type eq 1 and name eq '{FLOW_NAME}'",
        "workflowid,name,statecode,clientdata",
    )
    if len(flows) > 1:
        raise RuntimeError("Duplicate SPO sync flow names")
    flow_id = flows[0]["workflowid"] if flows else str(uuid.uuid4())

    if args.mode == "deploy":
        if not rendered.exists() or json.loads(rendered.read_text(encoding="utf-8")) != clientdata:
            raise RuntimeError("Render the current definition before deployment")
        relationships = api(
            "GET",
            "EntityDefinitions(LogicalName='environmentvariablevalue')/ManyToOneRelationships",
            params={
                "$select": "ReferencingAttribute,ReferencingEntityNavigationPropertyName",
                "$filter": "ReferencingAttribute eq 'environmentvariabledefinitionid'",
            },
        )["value"]
        if len(relationships) != 1:
            raise RuntimeError("Environment variable navigation metadata is ambiguous")
        definition_navigation = relationships[0]["ReferencingEntityNavigationPropertyName"]
        for schema, value in [
            ("fmc_SpoSiteUrl", SITE),
            ("fmc_SpoLibraryId", LIBRARY),
            ("fmc_SpoInboxPath", INBOX),
            ("fmc_SpoSourceNamespace", NAMESPACE),
        ]:
            definitions = rows(
                "environmentvariabledefinitions",
                f"schemaname eq '{schema}'",
                "environmentvariabledefinitionid",
            )
            if len(definitions) != 1:
                raise RuntimeError("Expected one environment variable definition: " + schema)
            definition_id = definitions[0]["environmentvariabledefinitionid"]
            values = rows(
                "environmentvariablevalues",
                f"_environmentvariabledefinitionid_value eq {definition_id}",
                "environmentvariablevalueid,value",
            )
            if len(values) > 1:
                raise RuntimeError("Duplicate current environment variable values: " + schema)
            if values:
                if values[0].get("value") != value:
                    api(
                        "PATCH",
                        f"environmentvariablevalues({values[0]['environmentvariablevalueid']})",
                        json={"value": value},
                    )
            else:
                api(
                    "POST",
                    "environmentvariablevalues",
                    json={
                        "value": value,
                        definition_navigation + "@odata.bind": f"/environmentvariabledefinitions({definition_id})",
                    },
                )
        for connector, (logical, connection) in CONNECTIONS.items():
            found = rows(
                "connectionreferences",
                f"connectionreferencelogicalname eq '{logical}'",
                "connectionreferenceid,connectionid,connectorid",
            )
            if len(found) != 1 or found[0].get("connectionid") != connection:
                raise RuntimeError(f"Connection reference is missing or differs: {logical}")
        payload = {
            "name": FLOW_NAME,
            "category": 5,
            "type": 1,
            "primaryentity": "none",
            "description": "Scans FMC-Inbox and archives only new or updated files by stable source key plus ETag.",
            "clientdata": json.dumps(clientdata, separators=(",", ":")),
        }
        if flows:
            if flows[0]["statecode"] == 1:
                api("PATCH", f"workflows({flow_id})", json={"statecode": 0})
            api("PATCH", f"workflows({flow_id})", json=payload)
        else:
            api(
                "POST",
                "workflows",
                json={"workflowid": flow_id, **payload},
                headers={"MSCRM.SolutionUniqueName": SOLUTION},
            )
        api(
            "POST",
            "AddSolutionComponent",
            json={
                "ComponentId": flow_id,
                "ComponentType": 29,
                "SolutionUniqueName": SOLUTION,
                "AddRequiredComponents": True,
            },
        )
        api("PATCH", f"workflows({flow_id})", json={"statecode": 1})
        deploy_page(api, rows, flow_id)
    elif args.mode in ("enable", "disable"):
        if len(flows) != 1:
            raise RuntimeError("Deploy the flow first")
        api("PATCH", f"workflows({flow_id})", json={"statecode": 1 if args.mode == "enable" else 0})
    elif args.mode == "test":
        request_id = str(uuid.uuid4())
        result = api("POST", API_NAME, json={"ClientRequestId": request_id})
        if not result.get("Accepted") or result.get("RequestId", "").lower() != request_id:
            raise RuntimeError("Custom API did not accept the supplied request ID")
        print(json.dumps({"accepted": True, "requestId": request_id, "message": result.get("Message")}))

    verify(api, rows, flow_id)


def deploy_page(api, rows, spo_flow_id):
    sql_flows = rows(
        "workflows",
        f"category eq 5 and type eq 1 and name eq '{SQL_FLOW_NAME}'",
        "workflowid",
    )
    if len(sql_flows) != 1:
        raise RuntimeError("Expected one SQL demo flow before deploying the shared page")
    full_flows = rows(
        "workflows",
        f"category eq 5 and type eq 1 and name eq '{FULL_FLOW_NAME}'",
        "workflowid",
    )
    if len(full_flows) > 1:
        raise RuntimeError("Found duplicate Full Sync flows")
    full_flow_id = full_flows[0]["workflowid"] if full_flows else "__FULL_FLOW_ID__"
    html = (ROOT / "dataverse/app-source/BmsEventDemo.html").read_text(encoding="utf-8")
    html = (
        html.replace("__ENVIRONMENT_ID__", ENV)
        .replace("__FLOW_ID__", sql_flows[0]["workflowid"])
        .replace("__SPO_FLOW_ID__", spo_flow_id)
        .replace("__FULL_FLOW_ID__", full_flow_id)
    )
    resources = rows("webresourceset", f"name eq '{RESOURCE_NAME}'", "webresourceid")
    body = {
        "name": RESOURCE_NAME,
        "displayname": "BMS Power Automate Event Demo",
        "webresourcetype": 1,
        "content": __import__("base64").b64encode(html.encode("utf-8")).decode("ascii"),
    }
    if len(resources) > 1:
        raise RuntimeError("Duplicate BmsEventDemo web resources")
    if resources:
        resource_id = resources[0]["webresourceid"]
        api("PATCH", f"webresourceset({resource_id})", json={"content": body["content"]})
    else:
        resource_id = str(uuid.uuid4())
        api(
            "POST",
            "webresourceset",
            json={"webresourceid": resource_id, **body},
            headers={"MSCRM.SolutionUniqueName": SOLUTION},
        )
    api(
        "POST",
        "AddSolutionComponent",
        json={
            "ComponentId": resource_id,
            "ComponentType": 61,
            "SolutionUniqueName": SOLUTION,
            "AddRequiredComponents": True,
        },
    )
    api("POST", "PublishXml", json={"ParameterXml": f"<importexportxml><webresources><webresource>{resource_id}</webresource></webresources></importexportxml>"})


def verify(api, rows, flow_id):
    flow = api("GET", f"workflows({flow_id})", params={"$select": "workflowid,name,statecode,clientdata"})
    trigger = json.loads(flow["clientdata"])["properties"]["definition"]["triggers"]["When_SPO_sync_requested"]
    if flow["statecode"] != 1 or trigger["inputs"]["parameters"]["subscriptionRequest/sdkmessagename"] != API_NAME:
        raise RuntimeError("SPO sync flow is inactive or has the wrong business-event trigger")
    callbacks = rows(
        "callbackregistrations",
        f"sdkmessagename eq '{API_NAME}'",
        "callbackregistrationid,sdkmessagename,entityname,softdeletestatus",
    )
    if len(callbacks) != 1 or callbacks[0]["entityname"] != "none" or callbacks[0]["softdeletestatus"] != 0:
        raise RuntimeError("Active Power Automate callback registration is missing")
    print(
        json.dumps(
            {
                "organization": ORG_ID,
                "api": API_NAME,
                "flow": FLOW_NAME,
                "flowId": flow_id,
                "state": flow["statecode"],
                "callbackId": callbacks[0]["callbackregistrationid"],
                "appUrl": ORG + "/main.aspx?appid=d19f4897-d227-4df3-8361-988f97c53e89",
            }
        )
    )


if __name__ == "__main__":
    main()
