"""Provision the full-sync orchestration flow and deploy its app button.

The flow is triggered by fmc_RequestFullSync and dispatches the existing SQL
and SharePoint Custom APIs. It does not duplicate either ingestion pipeline.
"""
import argparse
import base64
import json
import subprocess
import uuid
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / ".artifacts/full-sync"
ORG = "https://org06cbc9ec.crm5.dynamics.com"
ENV = "5abcb0e5-99b2-e51f-aa0e-90d84405798b"
ORG_ID = "ab191700-b99e-f111-aaa0-000d3a80bb96"
SOLUTION = "FMCentralBms"
API_NAME = "fmc_RequestFullSync"
SQL_API_NAME = "fmc_RequestBmsSync"
SPO_API_NAME = "fmc_RequestSpoSync"
FLOW_NAME = "FMC - Request Full Sync"
SQL_FLOW_NAME = "FMC - App Request BMS Sync Event"
SPO_FLOW_NAME = "FMC - Request SPO Sync"
RESOURCE_NAME = "fmc_/pages/BmsEventDemo.html"
DV = "shared_commondataserviceforapps"
DV_CONNECTION_LOGICAL = "fmc_sharedcommondataserviceforapps"
DV_CONNECTION_ID = "shared-commondataser-ffc44f95-e5ab-44b0-acdf-47752fa72135"
ALL_STATUSES = ["Succeeded", "Failed", "Skipped", "TimedOut"]


def after(*names):
    return {name: ALL_STATUSES for name in names}


def perform_unbound_action(action_name):
    return {
        "type": "OpenApiConnection",
        "runAfter": {},
        "inputs": {
            "host": {
                "apiId": "/providers/Microsoft.PowerApps/apis/" + DV,
                "connectionName": DV,
                "operationId": "PerformUnboundAction",
            },
            "parameters": {
                "actionName": action_name,
                "item/ClientRequestId": "@string(triggerBody()?['OutputParameters']?['RequestId'])",
            },
        },
    }


