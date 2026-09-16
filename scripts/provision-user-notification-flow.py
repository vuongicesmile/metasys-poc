"""Provision and verify the solution-aware FMC user email notification flow.

The flow consumes Pending fmc_notification outbox rows, sends them through the
Office 365 Outlook connector, and writes a delivery receipt back to Dataverse.
"""

import argparse
import json
import subprocess
import time
import uuid
from pathlib import Path

import requests


ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / ".artifacts/user-notifications"
ORG = "https://org06cbc9ec.crm5.dynamics.com"
ORG_ID = "ab191700-b99e-f111-aaa0-000d3a80bb96"
SOLUTION = "FMCentralBms"
FLOW_NAME = "FMC - Send User Email Notification"
DV = "shared_commondataserviceforapps"
DV_CONNECTOR = "/providers/Microsoft.PowerApps/apis/" + DV
DV_CONNECTION_LOGICAL = "fmc_sharedcommondataserviceforapps"
OUTLOOK = "shared_office365"
OUTLOOK_CONNECTOR = "/providers/Microsoft.PowerApps/apis/" + OUTLOOK
OUTLOOK_CONNECTION_LOGICAL = "fmc_sharedoffice365outlook"
OUTLOOK_CONNECTION_ID = "fae65120-ff53-4323-9e06-e0415cb4125a"
EMAIL_CHANNEL = 789120000
PENDING = 789121000
SENT = 789121001
FAILED = 789121002


def connection_reference(logical_name, connection_id, api_name):
    return {
        "runtimeSource": "embedded",
        "connection": {
            "name": connection_id,
            "connectionReferenceLogicalName": logical_name,
        },
        "api": {"name": api_name},
    }


def dataverse_update(entity_set, values, run_after):
    parameters = {
        "entityName": entity_set,
        "recordId": "@triggerOutputs()?['body/fmc_notificationid']",
    }
    parameters.update({"item/" + key: value for key, value in values.items()})
    return {
        "type": "OpenApiConnection",
        "runAfter": run_after,
        "inputs": {
            "host": {
                "apiId": DV_CONNECTOR,
                "connectionName": DV,
                "operationId": "UpdateOnlyRecord",
            },
            "parameters": parameters,
            "authentication": "@parameters('$authentication')",
        },
    }


