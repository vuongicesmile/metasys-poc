"""Provision SharePoint change detection, automatic dispatch and scoped SPO sync dispatch.

All deployment calls verify the checked-in Developer organization and add the
three flows to FMCentralBms. The actual file archive remains in the existing
FMC - Request SPO Sync flow and the local SPO worker remains the importer.
"""
import argparse
import json
import subprocess
import sys
import uuid
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / ".artifacts/spo-change-flows"
ORG = "https://org06cbc9ec.crm5.dynamics.com"
ENV = "5abcb0e5-99b2-e51f-aa0e-90d84405798b"
ORG_ID = "ab191700-b99e-f111-aaa0-000d3a80bb96"
SOLUTION = "FMCentralBms"
SITE = "https://titancorpvncom.sharepoint.com/sites/Powerplatform"
LIBRARY = "7d9282a4-c45d-4ef8-8719-18f78e71d0d0"
INBOX = "/Shared Documents/FMC-Inbox"
NAMESPACE = "bms_spo_dev01"
SP = "shared_sharepointonline"
DV = "shared_commondataserviceforapps"
DETECT_API = "fmc_DetectSpoChange"
CLAIM_API = "fmc_ClaimSpoChange"
SPO_API = "fmc_RequestSpoSync"
DETECT_FLOW = "FMC - Detect SPO File Change"
AUTO_FLOW = "FMC - Dispatch Due SPO Changes"
DISPATCH_FLOW = "FMC - Dispatch Claimed SPO Change"
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


def unbound(action_name, values, previous=None):
    return action(DV, "PerformUnboundAction", {"actionName": action_name, **{"item/" + key: value for key, value in values.items()}}, previous)


def supported_path(path):
    return (
        "@and(endsWith(toLower(" + path + "), '.xlsx'),or("
        "startsWith(toLower(" + path + "), toLower(concat(parameters('SpoInboxPath'), '/01-Master/Building/'))),"
        "startsWith(toLower(" + path + "), toLower(concat(parameters('SpoInboxPath'), '/01-Master/Equipment/'))),"
        "startsWith(toLower(" + path + "), toLower(concat(parameters('SpoInboxPath'), '/01-Master/ElectricMeter/'))),"
        "startsWith(toLower(" + path + "), toLower(concat(parameters('SpoInboxPath'), '/01-Master/WaterMeter/'))),"
        "startsWith(toLower(" + path + "), toLower(concat(parameters('SpoInboxPath'), '/02-Telemetry/Electricity/'))),"
        "startsWith(toLower(" + path + "), toLower(concat(parameters('SpoInboxPath'), '/02-Telemetry/Water/')))))"
    )


def parameters():
    result = {
        "$authentication": {"defaultValue": {}, "type": "SecureObject"},
        "$connections": {"defaultValue": {}, "type": "Object"},
    }
    for name, schema, value in [
        ("SpoSiteUrl", "fmc_SpoSiteUrl", SITE),
        ("SpoLibraryId", "fmc_SpoLibraryId", LIBRARY),
        ("SpoInboxPath", "fmc_SpoInboxPath", INBOX),
        ("SpoSourceNamespace", "fmc_SpoSourceNamespace", NAMESPACE),
    ]:
        result[name] = {"type": "String", "defaultValue": value, "metadata": {"schemaName": schema}}
    return result


def clientdata(definition):
    refs = {
        connector: {
            "runtimeSource": "embedded",
            "connection": {"name": connection, "connectionReferenceLogicalName": logical},
            "api": {"name": connector},
        }
        for connector, (logical, connection) in CONNECTIONS.items()
    }
    return {"properties": {"definition": definition, "connectionReferences": refs}, "schemaVersion": "1.0.0.0"}


