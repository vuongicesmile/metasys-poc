"""Deploy only Epic 3 UI/deadline flow; schema and signed plug-in use .NET provisioners.

All modes verify the Developer organization. No secrets are persisted.
Read-only: inspect, verify. Mutations: deploy-ui, deploy-flow, seed-demo, smoke.
"""
import argparse
import base64
import json
import hashlib
import subprocess
import uuid
import xml.etree.ElementTree as ET
from pathlib import Path
import requests

ROOT = Path(__file__).resolve().parents[1]
ORG = "https://org06cbc9ec.crm5.dynamics.com"
ORG_ID = "ab191700-b99e-f111-aaa0-000d3a80bb96"
APP = "d19f4897-d227-4df3-8361-988f97c53e89"
SOLUTION = "FMCentralBms"
FLOW = "FMC - Process FM Request Deadlines"
DV = "shared_commondataserviceforapps"
REFERENCE = "fmc_sharedcommondataserviceforapps"
PAGE = "fmc_/pages/FmWorkflow.html"
SCRIPT = "fmc_/scripts/FmWorkflow.js"
ARTIFACTS = ROOT / ".artifacts/epic3-workflow"


class Client:
    def __init__(self):
        token = subprocess.check_output(["python", str(ROOT / "scripts/get-dataverse-token.py")], text=True).strip()
        self.session = requests.Session()
        self.session.headers.update({"Authorization": "Bearer " + token, "Accept": "application/json", "OData-Version": "4.0"})
        self.who = self.api("GET", "WhoAmI")
        if self.who["OrganizationId"].lower() != ORG_ID:
            raise RuntimeError("Unexpected organization; no changes permitted")
        assert len(self.rows("solutions", "uniquename eq 'FMCentralBms'", "solutionid")) == 1
        self.sets = {}
        for table in ("fmc_fmrequest", "fmc_approvalroute", "fmc_requesthistory", "fmc_notification"):
            self.sets[table] = self.api("GET", f"EntityDefinitions(LogicalName='{table}')", params={"$select": "EntitySetName"})["EntitySetName"]

    def api(self, method, path, **kwargs):
        response = self.session.request(method, ORG + "/api/data/v9.2/" + path, timeout=120, **kwargs)
        if not response.ok:
            raise RuntimeError(f"{method} {path}: {response.status_code} {response.text[:3500]}")
        return response.json() if response.content else {}

    def rows(self, table, filter_text, select="*", **extra):
        return self.api("GET", self.sets.get(table, table) if hasattr(self, "sets") else table,
                        params={"$filter": filter_text, "$select": select, **extra})["value"]

    def add(self, id, kind):
        self.api("POST", "AddSolutionComponent", json={"ComponentId": id, "ComponentType": kind,
            "SolutionUniqueName": SOLUTION, "AddRequiredComponents": False})

    def backup(self, name, value):
        ARTIFACTS.mkdir(parents=True, exist_ok=True)
        path = ARTIFACTS / (name + ".json")
        if not path.exists():
            path.write_text(json.dumps(value, indent=2), encoding="utf-8")


def build_flow(entity_set, connection):
    def action(operation, parameters, after=None):
        return {"type": "OpenApiConnection", "runAfter": after or {}, "inputs": {
            "host": {"apiId": "/providers/Microsoft.PowerApps/apis/" + DV, "connectionName": DV, "operationId": operation},
            "parameters": parameters, "authentication": "@parameters('$authentication')"}}
    listing = action("ListRecords", {"entityName": entity_set, "$select": "fmc_fmrequestid", "$filter":
        "fmc_requeststatus eq 789141002 and fmc_workflowplan ne null and fmc_escalatedon eq null",
        "$orderby": "fmc_stepdueon asc"})
    listing["runtimeConfiguration"] = {"paginationPolicy": {"minimumItemCount": 100000}}
    process = action("PerformUnboundAction", {"actionName": "fmc_ProcessFmRequestDeadline",
        "item/RequestId": "@items('For_each_request')?['fmc_fmrequestid']"})
    return {"schemaVersion": "1.0.0.0", "properties": {"connectionReferences": {DV: {
        "runtimeSource": "embedded", "connection": {"name": connection, "connectionReferenceLogicalName": REFERENCE},
        "api": {"name": DV}}}, "definition": {
        "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
        "contentVersion": "1.0.0.0", "parameters": {"$authentication": {"defaultValue": {}, "type": "SecureObject"}, "$connections": {"defaultValue": {}, "type": "Object"}},
        "triggers": {"Every_five_minutes": {"type": "Recurrence", "recurrence": {"frequency": "Minute", "interval": 5}, "runtimeConfiguration": {"concurrency": {"runs": 1}}}},
        "actions": {"List_pending_requests": listing, "For_each_request": {"type": "Foreach", "foreach": "@outputs('List_pending_requests')?['body/value']",
            "runAfter": {"List_pending_requests": ["Succeeded"]}, "runtimeConfiguration": {"concurrency": {"repetitions": 5}}, "actions": {"Process_deadline": process}}}}}}


