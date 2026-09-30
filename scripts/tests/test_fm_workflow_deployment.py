import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("fm", Path(__file__).parents[1] / "provision-fm-workflow.py")
fm = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fm)


class FlowTests(unittest.TestCase):
    def test_schedule_is_serial_and_lists_all_pending_pages(self):
        flow = fm.build_flow("verified_entityset", "existing_connection")["properties"]
        definition = flow["definition"]
        trigger = definition["triggers"]["Every_five_minutes"]
        self.assertEqual({"frequency": "Minute", "interval": 5}, trigger["recurrence"])
        self.assertEqual(1, trigger["runtimeConfiguration"]["concurrency"]["runs"])
        listing = definition["actions"]["List_pending_requests"]
        self.assertEqual("verified_entityset", listing["inputs"]["parameters"]["entityName"])
        self.assertEqual(100000, listing["runtimeConfiguration"]["paginationPolicy"]["minimumItemCount"])
        self.assertEqual(fm.REFERENCE, flow["connectionReferences"][fm.DV]["connection"]["connectionReferenceLogicalName"])

    def test_flow_cannot_spoof_deadlines_or_approval_decisions(self):
        action = fm.build_flow("requests", "connection")["properties"]["definition"]["actions"]["For_each_request"]["actions"]["Process_deadline"]["inputs"]["parameters"]
        self.assertEqual({"actionName", "item/RequestId"}, set(action))
        self.assertEqual("fmc_ProcessFmRequestDeadline", action["actionName"])


if __name__ == "__main__":
    unittest.main()