def build(catalog_id, category_id):
    definition = {
        "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
        "contentVersion": "1.0.0.0",
        "parameters": {
            "$authentication": {"defaultValue": {}, "type": "SecureObject"},
            "$connections": {"defaultValue": {}, "type": "Object"},
        },
        "triggers": {
            "When_full_sync_requested": {
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
            "Request_SQL_sync": perform_unbound_action(SQL_API_NAME),
            "Request_SharePoint_sync": perform_unbound_action(SPO_API_NAME),
            "Dispatch_summary": {
                "type": "Compose",
                "runAfter": after("Request_SQL_sync", "Request_SharePoint_sync"),
                "inputs": {
                    "requestId": "@triggerBody()?['OutputParameters']?['RequestId']",
                    "dispatchedAt": "@utcNow()",
                    "sqlAction": SQL_API_NAME,
                    "sharePointAction": SPO_API_NAME,
                    "note": "Each child pipeline reports and retries independently.",
                },
            },
        },
    }
    refs = {
        DV: {
            "runtimeSource": "embedded",
            "connection": {
                "name": DV_CONNECTION_ID,
                "connectionReferenceLogicalName": DV_CONNECTION_LOGICAL,
            },
            "api": {"name": DV},
        }
    }
    return {
        "properties": {"definition": definition, "connectionReferences": refs},
        "schemaVersion": "1.0.0.0",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["render", "deploy", "enable", "disable", "verify", "test"])
    args = parser.parse_args()
    ARTIFACTS.mkdir(parents=True, exist_ok=True)

    appsettings = json.loads(
        (ROOT / "DataverseSyncWorker/appsettings.json").read_text(encoding="utf-8-sig")
    )
    token = subprocess.check_output(
        [
            appsettings["Dataverse"]["DeveloperTokenPython"],
            str(ROOT / "scripts/get-dataverse-token.py"),
        ],
        text=True,
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
        response = session.request(
            method, ORG + "/api/data/v9.2/" + path, timeout=120, **kwargs
        )
        if not response.ok:
            raise RuntimeError(
                f"{method} {path}: {response.status_code} {response.text[:3000]}"
            )
        return response.json() if response.content else {}

    def rows(table, filter_text, select):
        return api(
            "GET", table, params={"$filter": filter_text, "$select": select}
        )["value"]

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

    custom_apis = {}
    for api_name in (API_NAME, SQL_API_NAME, SPO_API_NAME):
        found = rows(
            "customapis",
            f"uniquename eq '{api_name}'",
            "customapiid,allowedcustomprocessingsteptype,workflowsdkstepenabled",
        )
        if (
            len(found) != 1
            or found[0]["allowedcustomprocessingsteptype"] != 1
            or not found[0]["workflowsdkstepenabled"]
        ):
            raise RuntimeError(
                f"Deploy workflow-enabled Async Only Custom API {api_name} first"
            )
        custom_apis[api_name] = found[0]["customapiid"]

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
        f"_catalogid_value eq {category_id} and _object_value eq {custom_apis[API_NAME]}",
        {
            "name": "Request Full Sync",
            "CatalogId@odata.bind": f"/catalogs({category_id})",
            "CustomAPIId@odata.bind": f"/customapis({custom_apis[API_NAME]})",
        },
    )

    connections = rows(
        "connectionreferences",
        f"connectionreferencelogicalname eq '{DV_CONNECTION_LOGICAL}'",
        "connectionreferenceid,connectionid,connectorid",
    )
    if len(connections) != 1 or connections[0].get("connectionid") != DV_CONNECTION_ID:
        raise RuntimeError("Dataverse connection reference is missing or differs")

    clientdata = build(catalog_id, category_id)
    rendered = ARTIFACTS / "clientdata.json"
    if args.mode == "render":
        rendered.write_text(json.dumps(clientdata, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"rendered": str(rendered), "api": API_NAME}))
        return

    flows = rows(
        "workflows",
        f"category eq 5 and type eq 1 and name eq '{FLOW_NAME}'",
        "workflowid,name,statecode,clientdata",
    )
    if len(flows) > 1:
        raise RuntimeError("Duplicate full-sync flow names")
    flow_id = flows[0]["workflowid"] if flows else str(uuid.uuid4())

    if args.mode == "deploy":
        if not rendered.exists() or json.loads(rendered.read_text(encoding="utf-8")) != clientdata:
            raise RuntimeError("Render the current definition before deployment")
        payload = {
            "name": FLOW_NAME,
            "category": 5,
            "type": 1,
            "primaryentity": "none",
            "description": "Dispatches the existing SQL and SharePoint synchronization Custom APIs from one app request.",
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
            raise RuntimeError("Deploy the full-sync flow first")
        api(
            "PATCH",
            f"workflows({flow_id})",
            json={"statecode": 1 if args.mode == "enable" else 0},
        )
    elif args.mode == "test":
        request_id = str(uuid.uuid4())
        result = api("POST", API_NAME, json={"ClientRequestId": request_id})
        if not result.get("Accepted") or result.get("RequestId", "").lower() != request_id:
            raise RuntimeError("Full-sync Custom API did not accept the supplied request ID")
        print(
            json.dumps(
                {"accepted": True, "requestId": request_id, "message": result.get("Message")}
            )
        )

    verify(api, rows, flow_id)


def find_flow_id(rows, name):
    found = rows(
        "workflows",
        f"category eq 5 and type eq 1 and name eq '{name}'",
        "workflowid",
    )
    if len(found) != 1:
        raise RuntimeError(f"Expected one flow named {name}")
    return found[0]["workflowid"]


def deploy_page(api, rows, full_flow_id):
    sql_flow_id = find_flow_id(rows, SQL_FLOW_NAME)
    spo_flow_id = find_flow_id(rows, SPO_FLOW_NAME)
    html = (ROOT / "dataverse/app-source/BmsEventDemo.html").read_text(encoding="utf-8")
    html = (
        html.replace("__ENVIRONMENT_ID__", ENV)
        .replace("__FLOW_ID__", sql_flow_id)
        .replace("__SPO_FLOW_ID__", spo_flow_id)
        .replace("__FULL_FLOW_ID__", full_flow_id)
    )
    resources = rows("webresourceset", f"name eq '{RESOURCE_NAME}'", "webresourceid")
    if len(resources) != 1:
        raise RuntimeError("Expected one BmsEventDemo web resource")
    resource_id = resources[0]["webresourceid"]
    api(
        "PATCH",
        f"webresourceset({resource_id})",
        json={"content": base64.b64encode(html.encode("utf-8")).decode("ascii")},
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
    api(
        "POST",
        "PublishXml",
        json={
            "ParameterXml": f"<importexportxml><webresources><webresource>{resource_id}</webresource></webresources></importexportxml>"
        },
    )


def verify(api, rows, flow_id):
    flow = api(
        "GET",
        f"workflows({flow_id})",
        params={"$select": "workflowid,name,statecode,clientdata"},
    )
    definition = json.loads(flow["clientdata"])["properties"]["definition"]
    trigger = definition["triggers"]["When_full_sync_requested"]
    actions = definition["actions"]
    if (
        flow["statecode"] != 1
        or trigger["inputs"]["parameters"]["subscriptionRequest/sdkmessagename"] != API_NAME
        or actions["Request_SQL_sync"]["inputs"]["parameters"]["actionName"] != SQL_API_NAME
        or actions["Request_SharePoint_sync"]["inputs"]["parameters"]["actionName"] != SPO_API_NAME
    ):
        raise RuntimeError("Full-sync flow trigger or dispatch actions differ")
    callbacks = rows(
        "callbackregistrations",
        f"sdkmessagename eq '{API_NAME}'",
        "callbackregistrationid,sdkmessagename,entityname,softdeletestatus",
    )
    if (
        len(callbacks) != 1
        or callbacks[0]["entityname"] != "none"
        or callbacks[0]["softdeletestatus"] != 0
    ):
        raise RuntimeError("Active full-sync Power Automate callback is missing")
    sql_flow_id = find_flow_id(rows, SQL_FLOW_NAME)
    spo_flow_id = find_flow_id(rows, SPO_FLOW_NAME)
    expected_page = (
        (ROOT / "dataverse/app-source/BmsEventDemo.html")
        .read_text(encoding="utf-8")
        .replace("__ENVIRONMENT_ID__", ENV)
        .replace("__FLOW_ID__", sql_flow_id)
        .replace("__SPO_FLOW_ID__", spo_flow_id)
        .replace("__FULL_FLOW_ID__", flow_id)
    )
    resources = rows(
        "webresourceset", f"name eq '{RESOURCE_NAME}'", "webresourceid,content"
    )
    if len(resources) != 1 or base64.b64decode(resources[0]["content"]).decode("utf-8") != expected_page:
        raise RuntimeError("Live BmsEventDemo page differs from the authored Full Sync page")
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