def deploy_flow(c):
    refs = c.rows("connectionreferences", f"connectionreferencelogicalname eq '{REFERENCE}'", "connectionid,connectorid")
    if len(refs) != 1 or refs[0]["connectorid"] != "/providers/Microsoft.PowerApps/apis/" + DV:
        raise RuntimeError("Expected existing Dataverse connection reference")
    email = c.rows("workflows", "name eq 'FMC - Send User Email Notification' and category eq 5 and type eq 1", "statecode")
    if len(email) != 1 or email[0]["statecode"] != 1:
        raise RuntimeError("Existing email dispatcher must be enabled")
    body = build_flow(c.sets["fmc_fmrequest"], refs[0]["connectionid"])
    flows = c.rows("workflows", f"name eq '{FLOW}' and category eq 5 and type eq 1", "workflowid,statecode,clientdata")
    if len(flows) > 1:
        raise RuntimeError("Duplicate deadline flows")
    id = flows[0]["workflowid"] if flows else str(uuid.uuid4())
    payload = {"name": FLOW, "category": 5, "type": 1, "primaryentity": "none", "description": "Epic 3 reminders/escalation. Server validates due time and deduplicates each step. Uses existing Dataverse connection.", "clientdata": json.dumps(body)}
    if flows:
        c.backup("flow-before", flows[0])
        if flows[0]["statecode"] == 1:
            c.api("PATCH", f"workflows({id})", json={"statecode": 0})
        c.api("PATCH", f"workflows({id})", json=payload)
    else:
        c.api("POST", "workflows", json={"workflowid": id, **payload}, headers={"MSCRM.SolutionUniqueName": SOLUTION})
    c.add(id, 29)
    c.api("PATCH", f"workflows({id})", json={"statecode": 1})
    print(json.dumps({"flowId": id, "enabled": True}))


CONTROL = {"String": "4273EDBD-AC1D-40d3-9FB2-095C621B552D", "Memo": "E0DECE4B-6FC8-4A8F-A065-082708572369",
    "Picklist": "3EF39988-22BB-4f0b-BBBE-64B5A3748AEE", "Integer": "C6D124CA-7EDA-4a60-AEA9-7FB8D318B68F",
    "Decimal": "C3EFE0C3-0EC6-42BE-8349-CBD9079DFD8E", "DateTime": "5B773807-9FB2-42DB-97C3-7A91EFF8ADFF",
    "Boolean": "67FAC785-CD58-4f9f-ABB3-4B7DDC6ED5ED", "Lookup": "270BD3DB-D9AF-4782-9025-509E298DEC0A"}


def guid():
    return "{" + str(uuid.uuid4()) + "}"


def label(parent, title):
    ET.SubElement(ET.SubElement(parent, "labels"), "label", {"description": title, "languagecode": "1033"})


def section(form, name, title):
    tabs = form.find("tabs")
    existing = tabs.find(f"tab[@name='{name}']")
    if existing is not None:
        tabs.remove(existing)  # Only replace our own tab; retain every unrelated control.
    tab = ET.SubElement(tabs, "tab", {"name": name, "id": guid(), "IsUserDefined": "1", "expanded": "true"})
    label(tab, title)
    column = ET.SubElement(ET.SubElement(tab, "columns"), "column", {"width": "100%"})
    sec = ET.SubElement(ET.SubElement(column, "sections"), "section", {"name": name + "_section", "id": guid(), "showlabel": "false", "showbar": "false", "columns": "1"})
    label(sec, title)
    return ET.SubElement(sec, "rows")


