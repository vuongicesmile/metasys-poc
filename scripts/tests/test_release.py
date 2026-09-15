import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("release", Path(__file__).parents[1] / "release/release.py")
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class ReleaseTests(unittest.TestCase):
    def test_release_workflow_uses_hosted_oidc_not_local_runner(self):
        workflow = (release.ROOT / ".github/workflows/release-dev.yml").read_text(encoding="utf-8")
        self.assertIn("runs-on: windows-latest", workflow)
        self.assertIn("id-token: write", workflow)
        self.assertIn("--githubFederated", workflow)
        self.assertIn("Restore last successful release receipt", workflow)
        self.assertNotIn("runs-on: [self-hosted", workflow)

    def test_semantic_tag_maps_to_solution_version(self):
        self.assertEqual((1, 2, 3), release.version("dev-v1.2.3"))

    def test_invalid_or_injectable_tags_rejected(self):
        for tag in ("v1.0.0", "dev-v1.0", "dev-v01.2.3", "dev-v1.2.3;whoami", "dev-v65536.0.0", "--help"):
            with self.subTest(tag=tag), self.assertRaises(ValueError):
                release.version(tag)

    def test_docs_only_does_not_select_runtime_deployments(self):
        plan = release.classify(["docs/runbooks/example.md", "README.md"])
        self.assertFalse(any(plan[k] for k in ("dashboard", "backend", "plugin", "solution", "webResources")))

    def test_dashboard_change_uses_page_deployment(self):
        plan = release.classify(["dataverse/app-source/fmc-bms-demo/src/services/localization.ts"])
        self.assertTrue(plan["dashboard"])
        self.assertFalse(plan["solution"])

    def test_solution_import_reapplies_authored_dashboard(self):
        self.assertTrue(release.classify(["dataverse/FMCentralBms/Other/Solution.xml"])["dashboard"])

    def test_plugin_requires_signed_build_and_page_validation(self):
        plan = release.classify(["plugins/FMCentralBms.Plugins/RequestBmsSync.cs"])
        self.assertTrue(plan["plugin"])
        self.assertTrue(plan["dashboard"])

    def test_backend_change_is_packaged_separately(self):
        plan = release.classify(["DataverseSyncWorker/Services/SyncEngine.cs"])
        self.assertTrue(plan["backend"])
        self.assertFalse(plan["solution"])

    def test_unsupported_schema_input_fails_explicitly(self):
        for files in (["sql/new-migration.sql"], ["dataverse/app-source/fmc-bms-demo/app-spec.json"]):
            with self.subTest(files=files), self.assertRaises(ValueError):
                release.classify(files)

    def test_web_resource_selects_exact_authored_file(self):
        plan = release.classify(["dataverse/app-source/BmsEventDemo.html"])
        self.assertEqual(["dataverse/app-source/BmsEventDemo.html"], plan["webResources"])

    def test_bom_and_crlf_readback_normalization(self):
        self.assertEqual(release.normalize("\ufeffa\r\nb\r\n"), release.normalize("a\nb\n"))

    def test_pac_access_token_ignores_cli_headers(self):
        token = "a" * 600 + "." + "b" * 600 + "." + "c" * 600
        self.assertEqual(token, release.pac_access_token("PAC CLI\nConnected as app\n" + token + "\n"))

    def test_pac_access_token_rejects_missing_or_ambiguous_output(self):
        token = "a" * 600 + "." + "b" * 600 + "." + "c" * 600
        for output in ("PAC CLI only", token + "\n" + token):
            with self.subTest(output_length=len(output)), self.assertRaises(ValueError):
                release.pac_access_token(output)

    def test_release_config_resolves_all_sync_flow_placeholders(self):
        self.assertEqual(
            {
                "__FLOW_ID__": "FMC - App Request BMS Sync Event",
                "__SPO_FLOW_ID__": "FMC - Request SPO Sync",
                "__FULL_FLOW_ID__": "FMC - Request Full Sync",
            },
            release.CONFIG["flowPlaceholders"],
        )

    def test_command_failure_propagates(self):
        import subprocess
        with patch.object(release.subprocess, "run", side_effect=subprocess.CalledProcessError(1, "pac")):
            with self.assertRaises(subprocess.CalledProcessError):
                release.command("pac", "solution", "import")

    def test_build_failure_does_not_deploy_or_advance_last_success(self):
        import tempfile
        import json
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            state = root / 'state'
            state.mkdir()
            previous = {'tag': 'dev-v1.0.0', 'commit': 'previous'}
            release.write_json(state / 'last-success.json', previous)
            plan = {'tag': 'dev-v1.0.1', 'commit': 'new', 'alreadyDeployed': False}
            with patch.object(release, 'ROOT', root), patch.object(release, 'state_directory', return_value=state), \
                 patch.object(release, 'get_plan', return_value=plan), \
                 patch.object(release, 'validate', side_effect=ValueError('test build failed')), \
                 patch.object(release, 'deploy') as deploy, \
                 patch('sys.argv', ['release.py', 'deploy', '--tag', 'dev-v1.0.1']):
                with self.assertRaises(ValueError):
                    release.main()
                deploy.assert_not_called()
            self.assertEqual(previous, json.loads((state / 'last-success.json').read_text()))
            self.assertEqual('Failed', json.loads((state / 'last-attempt.json').read_text())['status'])

    def test_already_deployed_tag_does_not_repeat_build_or_writes(self):
        import tempfile
        with tempfile.TemporaryDirectory() as directory, \
             patch.object(release, 'state_directory', return_value=Path(directory)), \
             patch.object(release, 'get_plan', return_value={'alreadyDeployed': True}), \
             patch.object(release, 'validate') as validate, patch.object(release, 'deploy') as deploy, \
             patch('sys.argv', ['release.py', 'deploy', '--tag', 'dev-v1.0.1']):
            release.main()
            validate.assert_not_called()
            deploy.assert_not_called()


if __name__ == "__main__":
    unittest.main()
