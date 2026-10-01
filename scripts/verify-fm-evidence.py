"""Verify FM evidence metadata, or exercise live guards in an always-rolled-back change set.

No request, history, outbox or asynchronous notification is committed by rollback-smoke.
Uses the existing Developer identity and organization guard; does not grant roles.
"""
import argparse
import importlib
import json
import uuid
from datetime import datetime, timezone
from pathlib import Path

fm = importlib.import_module("provision-fm-workflow")


def inspect(c):
    meta = {}
    for field, kind in (("fmc_evidencereadingid", "String"), ("fmc_evidencesnapshot", "Memo")):
        attr = c.api("GET", f"EntityDefinitions(LogicalName='fmc_fmrequest')/Attributes(LogicalName='{field}')",
                     params={"$select": "LogicalName,AttributeType,MetadataId"})
        assert attr["AttributeType"] == kind, attr
        meta[field] = attr["MetadataId"]
    reading_set = c.api("GET", "EntityDefinitions(LogicalName='fmc_bmsreadingsnapshot')",
                        params={"$select": "EntitySetName"})["EntitySetName"]
    rows = c.api("GET", reading_set, params={"$select": "fmc_bmsreadingsnapshotid,fmc_sqlreadingid,fmc_readingvalue,fmc_readingtime",
        "$filter": "fmc_readingvalue ne null and fmc_readingtime ne null and fmc_sqlreadingid ne null and fmc_objectid ne null",
        "$orderby": "fmc_readingtime desc", "$top": 1})["value"]
    assert rows, "No complete synced reading available"
    assemblies = c.rows("pluginassemblies", "name eq 'FMCentralBms.Plugins'", "version")
    assert len(assemblies) == 1 and assemblies[0]["version"] == "1.0.0.14", assemblies
    return {"organization": c.who["OrganizationId"], "columns": meta, "pluginVersion": assemblies[0]["version"],
            "sampleReadingId": rows[0]["fmc_bmsreadingsnapshotid"], "sampleSqlId": rows[0]["fmc_sqlreadingid"]}


def change_set(c, operations):
    batch, change = "batch_" + uuid.uuid4().hex, "changeset_" + uuid.uuid4().hex
    lines = ["--" + batch, "Content-Type: multipart/mixed; boundary=" + change, ""]
    for i, (method, path, body) in enumerate(operations, 1):
        lines += ["--" + change, "Content-Type: application/http", "Content-Transfer-Encoding: binary", "Content-ID: " + str(i), "",
                  f"{method} {fm.ORG}/api/data/v9.2/{path} HTTP/1.1", "Content-Type: application/json; type=entry", "",
                  json.dumps(body), ""]
    lines += ["--" + change + "--", "--" + batch + "--", ""]
    response = c.session.post(fm.ORG + "/api/data/v9.2/$batch", data="\r\n".join(lines).encode(),
                             headers={"Content-Type": "multipart/mixed; boundary=" + batch}, timeout=120)
    # Dataverse may return the failed change set with HTTP 400 at the outer level too.
    if not response.ok and not (response.status_code == 400 and response.headers.get("Content-Type", "").startswith("multipart/mixed")):
        raise RuntimeError(f"Batch rejected ({response.status_code}): {response.text[:3500]}")
    return response.text


def rollback_smoke(c, evidence):
    outcomes = []
    for mode, expected in (("draft-tamper", "field is managed: fmc_evidencesnapshot"),
                           ("submitted-lock", "Only Draft requests may be edited/deleted")):
        request_id = str(uuid.uuid4())
        request_set = c.sets["fmc_fmrequest"]
        path = f"{request_set}({request_id})"
        create = {"fmc_fmrequestid": request_id, "fmc_name": "EVIDENCE-ROLLBACK-" + uuid.uuid4().hex,
                  "fmc_description": "Transactional evidence verification; never committed.", "fmc_requesttype": 789140000,
                  "fmc_department": "EPIC3-DEMO", "fmc_evidencereadingid": evidence["sampleReadingId"]}
        operations = [("POST", request_set, create)]
        if mode == "submitted-lock":
            # Use an existing non-Draft only for a rejected, always-rolled-back update.
            locked = c.rows("fmc_fmrequest", "fmc_requeststatus ne 789141000 and fmc_requeststatus ne null",
                            "fmc_fmrequestid,fmc_evidencereadingid,fmc_evidencesnapshot", **{"$top": 1})
            assert locked, "No existing non-Draft request available to verify the lock"
            request_id = locked[0]["fmc_fmrequestid"]
            path = f"{request_set}({request_id})"
            operations = [("PATCH", path, {"fmc_evidencereadingid": evidence["sampleReadingId"]})]
        else:
            operations.append(("PATCH", path, {"fmc_evidencesnapshot": "FORGED"}))
        # Always force rollback even if the guard under test is broken. Unknown columns are rejected by the platform.
        operations.append(("PATCH", path, {"fmc_intentionally_nonexistent_evidence_test_column": True}))
        result = change_set(c, operations)
        remaining = c.rows("fmc_fmrequest", f"fmc_fmrequestid eq {request_id}",
                           "fmc_fmrequestid,fmc_evidencereadingid,fmc_evidencesnapshot")
        if mode == "draft-tamper":
            assert not remaining, "Change set unexpectedly committed: " + request_id
        else:
            assert remaining == locked, "Existing request unexpectedly changed"
        assert expected in result, result[:4000]
        if mode == "draft-tamper":
            history = c.rows("fmc_requesthistory", f"_fmc_requestid_value eq {request_id}", "fmc_requesthistoryid")
            assert not history, "History unexpectedly committed"
        outcomes.append({"test": mode, "requestId": request_id, "guardVerified": True, "rolledBack": True})
    return outcomes


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["inspect", "rollback-smoke"])
    args = parser.parse_args()
    c = fm.Client()
    receipt = inspect(c)
    if args.mode == "rollback-smoke":
        receipt["tests"] = rollback_smoke(c, receipt)
    receipt["verifiedAt"] = datetime.now(timezone.utc).isoformat()
    folder = Path(__file__).resolve().parents[1] / ".artifacts/fm-reading-evidence"
    folder.mkdir(parents=True, exist_ok=True)
    (folder / (args.mode + ".json")).write_text(json.dumps(receipt, indent=2), encoding="utf-8")
    print(json.dumps(receipt))


if __name__ == "__main__":
    main()