def deploy_form(c, table, fields, resource_id=None):
    forms = c.rows("systemforms", f"objecttypecode eq '{table}' and type eq 2 and formactivationstate eq 1", "formid,name,formxml")
    if len(forms) != 1:
        raise RuntimeError(f"Expected one active main form for {table}; observed {len(forms)}")
    record = forms[0]; c.backup(table + "-form-before", record)
    form = ET.fromstring(record["formxml"])
    meta = c.api("GET", f"EntityDefinitions(LogicalName='{table}')/Attributes", params={"$select": "LogicalName,AttributeType,DisplayName"})["value"]
    attributes = {a["LogicalName"]: a for a in meta}
    rows = section(form, "fmc_workflow_details", "Workflow details")
    for field in fields:
        # Keep existing controls intact; only add fields not already present elsewhere.
        if form.find(f".//control[@datafieldname='{field}']") is not None:
            continue
        attr = attributes[field]; cell = ET.SubElement(ET.SubElement(rows, "row"), "cell", {"id": guid()})
        title = (attr.get("DisplayName", {}).get("UserLocalizedLabel") or {}).get("Label", field)
        label(cell, title)
        control = ET.SubElement(cell, "control", {"id": field, "datafieldname": field, "classid": "{" + CONTROL[attr["AttributeType"]] + "}"})
        if field in ("fmc_requeststatus", "fmc_status", "fmc_currentapproveremail", "fmc_currentstep", "fmc_stepdueon"):
            control.set("disabled", "true")
    for control in form.findall(".//control"):
        if control.get("datafieldname") in ("fmc_requeststatus", "fmc_status"):
            control.set("disabled", "true")
    if resource_id:
        rows = section(form, "fmc_workflow_actions", "Workflow actions & history")
        cell = ET.SubElement(ET.SubElement(rows, "row"), "cell", {"id": guid(), "showlabel": "false", "rowspan": "20"}); label(cell, "Workflow actions")
        control = ET.SubElement(cell, "control", {"id": "WebResource_fmc_workflow", "classid": "{9FDF5F91-88B1-47F4-AD53-C11EFC01A01D}"})
        params = ET.SubElement(control, "parameters")
        for key, value in {"Url": PAGE, "PassParameters": "true", "Security": "false", "Scrolling": "auto", "Border": "false", "WebResourceId": "{" + resource_id + "}"}.items():
            ET.SubElement(params, key).text = value
    c.api("PATCH", f"systemforms({record['formid']})", json={"formxml": ET.tostring(form, encoding="unicode")})
    c.add(record["formid"], 60)
    print(json.dumps({"form": table, "id": record["formid"]}))


def deploy_resources(c):
    ids = {}
    for name, kind in ((PAGE, 1), (SCRIPT, 3)):
        found = c.rows("webresourceset", f"name eq '{name}'", "webresourceid,content")
        if len(found) > 1:
            raise RuntimeError("Duplicate webresource")
        id = found[0]["webresourceid"] if found else str(uuid.uuid4())
        values = {"name": name, "displayname": "FM Workflow " + ("Page" if kind == 1 else "Script"), "webresourcetype": kind,
                  "content": base64.b64encode((ROOT / "dataverse/webresources" / name).read_bytes()).decode()}
        if found:
            c.backup("webresource-" + str(kind) + "-before", found[0]); c.api("PATCH", f"webresourceset({id})", json=values)
        else:
            c.api("POST", "webresourceset", json={"webresourceid": id, **values}, headers={"MSCRM.SolutionUniqueName": SOLUTION})
        c.add(id, 61); ids[name] = id
    c.api("POST", "PublishXml", json={"ParameterXml": "<importexportxml><webresources>" + "".join(f"<webresource>{id}</webresource>" for id in ids.values()) + "</webresources></importexportxml>"})
    return ids


