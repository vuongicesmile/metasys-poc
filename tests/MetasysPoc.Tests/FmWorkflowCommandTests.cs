using System.Reflection;
using FMCentralBms.Plugins;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using S = Dataverse.SyncWorker.DataAccess.Constants.DataverseSchema.Workflow;

namespace MetasysPoc.Tests;

public class WorkflowProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> InvokeMethod = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => InvokeMethod(method!, args);
    public static T Make<T>(Func<MethodInfo, object?[]?, object?> call) where T : class
    { var result = Create<T, WorkflowProxy>(); ((WorkflowProxy)(object)result).InvokeMethod = call; return result; }
}

public sealed class FmWorkflowCommandTests
{
    private sealed class Host : IServiceProvider
    {
        public readonly Guid Actor = Guid.NewGuid();
        public Entity Request;
        public List<Entity> Created = [];
        public bool Admin = true, Conflict, FailEmail, Transaction = true;
        public string Message = S.TransitionApi;
        public Guid Operation = Guid.NewGuid();
        public ParameterCollection Input = [], Output = [], Shared = [];
        public Host()
        {
            Request = new Entity(S.Request, Guid.NewGuid()) { RowVersion = "1",
                [S.Name] = "Demo", [S.Description] = "Repair", ["ownerid"] = new EntityReference("systemuser", Actor),
                [S.Status] = new OptionSetValue(S.Draft), [S.Type] = new OptionSetValue(S.Ciwg), [S.Department] = "Demo" };
        }
        public object GetService(Type type)
        {
            if (type == typeof(ITracingService)) return WorkflowProxy.Make<ITracingService>((_, _) => null);
            if (type == typeof(IPluginExecutionContext)) return WorkflowProxy.Make<IPluginExecutionContext>((m, _) => m.Name switch {
                "get_MessageName" => Message, "get_IsInTransaction" => Transaction, "get_InitiatingUserId" or "get_UserId" => Actor,
                "get_InputParameters" => Input, "get_OutputParameters" => Output, "get_SharedVariables" => Shared,
                "get_PrimaryEntityName" => S.Request, "get_PrimaryEntityId" => Request.Id, "get_ParentContext" => null,
                _ => m.ReturnType.IsValueType ? Activator.CreateInstance(m.ReturnType) : null });
            if (type == typeof(IOrganizationServiceFactory)) return WorkflowProxy.Make<IOrganizationServiceFactory>((_, _) => Service());
            throw new NotSupportedException(type.Name);
        }
        private IOrganizationService Service() => WorkflowProxy.Make<IOrganizationService>((method, args) => {
            if (method.Name == "Retrieve")
            {
                if ((string)args![0]! == S.Request) return Copy(Request);
                return new Entity("systemuser", (Guid)args[1]!) { ["internalemailaddress"] = "demo@example.com", ["isdisabled"] = false };
            }
            if (method.Name == "RetrieveMultiple")
            {
                var q = (QueryExpression)args![0]!;
                IEnumerable<Entity> rows = q.EntityName switch {
                    S.History => Created.Where(e => e.LogicalName == S.History && e.Id == (Guid)q.Criteria.Conditions[0].Values[0]),
                    "role" => Admin ? [new Entity("role", Guid.NewGuid())] : [],
                    "systemuser" => [new Entity("systemuser", Actor) { ["internalemailaddress"] = "demo@example.com" }],
                    "environmentvariabledefinition" => [new Entity("environmentvariabledefinition", Guid.NewGuid()) { ["defaultvalue"] = "https://demo.crm.dynamics.com/main.aspx?appid=demo" }],
                    "environmentvariablevalue" => [],
                    S.Route => Enumerable.Range(1, 2).Select(i => new Entity(S.Route, Guid.NewGuid()) {
                        [S.Name] = "Step " + i, [S.Active] = true, [S.Type] = new OptionSetValue(S.Ciwg), [S.StepNo] = i,
                        [S.Sla] = 1, [S.ReminderHours] = 24, [S.RouteApprover] = "demo@example.com", [S.Escalation] = "demo@example.com" }),
                    _ => throw new NotSupportedException(q.EntityName) };
                return new EntityCollection(rows.ToList());
            }
            if (method.Name == "Execute")
            {
                var command = Assert.IsType<UpdateRequest>(args![0]);
                Assert.Equal(ConcurrencyBehavior.IfRowVersionMatches, command.ConcurrencyBehavior);
                if (Conflict || command.Target.RowVersion != Request.RowVersion) throw new InvalidPluginExecutionException("Concurrency conflict");
                foreach (var a in command.Target.Attributes) Request[a.Key] = a.Value;
                Request.RowVersion = (int.Parse(Request.RowVersion!) + 1).ToString();
                return new UpdateResponse();
            }
            if (method.Name == "Create")
            {
                var row = (Entity)args![0]!;
                if (FailEmail && row.LogicalName == "fmc_notification") throw new InvalidPluginExecutionException("Outbox unavailable");
                if (row.Id == Guid.Empty) row.Id = Guid.NewGuid();
                Created.Add(row); return row.Id;
            }
            throw new NotSupportedException(method.Name);
        });
        public void Command(string action, int? expected = null, string comment = "Reviewed")
        {
            Input = new ParameterCollection { ["RequestId"] = Request.Id, ["Action"] = action, ["ExpectedRevision"] = expected ?? Request.GetAttributeValue<int>(S.Revision), ["OperationId"] = Operation, ["Comment"] = comment };
            // Fake host models platform transaction rollback; live smoke tests must verify real transaction behavior.
            var before = Copy(Request); var count = Created.Count;
            try { new TransitionFmRequest().Execute(this); }
            catch { Request = before; Created.RemoveRange(count, Created.Count - count); throw; }
        }
        private static Entity Copy(Entity row)
        { var copy = new Entity(row.LogicalName, row.Id) { RowVersion = row.RowVersion }; foreach (var a in row.Attributes) copy[a.Key] = a.Value; return copy; }
    }

