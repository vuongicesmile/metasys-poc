"""Tag-based Developer release. Tokens stay in memory; data sync is never invoked."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import urllib.request
import xml.etree.ElementTree as ET
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[2]
CONFIG = json.loads((ROOT / "config/release.dev.json").read_text(encoding="utf-8-sig"))


def command(*args, cwd=ROOT, capture=False):
    executable = shutil.which(str(args[0])) or str(args[0])
    result = subprocess.run([executable, *map(str, args[1:])], cwd=cwd, check=True,
                            stdout=subprocess.PIPE if capture else None, text=True, encoding="utf-8")
    return result.stdout.strip() if capture else None


def pac_access_token(output):
    candidates = [line.strip() for line in output.splitlines()
                  if len(line.strip()) > 500 and line.strip().count(".") == 2]
    if len(candidates) != 1:
        raise ValueError("PAC did not return exactly one Dataverse access token")
    return candidates[0]


def access_token():
    python = os.environ.get("FMC_AZURE_PYTHON")
    if python:
        if not Path(python).is_file():
            raise ValueError("FMC_AZURE_PYTHON does not identify an existing executable")
        token = command(python, ROOT / "scripts/get-dataverse-token.py", capture=True)
    else:
        token = pac_access_token(command("pac", "auth", "token", capture=True))
    if token.count(".") != 2:
        raise ValueError("Dataverse access token acquisition failed")
    return token


def version(tag):
    match = re.fullmatch(r"dev-v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", tag)
    if not match:
        raise ValueError("Use an immutable dev-vMAJOR.MINOR.PATCH tag, for example dev-v1.0.1")
    parts = tuple(map(int, match.groups()))
    if any(n > 65535 for n in parts):
        raise ValueError("Each Dataverse version segment must be <= 65535")
    return parts


def classify(files):
    paths = set(files)
    infrastructure = any(p.startswith(("scripts/release/", "config/release.", ".github/workflows/")) for p in paths)
    solution = any(p.startswith("dataverse/FMCentralBms/") for p in paths)
    plugin = any(p.startswith("plugins/") for p in paths)
    web = [p for p in CONFIG["webResources"] if p in paths]
    dashboard = solution or plugin or infrastructure or any(p.startswith(CONFIG["pageDirectory"] + "/") for p in paths)
    backend = infrastructure or any(p.startswith(("BmsIngestionApp/", "DataverseSyncWorker/", "FakeMetasysApi/", "tests/", "sql/"))
                                   or p.endswith((".sln", "Directory.Build.props", "Directory.Packages.props")) for p in paths)
    if CONFIG["pageDirectory"] + "/app-spec.json" in paths and not solution:
        raise ValueError("app-spec changed: build/export the model-driven solution into dataverse/FMCentralBms before tagging")
    if any(p.endswith("Provisioner.cs") for p in paths) and not solution:
        raise ValueError("Provisioning code changed: apply/verify and export the schema into dataverse/FMCentralBms before tagging")
    if any(p.startswith("sql/") for p in paths):
        raise ValueError("SQL migration changed: apply/verify it separately before setting a new deployment baseline")
    return dict(dashboard=dashboard, backend=backend, plugin=plugin, solution=solution,
                webResources=web, changedFiles=sorted(paths))


def state_directory():
    return Path(os.environ.get("FMC_RELEASE_STATE", str(Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "FMCentralBms/releases")))


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    temp.replace(path)


def get_plan(tag):
    version(tag)
    command("git", "fetch", "origin", "main", "--tags")
    sha = command("git", "rev-parse", f"refs/tags/{tag}^{{commit}}", capture=True)
    remote = command("git", "ls-remote", "origin", f"refs/tags/{tag}", f"refs/tags/{tag}^{{}}", capture=True)
    remote_lines = [line.split()[0] for line in remote.splitlines()]
    if not remote_lines or sha != remote_lines[-1]:
        raise ValueError("The tag is not published or its remote SHA differs")
    if command("git", "rev-parse", "HEAD", capture=True) != sha:
        raise ValueError("Check out the exact release tag in an isolated checkout first")
    command("git", "merge-base", "--is-ancestor", sha, "origin/main")
    if command("git", "status", "--porcelain", "--untracked-files=no", capture=True):
        raise ValueError("Tracked files must match the tagged commit")
    state_path = state_directory() / "last-success.json"
    state = json.loads(state_path.read_text()) if state_path.exists() else None
    if state and state["organizationId"] != CONFIG["organizationId"]:
        raise ValueError("Release state belongs to a different organization")
    if state and state["tag"] == tag:
        if state["commit"] != sha:
            raise ValueError("Release tag was moved; create a new tag")
        return {**state, "alreadyDeployed": True}
    if state and version(tag) <= version(state["tag"]):
        raise ValueError("Refusing an older release: revert code on main and create a newer tag")
    baseline = state["commit"] if state else CONFIG["baselineCommit"]
    command("git", "merge-base", "--is-ancestor", baseline, sha)
    files = command("git", "diff", "--name-only", baseline, sha, capture=True).splitlines()
    plan = classify(files)
    plan.update(tag=tag, commit=sha, baseline=baseline, organizationId=CONFIG["organizationId"],
                solutionVersion=".".join(map(str, (*version(tag), 0))), alreadyDeployed=False)
    return plan


def validate(plan, out):
    if plan["backend"] or plan["plugin"]:
        command("dotnet", "test", "tests/MetasysPoc.Tests/MetasysPoc.Tests.csproj", "-c", "Release")
        for project in ("FakeMetasysApi", "BmsIngestionApp", "DataverseSyncWorker"):
            dest = out / "workers" / project
            command("dotnet", "publish", f"{project}/{project}.csproj", "-c", "Release", "-o", dest)
            # Worker packages are explicit handoff artifacts, not automatic data-sync execution.
    if plan["dashboard"]:
        app = ROOT / CONFIG["pageDirectory"]
        command("npm", "ci", "--no-audit", "--no-fund", cwd=app)
        command("npm", "run", "check", cwd=app)
        command("npm", "test", cwd=app)
    if plan["webResources"] or plan["dashboard"]:
        command("node", "--test", "scripts/tests/Test-BmsEventDemoLanguage.mjs", "scripts/tests/Test-EquipmentPluginTest.mjs")
    if plan["plugin"]:
        key = os.environ.get("FMC_PLUGIN_SIGNING_KEY")
        if not key or not Path(key).is_file():
            raise ValueError("Plugin changed: set FMC_PLUGIN_SIGNING_KEY to the existing .snk; no cloud writes made")
        command("dotnet", "build", "plugins/FMCentralBms.Plugins/FMCentralBms.Plugins.csproj", "-c", "Release",
                f"-p:AssemblyOriginatorKeyFile={key}")
        dll = ROOT / "plugins/FMCentralBms.Plugins/bin/Release/net48/FMCentralBms.Plugins.dll"
        manifests = list((ROOT / "dataverse/FMCentralBms/PluginAssemblies").glob("*/FMCentralBmsPlugins.dll.data.xml"))
        if len(manifests) != 1:
            raise ValueError("Expected one plugin manifest")
        identity = command("powershell", "-NoProfile", "-File", ROOT / "scripts/release/Get-AssemblyIdentity.ps1", "-Path", dll, capture=True)
        if identity != ET.parse(manifests[0]).getroot().get("FullName"):
            raise ValueError("Plugin identity differs from solution metadata; use the existing signing key and update the assembly contract")


class Dataverse:
    def __init__(self):
        self.token = access_token()
        who = self.request("WhoAmI")
        if who["OrganizationId"].lower() != CONFIG["organizationId"]:
            raise ValueError("Unexpected live organization")
        self.solution = self.request("solutions?$select=solutionid,version,uniquename&$expand=publisherid($select=uniquename)&$filter=uniquename eq 'FMCentralBms'")["value"]
        if len(self.solution) != 1 or self.solution[0]["publisherid"]["uniquename"] != CONFIG["publisher"]:
            raise ValueError("Unexpected solution or publisher")
        self.solution = self.solution[0]
        self.webresource_set = self.request("EntityDefinitions(LogicalName='webresource')?$select=EntitySetName")["EntitySetName"]

    def request(self, path, method="GET", body=None):
        url = CONFIG["environmentUrl"] + "api/data/v9.2/" + path.replace(" ", "%20")
        headers = {"Authorization": "Bearer " + self.token, "Accept": "application/json",
                   "Content-Type": "application/json; charset=utf-8", "MSCRM.SolutionUniqueName": CONFIG["solution"]}
        data = json.dumps(body).encode() if body is not None else None
        with urllib.request.urlopen(urllib.request.Request(url, data=data, headers=headers, method=method), timeout=120) as response:
            content = response.read()
            return json.loads(content) if content else None


def normalize(text):
    return text.lstrip("\ufeff").replace("\r\n", "\n").rstrip()


def pac(*args):
    command("pac", *args)


def deploy(plan, out, receipt):
    dv = Dataverse()
    target_version = tuple(map(int, plan["solutionVersion"].split(".")))
    if tuple(map(int, dv.solution["version"].split("."))) > target_version:
        raise ValueError("Live solution is newer than this release")
    env = CONFIG["environmentUrl"]
    app = ROOT / CONFIG["pageDirectory"]
    page_code = out / "page-input" / CONFIG["pageFile"]
    page_code.parent.mkdir(parents=True)
    shutil.copyfile(app / CONFIG["pageFile"], page_code)
    # Check compilation before any online write. PAC owns platform RuntimeTypes.
    if plan["dashboard"] or plan["solution"]:
        pac("model", "genpage", "transpile", "--environment", env, "--code-file", page_code,
            "--output-file", out / "dashboard.compiled.js", "--data-sources", CONFIG["dataSources"])
    pac("solution", "export", "--environment", env, "--name", CONFIG["solution"], "--path", out / "before.zip", "--managed", "false")
    receipt["stages"].append("backup-exported")
    if plan["solution"] or plan["plugin"]:
        staged = out / "solution-source"
        shutil.copytree(ROOT / "dataverse/FMCentralBms", staged)
        metadata = staged / "Other/Solution.xml"
        doc = ET.parse(metadata)
        doc.find(".//SolutionManifest/Version").text = plan["solutionVersion"]
        doc.write(metadata, encoding="utf-8", xml_declaration=True)
        if plan["plugin"]:
            dll = ROOT / "plugins/FMCentralBms.Plugins/bin/Release/net48/FMCentralBms.Plugins.dll"
            matches = list(staged.glob("PluginAssemblies/*/FMCentralBmsPlugins.dll"))
            if len(matches) != 1:
                raise ValueError("Expected one plugin assembly in solution source")
            # validate() already checked the signed identity before online writes.
            shutil.copyfile(dll, matches[0])
        pac("solution", "pack", "--zipfile", out / "solution.zip", "--folder", staged, "--packagetype", "Unmanaged")
        pac("solution", "import", "--environment", env, "--path", out / "solution.zip", "--publish-changes")
        receipt["stages"].append("solution-imported")
    # Always overlay authored resources after a solution import, which may contain an older export.
    resources = CONFIG["webResources"] if plan["solution"] or plan["plugin"] else plan["webResources"]
    import base64
    for source in resources:
        name = CONFIG["webResources"][source]
        rows = dv.request(f"{dv.webresource_set}?$select=webresourceid,name&$filter=name eq '{name}'")["value"]
        if len(rows) != 1:
            raise ValueError(f"Expected one existing web resource: {name}")
        content = (ROOT / source).read_text(encoding="utf-8-sig")
        for placeholder, flow_name in CONFIG.get("flowPlaceholders", {}).items():
            if placeholder not in content:
                continue
            escaped_name = flow_name.replace("'", "''")
            flows = dv.request(
                f"workflows?$select=workflowid&$filter=name eq '{escaped_name}' and category eq 5 and type eq 1"
            )["value"]
            if len(flows) != 1:
                raise ValueError(f"Expected one flow for {placeholder}: {flow_name}")
            content = content.replace(placeholder, flows[0]["workflowid"])
        content = content.replace("__ENVIRONMENT_ID__", CONFIG["environmentId"])
        unresolved = [marker for marker in ("__FLOW_ID__", "__SPO_FLOW_ID__", "__FULL_FLOW_ID__", "__ENVIRONMENT_ID__") if marker in content]
        if unresolved:
            raise ValueError(f"Unresolved web-resource placeholders in {source}: {', '.join(unresolved)}")
        resource_id = rows[0]["webresourceid"]
        dv.request(f"{dv.webresource_set}({resource_id})", "PATCH", {"content": base64.b64encode(content.encode()).decode()})
        dv.request("PublishXml", "POST", {"ParameterXml": f"<importexportxml><webresources><webresource>{resource_id}</webresource></webresources></importexportxml>"})
        actual = dv.request(f"{dv.webresource_set}({resource_id})?$select=content")["content"]
        if normalize(base64.b64decode(actual).decode("utf-8-sig")) != normalize(content):
            raise ValueError(f"Web resource readback mismatch: {name}")
        receipt["stages"].append("verified:" + name)
    if plan["dashboard"] or plan["plugin"]:
        pac("model", "genpage", "upload", "--environment", env, "--app-id", CONFIG["appId"],
            "--page-id", CONFIG["pageId"], "--code-file", page_code, "--name", CONFIG["pageName"],
            "--data-sources", CONFIG["dataSources"], "--prompt", f"Deploy {plan['tag']} from commit {plan['commit']}",
            "--agent-message", "Publish tested repository source through the release pipeline.", "--model", "gpt-6")
        pac("model", "genpage", "download", "--environment", env, "--app-id", CONFIG["appId"],
            "--page-id", CONFIG["pageId"], "--output-directory", out / "page-readback")
        downloaded = out / "page-readback" / CONFIG["pageId"] / "page.tsx"
        if normalize(downloaded.read_text(encoding="utf-8-sig")) != normalize((app / CONFIG["pageFile"]).read_text(encoding="utf-8-sig")):
            raise ValueError("Dashboard source readback mismatch")
        receipt["stages"].append("dashboard-published-and-verified")
    validation = dv.request(f"ValidateApp(AppModuleId={CONFIG['appId']})")
    if not validation["AppValidationResponse"]["ValidationSuccess"]:
        raise ValueError("ValidateApp failed")
    dv.request(f"solutions({dv.solution['solutionid']})", "PATCH", {"version": plan["solutionVersion"]})
    actual = dv.request(f"solutions({dv.solution['solutionid']})?$select=version")
    if actual["version"] != plan["solutionVersion"]:
        raise ValueError("Solution version readback mismatch")
    pac("solution", "export", "--environment", env, "--name", CONFIG["solution"], "--path", out / "after.zip", "--managed", "false")
    pac("solution", "unpack", "--zipfile", out / "after.zip", "--folder", out / "exported-solution", "--packagetype", "Unmanaged")
    receipt["stages"].append("app-validated-and-solution-exported")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=["plan", "deploy"])
    parser.add_argument("--tag", required=True)
    args = parser.parse_args()
    state_dir = state_directory()
    state_dir.mkdir(parents=True, exist_ok=True)
    # OS lock is released even if the process is killed; serializes manual and Actions runs.
    import msvcrt
    with (state_dir / "deploy.lock").open("a+b") as lock:
        lock.seek(0); lock.write(b"0"); lock.flush(); lock.seek(0)
        msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
        plan = get_plan(args.tag)
        print(json.dumps(plan, indent=2), flush=True)
        if args.mode == "plan" or plan["alreadyDeployed"]:
            return
        run_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + str(os.getpid())
        # Keep backups outside Actions checkout: checkout cleanup must not erase them.
        out = state_dir / "artifacts" / args.tag / run_id
        out.mkdir(parents=True)
        receipt = {**plan, "status": "Running", "startedAt": run_id, "stages": [], "workerDeployment": "package-only",
                   "artifactDirectory": str(out)}
        write_json(out / "receipt.json", receipt)
        try:
            validate(plan, out)
            receipt["stages"].append("build-and-tests-passed")
            deploy(plan, out, receipt)
            receipt["status"] = "Succeeded"
            write_json(state_dir / "last-success.json", receipt)
        except Exception as error:
            receipt["status"] = "Failed"
            receipt["error"] = str(error)
            raise
        finally:
            write_json(out / "receipt.json", receipt)
            write_json(ROOT / ".artifacts/release" / args.tag / run_id / "receipt.json", receipt)
            write_json(state_dir / "last-attempt.json", receipt)


if __name__ == "__main__":
    main()