def deploy_ui(c):
    definitions = c.rows("environmentvariabledefinitions", "schemaname eq 'fmc_FmAppUrl'", "environmentvariabledefinitionid,defaultvalue")
    if len(definitions) != 1:
        raise RuntimeError("Run schema migration before UI deployment")
    definition = definitions[0]
    if not definition.get("defaultvalue"):
        c.api("PATCH", f"environmentvariabledefinitions({definition['environmentvariabledefinitionid']})", json={"defaultvalue": ORG + "/main.aspx?appid=" + APP})
    c.add(definition["environmentvariabledefinitionid"], 380)
    ids = deploy_resources(c)
    deploy_form(c, "fmc_fmrequest", ["fmc_description", "fmc_requesttype", "fmc_department", "fmc_buildingcode", "fmc_estimatedvalue", "fmc_requeststatus", "fmc_riskseverity", "fmc_likelihood", "fmc_impact", "fmc_mitigation", "fmc_reviewdate", "fmc_currentstep", "fmc_currentapproveremail", "fmc_stepdueon"], ids[PAGE])
    deploy_form(c, "fmc_approvalroute", ["fmc_requesttype", "fmc_department", "fmc_stepno", "fmc_minvalue", "fmc_minseverity", "fmc_approveremail", "fmc_approverrole", "fmc_escalationemail", "fmc_sladays", "fmc_reminderhours", "fmc_escalationhours", "fmc_isactive"])
    maps = c.rows("sitemaps", "sitemapnameunique eq 'fmc_FMCBMSDemo'", "sitemapid,sitemapxml")
    if len(maps) != 1:
        raise RuntimeError("Expected unique FMC BMS Demo sitemap")
    m = maps[0]; c.backup("sitemap-before", m); xml = ET.fromstring(m["sitemapxml"])
    group = xml.find(".//Group[@Id='fmc_fmworkflow']")
    if group is None:
        raise RuntimeError("Existing FM Workflow navigation group not found")
    if group.find("SubArea[@Id='fmc_workflow_console']") is None:
        nav = ET.Element("SubArea", {"Id": "fmc_workflow_console", "Url": "$webresource:" + PAGE, "AvailableOffline": "false", "PassParams": "false"})
        ET.SubElement(ET.SubElement(nav, "Titles"), "Title", {"LCID": "1033", "Title": "My Requests & Approvals"})
        group.insert(1, nav)
    c.api("PATCH", f"sitemaps({m['sitemapid']})", json={"sitemapxml": ET.tostring(xml, encoding="unicode")})
    c.add(m["sitemapid"], 62)
    c.api("POST", "PublishXml", json={"ParameterXml": "<importexportxml><entities><entity>fmc_fmrequest</entity><entity>fmc_approvalroute</entity></entities><webresources>" + "".join(f"<webresource>{id}</webresource>" for id in ids.values()) + "</webresources><sitemaps><sitemap>" + m["sitemapid"] + "</sitemap></sitemaps></importexportxml>"})
    validation = c.api("GET", f"ValidateApp(AppModuleId={APP})")["AppValidationResponse"]
    if any(i.get("ErrorType") == "Error" for i in validation.get("ValidationIssueList", [])):
        raise RuntimeError("App validation errors: " + json.dumps(validation))
    c.api("POST", "PublishXml", json={"ParameterXml": f"<importexportxml><appmodules><appmodule>{APP}</appmodule></appmodules></importexportxml>"})
    print(json.dumps({"appPublished": APP, "validation": validation}))


def seed_demo(c):
    user = c.api("GET", f"systemusers({c.who['UserId']})", params={"$select": "internalemailaddress"})
    if user["internalemailaddress"].lower() != "vuong.nguyenq@titancorpvn.com":
        raise RuntimeError("Demo recipients must be the known demo user")
    for kind in (789140000, 789140001, 789140002):
        for step in (1, 2):
            name = f"EPIC3-DEMO {kind} step {step}"
            existing = c.rows("fmc_approvalroute", f"fmc_name eq '{name}'", "fmc_approvalrouteid")
            if existing:
                continue  # Never overwrite manually adjusted demo configuration.
            c.api("POST", c.sets["fmc_approvalroute"], json={"fmc_name": name, "fmc_department": "EPIC3-DEMO", "fmc_requesttype": kind,
                "fmc_stepno": step, "fmc_minvalue": 0, "fmc_minseverity": 789142000, "fmc_isactive": True,
                "fmc_approveremail": user["internalemailaddress"], "fmc_escalationemail": user["internalemailaddress"],
                "fmc_approverrole": "FMC BMS Demo Operator", "fmc_sladays": 1, "fmc_reminderhours": 24, "fmc_escalationhours": 0})
    print("Isolated EPIC3-DEMO routes ready; both steps use the demo user, not a segregation-of-duties policy.")