    [Fact] public void Two_step_approval_and_close_record_each_transition()
    {
        var h = new Host(); h.Command("Submit");
        Assert.Equal(1, h.Request[S.Step]); Assert.Equal(S.InApproval, FmWorkflowPolicy.Status(h.Request));
        h.Operation = Guid.NewGuid(); h.Command("Approve"); Assert.Equal(2, h.Request[S.Step]);
        h.Operation = Guid.NewGuid(); h.Command("Approve"); Assert.Equal(S.Approved, FmWorkflowPolicy.Status(h.Request));
        h.Operation = Guid.NewGuid(); h.Command("Close"); Assert.Equal(S.Closed, FmWorkflowPolicy.Status(h.Request));
        Assert.Equal(4, h.Created.Count(e => e.LogicalName == S.History));
    }
    [Fact] public void Retry_reuses_receipt_without_repeating_email()
    {
        var h = new Host(); h.Command("Submit", 0); var count = h.Created.Count;
        h.Command("Submit", 0); Assert.Equal(count, h.Created.Count); Assert.Equal(1, h.Output["Revision"]);
        Assert.Throws<InvalidPluginExecutionException>(() => h.Command("Reject", 0));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void Revision_or_rowversion_conflict_has_no_receipt(bool rowVersion)
    {
        var h = new Host { Conflict = rowVersion };
        Assert.Throws<InvalidPluginExecutionException>(() => h.Command("Submit", rowVersion ? 0 : 99)); Assert.Empty(h.Created);
    }
    [Fact] public void Failed_outbox_rolls_back_in_transaction_model()
    {
        var h = new Host { FailEmail = true }; Assert.Throws<InvalidPluginExecutionException>(() => h.Command("Submit"));
        Assert.Equal(S.Draft, FmWorkflowPolicy.Status(h.Request)); Assert.Empty(h.Created);
    }
    [Fact] public void Other_owner_cannot_submit_and_missing_transaction_is_rejected()
    {
        var h = new Host(); h.Request["ownerid"] = new EntityReference("systemuser", Guid.NewGuid());
        Assert.Throws<InvalidPluginExecutionException>(() => h.Command("Submit"));
        h.Transaction = false; Assert.Throws<InvalidPluginExecutionException>(() => h.Command("Submit")); Assert.Empty(h.Created);
    }
    [Fact] public void Decision_requires_assigned_user_and_comment()
    {
        var h = new Host(); h.Command("Submit"); h.Operation = Guid.NewGuid();
        Assert.Throws<InvalidPluginExecutionException>(() => h.Command("Approve", comment: ""));
        var steps = FmWorkflowPolicy.Deserialize(h.Request.GetAttributeValue<string>(S.Plan)); steps[0].UserId = Guid.NewGuid();
        h.Request[S.Plan] = FmWorkflowPolicy.Serialize(steps);
        Assert.Throws<InvalidPluginExecutionException>(() => h.Command("Approve"));
    }
    [Fact] public void Reminder_and_escalation_happen_once_per_step()
    {
        var h = new Host(); h.Command("Submit"); h.Message = S.DeadlineApi;
        h.Command("Deadline"); Assert.NotNull(h.Request[S.ReminderOn]); var count = h.Created.Count;
        h.Command("Deadline"); Assert.Equal(count, h.Created.Count);
        h.Request[S.Due] = DateTime.UtcNow.AddMinutes(-1);
        h.Command("Deadline"); Assert.NotNull(h.Request[S.EscalatedOn]); count = h.Created.Count;
        h.Command("Deadline"); Assert.Equal(count, h.Created.Count);
        h.Admin = false; Assert.Throws<InvalidPluginExecutionException>(() => h.Command("Deadline"));
    }
    [Fact] public void Native_update_cannot_bypass_approval_status()
    {
        var h = new Host(); h.Message = "Update"; h.Input["Target"] = new Entity(S.Request, h.Request.Id) { [S.Status] = new OptionSetValue(S.Approved) };
        Assert.Throws<InvalidPluginExecutionException>(() => new GuardFmWorkflow().Execute(h));
        h.Request[S.Status] = new OptionSetValue(S.InApproval); h.Input["Target"] = new Entity(S.Request, h.Request.Id) { [S.Name] = "Edit after submit" };
        Assert.Throws<InvalidPluginExecutionException>(() => new GuardFmWorkflow().Execute(h));
    }
    [Fact] public void Delayed_escalation_sends_overdue_once_before_reassignment()
    {
        var h = new Host(); h.Command("Submit"); h.Message = S.DeadlineApi;
        var steps = FmWorkflowPolicy.Deserialize(h.Request.GetAttributeValue<string>(S.Plan)); steps[0].EscalationHours = 4;
        h.Request[S.Plan] = FmWorkflowPolicy.Serialize(steps); h.Request[S.Due] = DateTime.UtcNow.AddHours(-1);
        h.Command("Deadline"); Assert.NotNull(h.Request[S.OverdueOn]); Assert.Null(h.Request.GetAttributeValue<DateTime?>(S.EscalatedOn));
        var count = h.Created.Count; h.Command("Deadline"); Assert.Equal(count, h.Created.Count);
        h.Request[S.Due] = DateTime.UtcNow.AddHours(-5); h.Command("Deadline"); Assert.NotNull(h.Request[S.EscalatedOn]);
        Assert.Contains(h.Created, e => e.LogicalName == S.History && e.GetAttributeValue<string>(S.Command) == "Overdue");
    }
    [Fact] public void Email_key_is_bounded_case_insensitive_and_recipient_specific()
    {
        var id = Guid.NewGuid(); Assert.Equal(FmWorkflowStore.CorrelationKey(id, "A@X.COM"), FmWorkflowStore.CorrelationKey(id, "a@x.com"));
        Assert.True(FmWorkflowStore.CorrelationKey(id, new string('a', 320)).Length < 200);
        Assert.NotEqual(FmWorkflowStore.CorrelationKey(id, "a@x.com"), FmWorkflowStore.CorrelationKey(id, "b@x.com"));
    }
    [Fact] public void Workflow_email_is_a_safe_bounded_html_card()
    {
        var request = new Entity(S.Request) { [S.Name] = "Repair <AHU> & Library", [S.Status] = new OptionSetValue(S.InApproval),
            [S.Step] = 2, [S.Approver] = "approver@example.com", [S.Due] = DateTime.UtcNow.AddHours(4) };
        var html = FmWorkflowStore.BuildEmailBody(request, "https://demo.example/app?id=1&x=2", "Approve", "Comment <script>alert(1)</script>\nSecond line");
        Assert.Contains("FM Request update", html); Assert.Contains("Open FM Request &amp; review", html);
        Assert.Contains("Repair &lt;AHU&gt; &amp; Library", html); Assert.DoesNotContain("<script>", html);
        Assert.Contains("Comment &lt;script&gt;alert(1)&lt;/script&gt;<br>Second line", html); Assert.True(html.Length < 10000);
    }
    [Theory] [InlineData(true, 30, true)] [InlineData(false, 30, false)] [InlineData(true, 40, false)]
    public void Internal_guard_requires_exact_request_and_synchronous_main_operation(bool sameRequest, int stage, bool expected)
    {
        var id = Guid.NewGuid(); var actor = Guid.NewGuid();
        var parent = WorkflowProxy.Make<IPluginExecutionContext>((m, _) => m.Name switch {
            "get_MessageName" => S.TransitionApi, "get_Stage" => stage, "get_IsInTransaction" => true, "get_InitiatingUserId" => actor,
            "get_InputParameters" => new ParameterCollection { ["RequestId"] = sameRequest ? id : Guid.NewGuid() },
            "get_ParentContext" => null, _ => null });
        var child = WorkflowProxy.Make<IPluginExecutionContext>((m, _) => m.Name switch {
            "get_MessageName" => "Update", "get_PrimaryEntityName" => S.Request, "get_PrimaryEntityId" => id,
            "get_IsInTransaction" => true, "get_InitiatingUserId" => actor, "get_ParentContext" => parent,
            "get_InputParameters" => new ParameterCollection { ["Target"] = new Entity(S.Request, id) }, _ => null });
        Assert.Equal(expected, GuardFmWorkflow.IsWorkflowWrite(child));
    }
}
