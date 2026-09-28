"""Deploy a solution-aware email flow for supported FMC-Inbox Excel file changes.

The SharePoint trigger reports file versions, not changed workbook rows/cells.
Existing FMC - Detect SPO File Change continues to record the sync request.
"""

import argparse
import json
import subprocess
import sys
import uuid
from pathlib import Path

import requests


ROOT = Path(__file__).resolve().parents[1]
ARTIFACT = ROOT / ".artifacts/spo-excel-change-email/clientdata.json"
ORG = "https://org06cbc9ec.crm5.dynamics.com"
ORG_ID = "ab191700-b99e-f111-aaa0-000d3a80bb96"
SOLUTION = "FMCentralBms"
FLOW_NAME = "FMC - Email SPO Excel File Change"
RECIPIENT_SCHEMA = "fmc_SpoExcelChangeEmailRecipient"
RECIPIENT = "vuong.nguyenq@titancorpvn.com"
SITE = "https://titancorpvncom.sharepoint.com/sites/Powerplatform"
LIBRARY = "7d9282a4-c45d-4ef8-8719-18f78e71d0d0"
INBOX = "/Shared Documents/FMC-Inbox"
SHAREPOINT = "shared_sharepointonline"
OUTLOOK = "shared_office365"
CONNECTIONS = {
    SHAREPOINT: ("fmc_sharedsharepointonline", "/providers/Microsoft.PowerApps/apis/shared_sharepointonline"),
    OUTLOOK: ("fmc_sharedoffice365outlook", "/providers/Microsoft.PowerApps/apis/shared_office365"),
}