def build(entity_set, dv_connection_id, outlook_connection_id):
    definition = {
        "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
        "contentVersion": "1.0.0.0",
        "parameters": {
            "$authentication": {"defaultValue": {}, "type": "SecureObject"},
            "$connections": {"defaultValue": {}, "type": "Object"},
        },
        "triggers": {
            "When_a_pending_email_notification_is_created": {
                "type": "OpenApiConnectionWebhook",
                "inputs": {
                    "host": {
                        "connectionName": DV,
                        "operationId": "SubscribeWebhookTrigger",
                        "apiId": DV_CONNECTOR,
                    },
                    "parameters": {
                        "subscriptionRequest/message": 1,
                        "subscriptionRequest/entityname": "fmc_notification",
                        "subscriptionRequest/scope": 4,
                        "subscriptionRequest/filterexpression": (
                            f"fmc_status eq {PENDING} and "
                            f"fmc_channel eq {EMAIL_CHANNEL} and "
                            "fmc_recipientemail ne null"
                        ),
                    },
                    "authentication": "@parameters('$authentication')",
                },
                "runtimeConfiguration": {"concurrency": {"runs": 10}},
            }
        },
        "actions": {
            "Mark_attempt": dataverse_update(
                entity_set,
                {
                    "fmc_attemptedat": "@utcNow()",
                    "fmc_attempts": "@add(coalesce(triggerOutputs()?['body/fmc_attempts'], 0), 1)",
                    "fmc_flowrunid": "@workflow()?['run']?['name']",
                    "fmc_errormessage": None,
                },
                {},
            ),
            "Send_email": {
                "type": "OpenApiConnection",
                "runAfter": {"Mark_attempt": ["Succeeded"]},
                "inputs": {
                    "host": {
                        "apiId": OUTLOOK_CONNECTOR,
                        "connectionName": OUTLOOK,
                        "operationId": "SendEmailV2",
                    },
                    "parameters": {
                        "emailMessage/To": "@triggerOutputs()?['body/fmc_recipientemail']",
                        "emailMessage/Subject": "@triggerOutputs()?['body/fmc_subject']",
                        "emailMessage/Body": "@triggerOutputs()?['body/fmc_body']",
                        "emailMessage/Importance": "Normal",
                    },
                    "authentication": "@parameters('$authentication')",
                },
                "runtimeConfiguration": {"retryPolicy": {"type": "none"}},
            },
            "Mark_sent": dataverse_update(
                entity_set,
                {
                    "fmc_status": SENT,
                    "fmc_sentat": "@utcNow()",
                    "fmc_errormessage": None,
                },
                {"Send_email": ["Succeeded"]},
            ),
            "Mark_failed": dataverse_update(
                entity_set,
                {
                    "fmc_status": FAILED,
                    "fmc_errormessage": (
                        "EMAIL-001 Office 365 Outlook delivery failed. "
                        "Inspect the Power Automate run by fmc_flowrunid."
                    ),
                },
                {"Send_email": ["Failed", "TimedOut"]},
            ),
        },
    }
    refs = {
        DV: connection_reference(DV_CONNECTION_LOGICAL, dv_connection_id, DV),
        OUTLOOK: connection_reference(
            OUTLOOK_CONNECTION_LOGICAL, outlook_connection_id, OUTLOOK
        ),
    }
    return {
        "properties": {"definition": definition, "connectionReferences": refs},
        "schemaVersion": "1.0.0.0",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "mode",
        choices=[
            "bootstrap-reference",
            "render",
            "deploy",
            "enable",
            "disable",
            "verify",
            "test",
            "test-producer",
        ],
    )
    parser.add_argument(
        "--outlook-connection-id",
        default=OUTLOOK_CONNECTION_ID,
        help=(
            "Office 365 Outlook connection ID used only by bootstrap-reference. "
            "Defaults to the checked Developer-environment connection."
        ),
    )
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

    if api("GET", "WhoAmI")["OrganizationId"].lower() != ORG_ID:
        raise RuntimeError("Unexpected Dataverse organization")
    if len(rows("solutions", "uniquename eq 'FMCentralBms'", "solutionid")) != 1:
        raise RuntimeError("Expected one FMCentralBms solution")

    metadata = api(
        "GET",
        "EntityDefinitions(LogicalName='fmc_notification')",
        params={"$select": "EntitySetName"},
    )
    entity_set = metadata["EntitySetName"]

    if args.mode == "bootstrap-reference":
        found = rows(
            "connectionreferences",
            f"connectionreferencelogicalname eq '{OUTLOOK_CONNECTION_LOGICAL}'",
            "connectionreferenceid,connectionid,connectorid",
        )
        if len(found) > 1:
            raise RuntimeError("Duplicate Office 365 Outlook connection references")
        values = {
            "connectionreferencelogicalname": OUTLOOK_CONNECTION_LOGICAL,
            "connectionreferencedisplayname": "FM Central Office 365 Outlook",
            "description": "Office 365 Outlook connection used by FMC user email notifications.",
            "connectorid": OUTLOOK_CONNECTOR,
            "connectionid": args.outlook_connection_id,
        }
        if found:
            reference_id = found[0]["connectionreferenceid"]
            if found[0].get("connectorid") != OUTLOOK_CONNECTOR:
                raise RuntimeError("Existing Outlook connection reference uses another connector")
            api("PATCH", f"connectionreferences({reference_id})", json=values)
        else:
            reference_id = str(uuid.uuid4())
            api(
                "POST",
                "connectionreferences",
                json={"connectionreferenceid": reference_id, **values},
                headers={"MSCRM.SolutionUniqueName": SOLUTION},
            )
        print(
            json.dumps(
                {
                    "connectionReferenceId": reference_id,
                    "logicalName": OUTLOOK_CONNECTION_LOGICAL,
                    "connectionId": args.outlook_connection_id,
                }
            )
        )
        return

    def resolve_connection(logical_name, connector_id):
        found = rows(
            "connectionreferences",
            f"connectionreferencelogicalname eq '{logical_name}'",
            "connectionreferenceid,connectionid,connectorid",
        )
        if len(found) != 1 or not found[0].get("connectionid"):
            raise RuntimeError(
                f"Connection reference {logical_name} is missing or has no connection"
            )
        if found[0].get("connectorid") != connector_id:
            raise RuntimeError(f"Connection reference {logical_name} uses another connector")
        return found[0]["connectionid"]

    dv_connection_id = resolve_connection(DV_CONNECTION_LOGICAL, DV_CONNECTOR)
    outlook_connection_id = resolve_connection(
        OUTLOOK_CONNECTION_LOGICAL, OUTLOOK_CONNECTOR
    )
    clientdata = build(entity_set, dv_connection_id, outlook_connection_id)
    rendered = ARTIFACTS / "clientdata.json"
    if args.mode == "render":
        rendered.write_text(json.dumps(clientdata, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"rendered": str(rendered), "entitySet": entity_set}))
        return

    flows = rows(
        "workflows",
        f"category eq 5 and type eq 1 and name eq '{FLOW_NAME}'",
        "workflowid,name,statecode,clientdata",
    )
    if len(flows) > 1:
        raise RuntimeError("Duplicate notification flow names")
    flow_id = flows[0]["workflowid"] if flows else str(uuid.uuid4())

    if args.mode == "deploy":
        if not rendered.exists() or json.loads(rendered.read_text(encoding="utf-8")) != clientdata:
            raise RuntimeError("Render the current definition before deployment")
        payload = {
            "name": FLOW_NAME,
            "category": 5,
            "type": 1,
            "primaryentity": "none",
            "description": (
                "Sends Pending fmc_notification email outbox rows and records "
                "Sent or Failed delivery receipts."
            ),
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
    elif args.mode in ("enable", "disable"):
        if len(flows) != 1:
            raise RuntimeError("Deploy the notification flow first")
        api(
            "PATCH",
            f"workflows({flow_id})",
            json={"statecode": 1 if args.mode == "enable" else 0},
        )
        if args.mode == "disable":
            print(json.dumps({"flowId": flow_id, "state": 0}))
            return
    elif args.mode == "test":
        if len(flows) != 1 or flows[0]["statecode"] != 1:
            raise RuntimeError("Enable the notification flow before testing")
        who = api("GET", "WhoAmI")
        user = api(
            "GET",
            f"systemusers({who['UserId']})",
            params={"$select": "internalemailaddress,fullname"},
        )
        recipient = user.get("internalemailaddress")
        if not recipient:
            raise RuntimeError("Current Dataverse user has no internal email address")
        notification_id = str(uuid.uuid4())
        api(
            "POST",
            entity_set,
            json={
                "fmc_notificationid": notification_id,
                "fmc_name": "Notification smoke test",
                "fmc_channel": EMAIL_CHANNEL,
                "fmc_eventtype": 789122000,
                "fmc_status": PENDING,
                "fmc_recipientemail": recipient,
                "fmc_recipientname": user.get("fullname"),
                "fmc_subject": "[FMC BMS] Notification smoke test",
                "fmc_body": "<p>The FMCentralBms email notification flow is working.</p>",
                "fmc_correlationkey": "notification-smoke:" + notification_id,
                "fmc_regardingtable": "smoke-test",
                "fmc_regardingid": notification_id,
                "fmc_attempts": 0,
            },
        )
        deadline = time.time() + 55
        result = None
        while time.time() < deadline:
            result = api(
                "GET",
                f"{entity_set}({notification_id})",
                params={
                    "$select": "fmc_status,fmc_attempts,fmc_sentat,fmc_flowrunid,fmc_errormessage"
                },
            )
            if result.get("fmc_status") in (SENT, FAILED):
                break
            time.sleep(3)
        print(
            json.dumps(
                {
                    "notificationId": notification_id,
                    "recipient": recipient,
                    "receipt": result,
                }
            )
        )
        if not result or result.get("fmc_status") != SENT:
            raise RuntimeError("Notification did not reach Sent within 55 seconds")
    elif args.mode == "test-producer":
        if len(flows) != 1 or flows[0]["statecode"] != 1:
            raise RuntimeError("Enable the notification flow before testing")
        who = api("GET", "WhoAmI")
        user = api(
            "GET",
            f"systemusers({who['UserId']})",
            params={"$select": "internalemailaddress,fullname"},
        )
        recipient = user.get("internalemailaddress")
        if not recipient:
            raise RuntimeError("Current Dataverse user has no internal email address")
        request_id = str(uuid.uuid4())
        api(
            "POST",
            "fmc_syncrequests",
            json={
                "fmc_syncrequestid": request_id,
                "fmc_name": "Notification producer smoke test",
                "fmc_pipeline": "notification-producer-smoke:" + request_id,
                "fmc_correlationid": request_id,
                "fmc_requestedby": recipient,
                "fmc_status": 789100001,
                "fmc_deliveredrows": 3,
                "fmc_quarantinedrows": 0,
                "fmc_pendingafter": 0,
            },
        )
        api(
            "PATCH",
            f"fmc_syncrequests({request_id})",
            json={
                "fmc_status": 789100002,
                "fmc_completedat": time.strftime(
                    "%Y-%m-%dT%H:%M:%SZ", time.gmtime()
                ),
            },
        )
        correlation_key = (
            "syncrequest:"
            + request_id.replace("-", "")
            + ":status:789100002"
        )
        deadline = time.time() + 55
        notification = None
        while time.time() < deadline:
            found = rows(
                entity_set,
                f"fmc_correlationkey eq '{correlation_key}'",
                "fmc_notificationid,fmc_status,fmc_recipientemail,fmc_attempts,fmc_sentat,fmc_flowrunid,fmc_errormessage",
            )
            if found:
                notification = found[0]
                if notification.get("fmc_status") in (SENT, FAILED, 789121003):
                    break
            time.sleep(3)
        print(
            json.dumps(
                {
                    "syncRequestId": request_id,
                    "correlationKey": correlation_key,
                    "notification": notification,
                }
            )
        )
        if not notification or notification.get("fmc_status") != SENT:
            raise RuntimeError("Producer notification did not reach Sent within 55 seconds")

    verify(api, rows, flow_id)


def verify(api, rows, flow_id):
    flow = api(
        "GET",
        f"workflows({flow_id})",
        params={"$select": "workflowid,name,statecode,clientdata"},
    )
    clientdata = json.loads(flow["clientdata"])
    definition = clientdata["properties"]["definition"]
    trigger = definition["triggers"]["When_a_pending_email_notification_is_created"]
    send = definition["actions"]["Send_email"]
    if (
        flow["statecode"] != 1
        or trigger["inputs"]["parameters"]["subscriptionRequest/entityname"]
        != "fmc_notification"
        or send["inputs"]["host"]["operationId"] != "SendEmailV2"
    ):
        raise RuntimeError("Notification flow trigger or email action differs")
    callbacks = rows(
        "callbackregistrations",
        "entityname eq 'fmc_notification'",
        "callbackregistrationid,entityname,sdkmessagename,softdeletestatus",
    )
    active = [row for row in callbacks if row.get("softdeletestatus") == 0]
    if len(active) != 1:
        raise RuntimeError(
            "Expected one active fmc_notification create callback; observed "
            + json.dumps(callbacks)
        )
    assemblies = rows(
        "pluginassemblies",
        "name eq 'FMCentralBms.Plugins'",
        "pluginassemblyid,name,version",
    )
    if len(assemblies) != 1 or assemblies[0].get("version") != "1.0.0.5":
        raise RuntimeError("Expected FMCentralBms.Plugins assembly version 1.0.0.5")
    steps = rows(
        "sdkmessageprocessingsteps",
        "name eq 'BMS: Queue notification on Sync Request completion'",
        "sdkmessageprocessingstepid,name,mode,stage,filteringattributes,statecode",
    )
    if (
        len(steps) != 1
        or steps[0].get("mode") != 1
        or steps[0].get("stage") != 40
        or steps[0].get("filteringattributes") != "fmc_status"
        or steps[0].get("statecode") != 0
    ):
        raise RuntimeError("Notification producer plug-in step differs")
    print(
        json.dumps(
            {
                "organization": ORG_ID,
                "flow": FLOW_NAME,
                "flowId": flow_id,
                "state": flow["statecode"],
                "callbackId": active[0]["callbackregistrationid"],
                "pluginAssemblyVersion": assemblies[0]["version"],
                "pluginStepId": steps[0]["sdkmessageprocessingstepid"],
            }
        )
    )


if __name__ == "__main__":
    main()