def verify(c):
    for table, fields in {"fmc_fmrequest": ["fmc_workflowrevision", "fmc_workflowplan", "fmc_reminderon", "fmc_overdueon", "fmc_escalatedon"], "fmc_requesthistory": ["fmc_actorid", "fmc_command", "fmc_resultrevision"]}.items():
        for field in fields:
            c.api("GET", f"EntityDefinitions(LogicalName='{table}')/Attributes(LogicalName='{field}')", params={"$select": "LogicalName"})
    flows = c.rows("workflows", f"name eq '{FLOW}' and category eq 5 and type eq 1", "workflowid,name,statecode,clientdata")
    if len(flows) != 1 or flows[0]["statecode"] != 1:
        raise RuntimeError("Expected one enabled deadline flow")
    flow = json.loads(flows[0].pop("clientdata"))
    definition = flow["properties"]["definition"]
    if definition["triggers"]["Every_five_minutes"]["recurrence"] != {"frequency": "Minute", "interval": 5}:
        raise RuntimeError("Live recurrence differs")
    if definition["actions"]["For_each_request"]["actions"]["Process_deadline"]["inputs"]["parameters"]["actionName"] != "fmc_ProcessFmRequestDeadline":
        raise RuntimeError("Live deadline action differs")
    apis = c.rows("customapis", "uniquename eq 'fmc_TransitionFmRequest' or uniquename eq 'fmc_ProcessFmRequestDeadline'", "uniquename,customapiid,_plugintypeid_value,executeprivilegename")
    if len(apis) != 2 or any(not a.get("_plugintypeid_value") or a.get("executeprivilegename") != "prvWritefmc_fmrequest" for a in apis):
        raise RuntimeError("Custom API binding or privilege differs")
    steps = c.rows("sdkmessageprocessingsteps", "startswith(name,'FM Workflow:')", "name,statecode,mode,stage")
    if len(steps) != 10 or any(s["statecode"] != 0 or s["mode"] != 0 or s["stage"] != 20 for s in steps):
        raise RuntimeError("Expected ten synchronous active PreOperation guards")
    resources = c.rows("webresourceset", f"name eq '{PAGE}' or name eq '{SCRIPT}'", "name,webresourceid,content")
    if len(resources) != 2:
        raise RuntimeError("Missing workflow webresources")
    for r in resources:
        data = base64.b64decode(r.pop("content")); local = (ROOT / "dataverse/webresources" / r["name"]).read_bytes()
        if data != local:
            raise RuntimeError("Live webresource differs: " + r["name"])
        r["sha256"] = hashlib.sha256(data).hexdigest()
    form = c.rows("systemforms", "objecttypecode eq 'fmc_fmrequest' and type eq 2 and formactivationstate eq 1", "formxml")
    if len(form) != 1 or ET.fromstring(form[0]["formxml"]).find(".//control[@id='WebResource_fmc_workflow']") is None:
        raise RuntimeError("Workflow form panel missing")
    validation = c.api("GET", f"ValidateApp(AppModuleId={APP})")["AppValidationResponse"]
    if not validation["ValidationSuccess"]:
        raise RuntimeError("App validation failed")
    receipt = {"organization": c.who["OrganizationId"], "flows": flows, "apis": apis, "guardCount": len(steps), "resources": resources, "appValidation": validation}
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    (ARTIFACTS / "deployment-verification.json").write_text(json.dumps(receipt, indent=2), encoding="utf-8")
    print(json.dumps(receipt))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["inspect", "deploy-ui", "deploy-resources", "deploy-flow", "seed-demo", "verify"])
    args = parser.parse_args(); c = Client()
    if args.mode == "inspect":
        for table in ("fmc_fmrequest", "fmc_approvalroute"):
            print(json.dumps({"table": table, "forms": c.rows("systemforms", f"objecttypecode eq '{table}' and type eq 2", "formid,name,formactivationstate")}))
        print(json.dumps(c.rows("sitemaps", "sitemapnameunique eq 'fmc_FMCBMSDemo'", "sitemapid,sitemapnameunique")))
    else:
        {"deploy-ui": deploy_ui, "deploy-resources": deploy_resources, "deploy-flow": deploy_flow, "seed-demo": seed_demo, "verify": verify}[args.mode](c)


if __name__ == "__main__":
    main()