def detect_definition():
    trigger_identifier = "@triggerBody()?['{Identifier}']"
    trigger_path = "concat('/', triggerBody()?['{FullPath}'])"
    return {
        "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
        "contentVersion": "1.0.0.0",
        "parameters": parameters(),
        "triggers": {
            "When_file_created_or_modified": {
                "type": "OpenApiConnection",
                "recurrence": {"frequency": "Minute", "interval": 1},
                "splitOn": "@triggerBody()?['value']",
                "inputs": {
                    "host": {"apiId": "/providers/Microsoft.PowerApps/apis/" + SP, "connectionName": SP, "operationId": "GetOnUpdatedFileItems"},
                    # Watch the library so the supported inbox subfolders are
                    # included; Enabled_xlsx_source performs the exact path filter.
                    "parameters": {"dataset": "@parameters('SpoSiteUrl')", "table": "@parameters('SpoLibraryId')"},
                    "authentication": "@parameters('$authentication')",
                },
            }
        },
        "actions": {
            "Get_file_metadata": action(SP, "GetFileMetadata", {"dataset": "@parameters('SpoSiteUrl')", "id": trigger_identifier}),
            "Enabled_xlsx_source": {
                "type": "If",
                "runAfter": after("Get_file_metadata"),
                "expression": supported_path(trigger_path),
                "actions": {
                    "Record_debounced_change": unbound(DETECT_API, {
                        "SourceKey": "@concat(parameters('SpoSourceNamespace'), '_', toLower(parameters('SpoLibraryId')), '_', string(triggerBody()?['ID']))",
                        "LibraryId": "@parameters('SpoLibraryId')",
                        "ItemId": "@string(triggerBody()?['ID'])",
                        "SharePointIdentifier": trigger_identifier,
                        "SharePointPath": "@" + trigger_path,
                        "FileName": "@triggerBody()?['{FilenameWithExtension}']",
                        "ExpectedETag": "@body('Get_file_metadata')?['ETag']",
                    }),
                },
                "else": {"actions": {"Skip_unsupported": {"type": "Compose", "inputs": "Not an enabled FMC-Inbox .xlsx source", "runAfter": {}}}},
            },
        },
    }


def automatic_definition(entity_set):
    return {
        "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
        "contentVersion": "1.0.0.0",
        "parameters": parameters(),
        "triggers": {"Every_minute": {"type": "Recurrence", "recurrence": {"frequency": "Minute", "interval": 1}}},
        "actions": {
            "List_due_changes": action(DV, "ListRecords", {
                "entityName": entity_set,
                "$select": "fmc_spochangerequestid,fmc_expectedetag",
                "$filter": "@concat('fmc_status eq 789112000 and fmc_dueat le ', utcNow())",
                "$orderby": "fmc_dueat asc",
                "$top": 25,
            }),
            "Claim_each_due_change": {
                "type": "Foreach",
                "foreach": "@body('List_due_changes')?['value']",
                "runAfter": after("List_due_changes"),
                "runtimeConfiguration": {"concurrency": {"repetitions": 1}},
                "actions": {
                    "Claim_for_auto_sync": unbound(CLAIM_API, {
                        "ChangeRequestId": "@items('Claim_each_due_change')?['fmc_spochangerequestid']",
                        "ExpectedETag": "@items('Claim_each_due_change')?['fmc_expectedetag']",
                        "DispatchSource": "Auto",
                        "ClientRequestId": "@guid()",
                    }),
                },
            },
        },
    }