def definition(connection_ids):
    path = "concat('/', triggerBody()?['{FullPath}'])"
    allowed = [
        "/01-Master/Building/",
        "/01-Master/Equipment/",
        "/01-Master/ElectricMeter/",
        "/01-Master/WaterMeter/",
        "/02-Telemetry/Electricity/",
        "/02-Telemetry/Water/",
    ]
    prefixes = ",".join(
        f"startsWith(toLower({path}),toLower(concat(parameters('SpoInboxPath'),'{suffix}')))"
        for suffix in allowed
    )
    supported = (
        "@and(endsWith(toLower(" + path + "),'.xlsx'),"
        "not(startsWith(triggerBody()?['{FilenameWithExtension}'],'~$')),")
    supported = supported + "or(" + prefixes + "))"
    name = "coalesce(triggerBody()?['{FilenameWithExtension}'],'Excel file')"
    email_body = (
        "@concat('<p>A SharePoint Excel file was created or modified.</p>',"
        "'<p><b>File:</b> '," + name + ",'<br/><b>Path:</b> ',"
        + path
        + ",'<br/><b>Modified by:</b> ',"
        "coalesce(triggerBody()?['Editor']?['DisplayName'],triggerBody()?['Editor']?['Email'],'Unknown'),"
        "'<br/><b>Modified at (UTC):</b> ',coalesce(triggerBody()?['Modified'],'Unknown'),"
        "'<br/><b>Detected at (UTC):</b> ',utcNow(),"
        "'<br/><b>ETag:</b> ',coalesce(body('Get_file_metadata')?['ETag'],'Unknown'),"
        "'</p><p><a href=\"',coalesce(triggerBody()?['{Link}'],''),'\">Open Excel file</a></p>',"
        "'<p>This notification tracks a file version, not individual changed cells or rows.</p>')"
    )
    parameters = {
        "$authentication": {"defaultValue": {}, "type": "SecureObject"},
        "$connections": {"defaultValue": {}, "type": "Object"},
    }
    for name_, schema, value in [
        ("SpoSiteUrl", "fmc_SpoSiteUrl", SITE),
        ("SpoLibraryId", "fmc_SpoLibraryId", LIBRARY),
        ("SpoInboxPath", "fmc_SpoInboxPath", INBOX),
        ("EmailRecipient", RECIPIENT_SCHEMA, RECIPIENT),
    ]:
        parameters[name_] = {
            "type": "String",
            "defaultValue": value,
            "metadata": {"schemaName": schema},
        }
    flow_definition = {
        "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
        "contentVersion": "1.0.0.0",
        "parameters": parameters,
        "triggers": {
            "When_Excel_file_created_or_modified": {
                "type": "OpenApiConnection",
                "recurrence": {"frequency": "Minute", "interval": 1},
                "splitOn": "@triggerBody()?['value']",
                "inputs": {
                    "host": {
                        "apiId": CONNECTIONS[SHAREPOINT][1],
                        "connectionName": SHAREPOINT,
                        "operationId": "GetOnUpdatedFileItems",
                    },
                    "parameters": {
                        "dataset": "@parameters('SpoSiteUrl')",
                        "table": "@parameters('SpoLibraryId')",
                    },
                    "authentication": "@parameters('$authentication')",
                },
            }
        },
        "actions": {
            "Supported_FMC_Excel_source": {
                "type": "If",
                "runAfter": {},
                "expression": supported,
                "actions": {
                    "Get_file_metadata": {
                        "type": "OpenApiConnection",
                        "runAfter": {},
                        "inputs": {
                            "host": {
                                "apiId": CONNECTIONS[SHAREPOINT][1],
                                "connectionName": SHAREPOINT,
                                "operationId": "GetFileMetadata",
                            },
                            "parameters": {
                                "dataset": "@parameters('SpoSiteUrl')",
                                "id": "@triggerBody()?['{Identifier}']",
                            },
                            "authentication": "@parameters('$authentication')",
                        },
                    },
                    "Send_email": {
                        "type": "OpenApiConnection",
                        "runAfter": {"Get_file_metadata": ["Succeeded"]},
                        "inputs": {
                            "host": {
                                "apiId": CONNECTIONS[OUTLOOK][1],
                                "connectionName": OUTLOOK,
                                "operationId": "SendEmailV2",
                            },
                            "parameters": {
                                "emailMessage/To": "@parameters('EmailRecipient')",
                                "emailMessage/Subject": "@concat('[FMC] SharePoint Excel changed: '," + name + ")",
                                "emailMessage/Body": email_body,
                                "emailMessage/Importance": "Normal",
                            },
                            "authentication": "@parameters('$authentication')",
                        },
                    },
                },
                "else": {
                    "actions": {
                        "Skip_other_files": {
                            "type": "Compose",
                            "runAfter": {},
                            "inputs": "Outside supported FMC-Inbox .xlsx sources",
                        }
                    }
                },
            }
        },
    }
    references = {
        connector: {
            "runtimeSource": "embedded",
            "connection": {
                "name": connection_ids[connector],
                "connectionReferenceLogicalName": CONNECTIONS[connector][0],
            },
            "api": {"name": connector},
        }
        for connector in CONNECTIONS
    }
    return {
        "properties": {"definition": flow_definition, "connectionReferences": references},
        "schemaVersion": "1.0.0.0",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["render", "deploy", "verify", "enable", "disable"])
    args = parser.parse_args()
    token = subprocess.check_output(
        [sys.executable, str(ROOT / "scripts/get-dataverse-token.py")], text=True
    ).strip()
    session = requests.Session()
    session.headers.update({
        "Authorization": "Bearer " + token,
        "Accept": "application/json",
        "OData-Version": "4.0",
        "OData-MaxVersion": "4.0",
    })

    def api(method, path, **kwargs):
        response = session.request(method, ORG + "/api/data/v9.2/" + path, timeout=120, **kwargs)
        if not response.ok:
            raise RuntimeError(f"{method} {path}: {response.status_code} {response.text[:2000]}")
        return response.json() if response.content else {}

    def rows(table, filter_text, select):
        return api("GET", table, params={"$filter": filter_text, "$select": select})["value"]

    if api("GET", "WhoAmI")["OrganizationId"].lower() != ORG_ID:
        raise RuntimeError("Unexpected Dataverse organization")
    solutions = rows("solutions", f"uniquename eq '{SOLUTION}'", "solutionid")
    if len(solutions) != 1:
        raise RuntimeError("Expected exactly one FMCentralBms solution")

    for schema, expected in [
        ("fmc_SpoSiteUrl", SITE),
        ("fmc_SpoLibraryId", LIBRARY),
        ("fmc_SpoInboxPath", INBOX),
    ]:
        found = rows("environmentvariabledefinitions", f"schemaname eq '{schema}'", "environmentvariabledefinitionid,defaultvalue")
        if len(found) != 1:
            raise RuntimeError("Missing or ambiguous environment variable: " + schema)
        values = rows(
            "environmentvariablevalues",
            f"_environmentvariabledefinitionid_value eq {found[0]['environmentvariabledefinitionid']}",
            "value",
        )
        if len(values) > 1 or (values[0]["value"] if values else found[0].get("defaultvalue")) != expected:
            raise RuntimeError("Unexpected live SharePoint scope: " + schema)

    connection_ids = {}
    for connector, (logical, api_id) in CONNECTIONS.items():
        found = rows(
            "connectionreferences", f"connectionreferencelogicalname eq '{logical}'",
            "connectionreferenceid,connectionid,connectorid",
        )
        if len(found) != 1 or not found[0].get("connectionid") or found[0]["connectorid"] != api_id:
            raise RuntimeError("Missing or unexpected connection reference: " + logical)
        connection_ids[connector] = found[0]["connectionid"]

    clientdata = definition(connection_ids)
    flows = rows("workflows", f"category eq 5 and type eq 1 and name eq '{FLOW_NAME}'", "workflowid,name,statecode,clientdata")
    if len(flows) > 1:
        raise RuntimeError("Duplicate Excel email flow names")
    recipient_definitions = rows(
        "environmentvariabledefinitions",
        f"schemaname eq '{RECIPIENT_SCHEMA}'",
        "environmentvariabledefinitionid,defaultvalue,type",
    )
    if len(recipient_definitions) > 1:
        raise RuntimeError("Duplicate recipient environment variables")
    recipient_values = (
        rows(
            "environmentvariablevalues",
            f"_environmentvariabledefinitionid_value eq {recipient_definitions[0]['environmentvariabledefinitionid']}",
            "value",
        )
        if recipient_definitions else []
    )
    if len(recipient_values) > 1:
        raise RuntimeError("Duplicate recipient environment variable values")
    effective_recipient = (
        recipient_values[0]["value"] if recipient_values else
        recipient_definitions[0].get("defaultvalue") if recipient_definitions else None
    )
    if args.mode == "render":
        ARTIFACT.parent.mkdir(parents=True, exist_ok=True)
        ARTIFACT.write_text(json.dumps(clientdata, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"rendered": str(ARTIFACT), "scope": INBOX, "recipient": RECIPIENT}))
        return
    if args.mode == "deploy":
        if not ARTIFACT.exists() or json.loads(ARTIFACT.read_text(encoding="utf-8")) != clientdata:
            raise RuntimeError("Render the current definition before deployment")
        existing = recipient_definitions
        if len(existing) > 1 or (existing and (existing[0].get("defaultvalue") != RECIPIENT or existing[0]["type"] != 100000000)):
            raise RuntimeError("Recipient environment variable has a conflicting definition")
        if recipient_values and recipient_values[0]["value"] != RECIPIENT:
            raise RuntimeError("Recipient environment variable has a conflicting current value")
        if not existing:
            api("POST", "environmentvariabledefinitions", json={
                "environmentvariabledefinitionid": str(uuid.uuid4()),
                "schemaname": RECIPIENT_SCHEMA,
                "displayname": "SPO Excel Change Email Recipient",
                "description": "Recipient for notifications about supported FMC-Inbox Excel file versions.",
                "type": 100000000,
                "defaultvalue": RECIPIENT,
            }, headers={"MSCRM.SolutionUniqueName": SOLUTION})
        flow_id = flows[0]["workflowid"] if flows else str(uuid.uuid4())
        payload = {
            "name": FLOW_NAME,
            "category": 5,
            "type": 1,
            "primaryentity": "none",
            "description": "Emails the configured recipient when a supported FMC-Inbox Excel file version changes.",
            "clientdata": json.dumps(clientdata, separators=(",", ":")),
        }
        if flows:
            if flows[0]["statecode"] == 1:
                api("PATCH", f"workflows({flow_id})", json={"statecode": 0})
            api("PATCH", f"workflows({flow_id})", json=payload)
        else:
            api("POST", "workflows", json={"workflowid": flow_id, **payload}, headers={"MSCRM.SolutionUniqueName": SOLUTION})
        api("POST", "AddSolutionComponent", json={
            "ComponentId": flow_id,
            "ComponentType": 29,
            "SolutionUniqueName": SOLUTION,
            "AddRequiredComponents": True,
        })
        api("PATCH", f"workflows({flow_id})", json={"statecode": 1})
    elif args.mode in ("enable", "disable"):
        if len(flows) != 1:
            raise RuntimeError("Deploy the flow first")
        api("PATCH", f"workflows({flows[0]['workflowid']})", json={"statecode": 1 if args.mode == "enable" else 0})

    found = rows("workflows", f"category eq 5 and type eq 1 and name eq '{FLOW_NAME}'", "workflowid,statecode,clientdata")
    if len(found) != 1:
        raise RuntimeError("Excel email flow was not found after operation")
    membership = rows(
        "solutioncomponents",
        f"_solutionid_value eq {solutions[0]['solutionid']} and objectid eq {found[0]['workflowid']}",
        "solutioncomponentid,componenttype",
    )
    print(json.dumps({
        "organization": ORG_ID,
        "flow": FLOW_NAME,
        "flowId": found[0]["workflowid"],
        "enabled": found[0]["statecode"] == 1,
        "definitionMatches": json.loads(found[0]["clientdata"]) == clientdata,
        "solutionMember": any(row["componenttype"] == 29 for row in membership),
        "recipient": effective_recipient or RECIPIENT,
    }))


if __name__ == "__main__":
    main()
