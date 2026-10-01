import importlib.util
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[1] / "provision-user-notification-flow.py"
SPEC = importlib.util.spec_from_file_location("user_notification_flow", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


def test_notification_flow_is_solution_aware_and_receipted():
    clientdata = MODULE.build(
        "fmc_notifications",
        "dataverse-connection",
        "outlook-connection",
        "teams-connection",
    )
    properties = clientdata["properties"]
    refs = properties["connectionReferences"]
    definition = properties["definition"]
    trigger = definition["triggers"]["When_a_pending_email_notification_is_created"]
    actions = definition["actions"]

    assert refs[MODULE.DV]["connection"]["connectionReferenceLogicalName"] == (
        MODULE.DV_CONNECTION_LOGICAL
    )
    assert refs[MODULE.OUTLOOK]["connection"]["connectionReferenceLogicalName"] == (
        MODULE.OUTLOOK_CONNECTION_LOGICAL
    )
    assert refs[MODULE.TEAMS]["connection"]["connectionReferenceLogicalName"] == (
        MODULE.TEAMS_CONNECTION_LOGICAL
    )
    assert trigger["inputs"]["parameters"]["subscriptionRequest/message"] == 1
    assert trigger["inputs"]["parameters"]["subscriptionRequest/entityname"] == (
        "fmc_notification"
    )
    assert str(MODULE.PENDING) in trigger["inputs"]["parameters"][
        "subscriptionRequest/filterexpression"
    ]
    assert actions["Send_email"]["inputs"]["host"]["operationId"] == "SendEmailV2"
    assert actions["Send_email"]["runtimeConfiguration"]["retryPolicy"] == {
        "type": "none"
    }
    teams = actions["Send_Teams_message"]
    assert teams["inputs"]["host"]["operationId"] == "PostMessageToConversation"
    assert teams["inputs"]["parameters"]["poster"] == "Flow bot"
    assert teams["inputs"]["parameters"]["location"] == "Chat with Flow bot"
    assert teams["inputs"]["parameters"]["body/recipient"].startswith("@concat(")
    assert teams["runAfter"] == {"Send_email": ["Succeeded"]}
    assert teams["runtimeConfiguration"]["retryPolicy"] == {"type": "none"}
    assert actions["Mark_sent"]["runAfter"] == {
        "Send_Teams_message": ["Succeeded"]
    }
    assert actions["Mark_sent"]["inputs"]["parameters"]["item/fmc_status"] == (
        MODULE.SENT
    )
    assert actions["Mark_email_failed"]["inputs"]["parameters"]["item/fmc_status"] == (
        MODULE.FAILED
    )
    assert actions["Mark_Teams_failed"]["inputs"]["parameters"]["item/fmc_status"] == (
        MODULE.FAILED
    )
