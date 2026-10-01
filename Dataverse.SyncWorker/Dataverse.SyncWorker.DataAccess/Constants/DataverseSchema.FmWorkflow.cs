// Shared with the sandbox plug-in: keep wire names and choice values in one place.
namespace Dataverse.SyncWorker.DataAccess.Constants
{
    public static partial class DataverseSchema
    {
        public static class Workflow
        {
            public const string Request = "fmc_fmrequest", Route = "fmc_approvalroute", History = "fmc_requesthistory";
            public const string TransitionApi = "fmc_TransitionFmRequest", DeadlineApi = "fmc_ProcessFmRequestDeadline";
            public const string Name = "fmc_name", Description = "fmc_description", Type = "fmc_requesttype";
            public const string Status = "fmc_requeststatus", LegacyStatus = "fmc_status", Department = "fmc_department";
            public const string Value = "fmc_estimatedvalue", Severity = "fmc_riskseverity", Building = "fmc_buildingcode";
            public const string Likelihood = "fmc_likelihood", Impact = "fmc_impact", Mitigation = "fmc_mitigation", ReviewDate = "fmc_reviewdate";
            public const string Requester = "fmc_requesteremail", Approver = "fmc_currentapproveremail", Step = "fmc_currentstep";
            public const string Due = "fmc_stepdueon", SubmittedOn = "fmc_submittedon", CompletedOn = "fmc_completedon";
            public const string Plan = "fmc_workflowplan", Revision = "fmc_workflowrevision";
            public const string EvidenceReading = "fmc_evidencereadingid", EvidenceReadingIds = "fmc_evidencereadingids", EvidenceSnapshot = "fmc_evidencesnapshot";
            public const string ReadingTable = "fmc_bmsreadingsnapshot";
            public const string ReminderOn = "fmc_reminderon", OverdueOn = "fmc_overdueon", EscalatedOn = "fmc_escalatedon";
            public const string RouteApprover = "fmc_approveremail", Escalation = "fmc_escalationemail", Role = "fmc_approverrole";
            public const string Active = "fmc_isactive", MinValue = "fmc_minvalue", MinSeverity = "fmc_minseverity", Sla = "fmc_sladays";
            public const string ReminderHours = "fmc_reminderhours", EscalationHours = "fmc_escalationhours", StepNo = "fmc_stepno";
            public const string RequestLookup = "fmc_requestid", Action = "fmc_action", ActorEmail = "fmc_actoremail";
            public const string ActorId = "fmc_actorid", Comments = "fmc_comments", ActedOn = "fmc_actedon", FlowRun = "fmc_flowrunid";
            public const string Command = "fmc_command", ResultRevision = "fmc_resultrevision", ResultStatus = "fmc_resultstatus";
            public const int Draft = 789141000, Submitted = 789141001, InApproval = 789141002, Approved = 789141003, Rejected = 789141004, Closed = 789141005;
            public const int Ciwg = 789140000, Risk = 789140001, Project = 789140002;
            public const int Low = 789142000, Medium = 789142001, High = 789142002, Critical = 789142003;
            public const int SubmitHistory = 789143000, ApproveHistory = 789143001, RejectHistory = 789143002, RemindHistory = 789143003, EscalateHistory = 789143004, CloseHistory = 789143005, OverdueHistory = 789143006;
        }
    }
}