def dispatch_definition(claim_catalog, claim_category, entity_set):
    output = "@triggerBody()?['OutputParameters']?['"
    return {
        "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
        "contentVersion": "1.0.0.0",
        "parameters": parameters(),
        "triggers": {
            "When_SPO_change_claimed": {
                "type": "OpenApiConnectionWebhook",
                "inputs": {
                    "host": {"connectionName": DV, "operationId": "BusinessEventsTrigger", "apiId": "/providers/Microsoft.PowerApps/apis/" + DV},
                    "parameters": {"catalog": claim_catalog, "category": claim_category, "subscriptionRequest/entityname": "none", "subscriptionRequest/sdkmessagename": CLAIM_API},
                    "authentication": "@parameters('$authentication')",
                },
            }
        },
        "actions": {
            "Claim_won": {
                "type": "If",
                "runAfter": {},
                "expression": {"equals": [output + "Accepted']", True]},
                "actions": {
                    "Dispatch_scoped_sync": {
                        "type": "Scope",
                        "runAfter": {},
                        "actions": {
                            "Request_exact_file_sync": unbound(SPO_API, {
                                "ClientRequestId": "@string(triggerBody()?['OutputParameters']?['DispatchId'])",
                                "SourceKey": output + "SourceKey']",
                                "ExpectedETag": output + "ExpectedETag']",
                                "SharePointIdentifier": output + "SharePointIdentifier']",
                            }),
                            "Record_dispatch_run": action(DV, "UpdateOnlyRecord", {
                                "entityName": entity_set,
                                "recordId": output + "RequestId']",
                                "item/fmc_flowrunid": "@workflow()?['run']?['name']",
                            }, "Request_exact_file_sync"),
                        },
                    },
                    "Record_dispatch_failure": {
                        "type": "Scope",
                        "runAfter": after("Dispatch_scoped_sync", ["Failed", "TimedOut"]),
                        "actions": {
                            "Mark_failed": action(DV, "UpdateOnlyRecord", {
                                "entityName": entity_set,
                                "recordId": output + "RequestId']",
                                "item/fmc_status": 789112004,
                                "item/fmc_errormessage": "SPO-DISPATCH: scoped archive event failed; inspect this flow run.",
                                "item/fmc_flowrunid": "@workflow()?['run']?['name']",
                            }),
                        },
                    },
                },
                "else": {"actions": {"Already_claimed": {"type": "Compose", "inputs": "Another manual or automatic dispatch won the claim.", "runAfter": {}}}},
            },
        },
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["render", "deploy", "enable", "disable", "verify"])
    args = parser.parse_args()
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    appsettings = json.loads((ROOT / "Dataverse.SyncWorker/Dataverse.SyncWorker.App/appsettings.json").read_text(encoding="utf-8-sig"))
    token = subprocess.check_output([appsettings["Dataverse"]["DeveloperTokenPython"], str(ROOT / "scripts/get-dataverse-token.py")], text=True).strip()
    session = requests.Session()
    session.headers.update({"Authorization": "Bearer " + token, "Accept": "application/json", "OData-Version": "4.0", "OData-MaxVersion": "4.0"})

    def api(method, path, **kwargs):
        response = session.request(method, ORG + "/api/data/v9.2/" + path, timeout=120, **kwargs)
        if not response.ok:
            raise RuntimeError(f"{method} {path}: {response.status_code} {response.text[:3000]}")
        return response.json() if response.content else {}

    def rows(table, filter_text, select):
        return api("GET", table, params={"$filter": filter_text, "$select": select})["value"]

    def ensure_row(table, key, filter_text, body):
        found = rows(table, filter_text, key)
        if len(found) > 1: raise RuntimeError(f"Ambiguous {table}: {filter_text}")
        if found: return found[0][key]
        item = str(uuid.uuid4())
        api("POST", table, json={key: item, **body})
        return item

    if api("GET", "WhoAmI")["OrganizationId"].lower() != ORG_ID: raise RuntimeError("Unexpected Dataverse organization")
    if len(rows("solutions", "uniquename eq 'FMCentralBms'", "solutionid")) != 1: raise RuntimeError("Expected one FMCentralBms solution")
    apis = {name: rows("customapis", f"uniquename eq '{name}'", "customapiid,allowedcustomprocessingsteptype") for name in (DETECT_API, CLAIM_API, SPO_API)}
    if any(len(value) != 1 or value[0]["allowedcustomprocessingsteptype"] != 1 for value in apis.values()): raise RuntimeError("Deploy the SPO change Custom APIs with Async Only processing first")
    entity_set = api("GET", "EntityDefinitions(LogicalName='fmc_spochangerequest')", params={"$select": "EntitySetName"})["EntitySetName"]
    catalog = ensure_row("catalogs", "catalogid", "uniquename eq 'fmc_BmsDemoEvents'", {"name": "FMC BMS Demo Events", "displayname": "FMC BMS Demo", "uniquename": "fmc_BmsDemoEvents"})
    category = ensure_row("catalogs", "catalogid", "uniquename eq 'fmc_BmsAppEvents'", {"name": "FMC BMS App Events", "displayname": "App Events", "uniquename": "fmc_BmsAppEvents", "ParentCatalogId@odata.bind": f"/catalogs({catalog})"})
    ensure_row("catalogassignments", "catalogassignmentid", f"_catalogid_value eq {category} and _object_value eq {apis[CLAIM_API][0]['customapiid']}", {"name": "Claim SharePoint File Change", "CatalogId@odata.bind": f"/catalogs({category})", "CustomAPIId@odata.bind": f"/customapis({apis[CLAIM_API][0]['customapiid']})"})
    definitions = {
        DETECT_FLOW: clientdata(detect_definition()),
        AUTO_FLOW: clientdata(automatic_definition(entity_set)),
        DISPATCH_FLOW: clientdata(dispatch_definition(catalog, category, entity_set)),
    }
    for name, definition in definitions.items():
        (ARTIFACTS / (name.replace(" ", "-") + ".json")).write_text(json.dumps(definition, indent=2) + "\n", encoding="utf-8")
    if args.mode == "render":
        print(json.dumps({"rendered": [str(path) for path in ARTIFACTS.glob("*.json")], "entitySet": entity_set}))
        return

    for connector, (logical, connection) in CONNECTIONS.items():
        found = rows("connectionreferences", f"connectionreferencelogicalname eq '{logical}'", "connectionreferenceid,connectionid")
        if len(found) != 1 or found[0].get("connectionid") != connection: raise RuntimeError(f"Connection reference is missing or differs: {logical}")
    workflows = {name: rows("workflows", f"category eq 5 and type eq 1 and name eq '{name}'", "workflowid,name,statecode,clientdata") for name in definitions}
    if any(len(value) > 1 for value in workflows.values()): raise RuntimeError("Duplicate SPO change flow names")
    if args.mode == "deploy":
        for name, definition in definitions.items():
            found = workflows[name]
            flow_id = found[0]["workflowid"] if found else str(uuid.uuid4())
            payload = {"name": name, "category": 5, "type": 1, "primaryentity": "none", "description": "FMCentralBms SharePoint file-change debounce and scoped dispatch.", "clientdata": json.dumps(definition, separators=(",", ":"))}
            if found:
                if found[0]["statecode"] == 1: api("PATCH", f"workflows({flow_id})", json={"statecode": 0})
                api("PATCH", f"workflows({flow_id})", json=payload)
            else:
                api("POST", "workflows", json={"workflowid": flow_id, **payload}, headers={"MSCRM.SolutionUniqueName": SOLUTION})
            api("POST", "AddSolutionComponent", json={"ComponentId": flow_id, "ComponentType": 29, "SolutionUniqueName": SOLUTION, "AddRequiredComponents": True})
            api("PATCH", f"workflows({flow_id})", json={"statecode": 1})
    elif args.mode in ("enable", "disable"):
        for name, found in workflows.items():
            if len(found) != 1: raise RuntimeError("Deploy the flow first: " + name)
            api("PATCH", f"workflows({found[0]['workflowid']})", json={"statecode": 1 if args.mode == "enable" else 0})

    verified = {}
    for name in definitions:
        found = rows("workflows", f"category eq 5 and type eq 1 and name eq '{name}'", "workflowid,name,statecode,clientdata")
        if len(found) != 1 or found[0]["statecode"] != 1: raise RuntimeError("Flow missing or inactive: " + name)
        verified[name] = found[0]["workflowid"]
    callbacks = rows("callbackregistrations", f"sdkmessagename eq '{CLAIM_API}' and softdeletestatus eq 0", "callbackregistrationid,entityname")
    if len(callbacks) != 1 or callbacks[0]["entityname"] != "none": raise RuntimeError("Active Claim SPO Change callback registration is missing")
    print(json.dumps({"organization": ORG_ID, "flows": verified, "claimCallback": callbacks[0]["callbackregistrationid"]}))


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(str(error), file=sys.stderr)
        raise
