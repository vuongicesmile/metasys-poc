"""Scoped Developer smoke test. Creates explicitly named demo requests and sends email ONLY to the known demo user.
Keeps request/history/outbox evidence; never edits existing business requests.
"""
import argparse
import importlib.util
import json
import uuid
from datetime import datetime, timezone
from pathlib import Path

spec = importlib.util.spec_from_file_location("fm", Path(__file__).with_name("provision-fm-workflow.py"))
fm = importlib.util.module_from_spec(spec); spec.loader.exec_module(fm)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["run", "receipts"])
    parser.add_argument("--scenario", choices=["all", "approve", "reject", "scheduled"], default="all")
    args = parser.parse_args(); c = fm.Client()
    fm.ARTIFACTS.mkdir(parents=True, exist_ok=True)
    evidence = fm.ARTIFACTS / "smoke.json"
    if args.mode == "receipts":
        runs = json.loads(evidence.read_text(encoding="utf-8"))
        for run in runs:
            print(json.dumps({"requestId": run["requestId"], "notifications": c.rows("fmc_notification", f"fmc_regardingid eq '{run['requestId']}'", "fmc_notificationid,fmc_eventtype,fmc_status,fmc_attempts,fmc_sentat,fmc_errormessage,fmc_flowrunid")}))
            if run["scenario"] == "scheduled":
                print(json.dumps({"scheduledHistory": c.rows("fmc_requesthistory", f"_fmc_requestid_value eq {run['requestId']}", "fmc_command,fmc_actedon,fmc_comments")}))
        return
    user = c.api("GET", f"systemusers({c.who['UserId']})", params={"$select": "internalemailaddress"})
    if user["internalemailaddress"].lower() != "vuong.nguyenq@titancorpvn.com":
        raise RuntimeError("Unexpected demo caller")
    # Verify every eligible configured route is isolated to the allowed recipient.
    routes = c.rows("fmc_approvalroute", "fmc_department eq 'EPIC3-DEMO' and fmc_isactive eq true", "fmc_approveremail,fmc_escalationemail")
    assert len(routes) == 6 and all(r["fmc_approveremail"].lower() == user["internalemailaddress"].lower() and r["fmc_escalationemail"].lower() == user["internalemailaddress"].lower() for r in routes)
    result = json.loads(evidence.read_text(encoding="utf-8")) if evidence.exists() else []

    def expected_failure(call, marker):
        try:
            call()
        except RuntimeError as e:
            if not any(value in str(e) for value in (marker if isinstance(marker, tuple) else (marker,))):
                raise
            return
        raise AssertionError("Expected server validation failure: " + marker)

    for scenario, kind in (("approve", 789140000), ("reject", 789140001), ("scheduled", 789140002)):
        if args.scenario not in ("all", scenario):
            continue
        id = str(uuid.uuid4()); table = c.sets["fmc_fmrequest"]
        payload = {"fmc_fmrequestid": id, "fmc_name": "EPIC3-SMOKE " + scenario + " " + datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S"), "fmc_department": "EPIC3-DEMO", "fmc_requesttype": kind, "fmc_description": "Isolated workflow verification. Not a real facilities request.", "fmc_estimatedvalue": 100}
        if kind == 789140001:
            payload.update({"fmc_likelihood": 2, "fmc_impact": 3, "fmc_riskseverity": 789142001, "fmc_mitigation": "Demo mitigation", "fmc_reviewdate": "2026-10-15"})
        c.api("POST", table, json=payload)
        run = {"requestId": id, "scenario": scenario, "operations": []}; result.append(run)
        evidence.write_text(json.dumps(result, indent=2), encoding="utf-8")

        def read():
            return c.api("GET", f"{table}({id})", params={"$select": "fmc_requeststatus,fmc_workflowrevision,fmc_currentstep,fmc_workflowplan,fmc_reminderon"})

        def command(action):
            record = read(); body = {"RequestId": id, "Action": action, "ExpectedRevision": record.get("fmc_workflowrevision") or 0, "OperationId": str(uuid.uuid4()), "Comment": "EPIC3 smoke: " + action}
            output = c.api("POST", "fmc_TransitionFmRequest", json=body); run["operations"].append(output)
            evidence.write_text(json.dumps(result, indent=2), encoding="utf-8")
            return body, output

        expected_failure(lambda: c.api("PATCH", f"{table}({id})", json={"fmc_requeststatus": 789141003}), "Use workflow actions")
        body, output = command("Submit")
        assert output["Status"] == 789141002 and read()["fmc_currentstep"] == 1
        if scenario == "scheduled":
            print(json.dumps({"scenario": scenario, "requestId": id, "status": "WAITING_FOR_SCHEDULED_REMINDER"}))
            continue
        retry = c.api("POST", "fmc_TransitionFmRequest", json=body)
        assert retry["Revision"] == output["Revision"]
        expected_failure(lambda: c.api("POST", "fmc_TransitionFmRequest", json={**body, "OperationId": str(uuid.uuid4()), "Action": "Approve"}), "Request changed")
        expected_failure(lambda: c.api("PATCH", f"{table}({id})", json={"fmc_description": "Bypass attempt"}), "Only Draft")
        expected_failure(lambda: c.api("DELETE", f"{table}({id})"), ("Only Draft", "cannot be deleted"))
        history = c.rows("fmc_requesthistory", f"_fmc_requestid_value eq {id}", "fmc_requesthistoryid")
        assert len(history) == 1
        expected_failure(lambda: c.api("PATCH", f"{c.sets['fmc_requesthistory']}({history[0]['fmc_requesthistoryid']})", json={"fmc_comments": "Overwrite"}), "append-only")
        if scenario == "approve":
            c.api("POST", "fmc_ProcessFmRequestDeadline", json={"RequestId": id})
            assert read()["fmc_reminderon"] is not None
            revision = read()["fmc_workflowrevision"]
            c.api("POST", "fmc_ProcessFmRequestDeadline", json={"RequestId": id})
            assert revision == read()["fmc_workflowrevision"]
            command("Approve"); assert read()["fmc_currentstep"] == 2
            command("Approve"); assert read()["fmc_requeststatus"] == 789141003
        else:
            command("Reject"); assert read()["fmc_requeststatus"] == 789141004
        command("Close"); assert read()["fmc_requeststatus"] == 789141005
        print(json.dumps({"scenario": scenario, "requestId": id, "status": "PASS"}))
    evidence.write_text(json.dumps(result, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
