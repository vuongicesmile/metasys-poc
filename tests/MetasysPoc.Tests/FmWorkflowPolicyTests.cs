using FMCentralBms.Plugins;
using Microsoft.Xrm.Sdk;
using S = Dataverse.SyncWorker.DataAccess.Constants.DataverseSchema.Workflow;

namespace MetasysPoc.Tests;

public class FmWorkflowPolicyTests
{
    private static Entity Request() => new(S.Request) { [S.Name] = "Test", [S.Description] = "Test description", [S.Type] = new OptionSetValue(S.Ciwg), [S.Department] = "FM", [S.Value] = 100m };
    private static Entity Route(int step, decimal min = 0, string? dept = null) => new(S.Route, Guid.NewGuid()) {
        [S.StepNo] = step, [S.Active] = true, [S.Type] = new OptionSetValue(S.Ciwg), [S.MinValue] = min, [S.Department] = dept
    };
    [Fact] public void RoutesSelectSpecificThresholdThenNextStep()
    {
        var fallback = Route(1); var specific = Route(1, 50, "FM"); var excluded = Route(1, 200, "FM"); var next = Route(3);
        var result = FmWorkflowPolicy.SelectRoutes(Request(), new[] { next, fallback, specific, excluded });
        Assert.Equal(new[] { specific.Id, next.Id }, result.Select(r => r.Id));
    }
    [Fact] public void RoutesRejectAmbiguityInsteadOfArbitraryApprover()
        => Assert.Throws<InvalidPluginExecutionException>(() => FmWorkflowPolicy.SelectRoutes(Request(), new[] { Route(1), Route(1) }));
    [Fact] public void RoutesRejectMissingConfiguration()
        => Assert.Throws<InvalidPluginExecutionException>(() => FmWorkflowPolicy.SelectRoutes(Request(), new[] { Route(1, 1000) }));
    [Fact] public void DisabledOrOtherTypeRoutesAreExcluded()
    {
        var disabled = Route(1); disabled[S.Active] = false;
        var other = Route(1); other[S.Type] = new OptionSetValue(S.Risk);
        Assert.Throws<InvalidPluginExecutionException>(() => FmWorkflowPolicy.SelectRoutes(Request(), new[] { disabled, other }));
    }
    [Fact] public void RiskCannotSubmitWithoutMitigationReviewAndScores()
    {
        var r = Request(); r[S.Type] = new OptionSetValue(S.Risk);
        Assert.Throws<InvalidPluginExecutionException>(() => FmWorkflowPolicy.ValidateDraft(r, true));
        r[S.Likelihood] = 3; r[S.Impact] = 5; r[S.Mitigation] = "Inspect monthly";
        r[S.ReviewDate] = DateTime.UtcNow.AddDays(30); r["ownerid"] = new EntityReference("systemuser", Guid.NewGuid());
        FmWorkflowPolicy.ValidateDraft(r, true);
        r[S.Likelihood] = 6;
        Assert.Throws<InvalidPluginExecutionException>(() => FmWorkflowPolicy.ValidateDraft(r, true));
    }
    [Fact] public void SnapshotPreservesIdentityTimingAndEscapesXml()
    {
        var step = new FmApprovalStep { Number = 1, UserId = Guid.NewGuid(), Email = "a@example.test", EscalationUserId = Guid.NewGuid(), EscalationEmail = "b@example.test", Role = "FM & Operations", SlaDays = 3, ReminderHours = 12, EscalationHours = 4 };
        var actual = Assert.Single(FmWorkflowPolicy.Deserialize(FmWorkflowPolicy.Serialize(new[] { step })));
        Assert.Equal(step.UserId, actual.UserId); Assert.Equal(step.EscalationUserId, actual.EscalationUserId);
        Assert.Equal(step.Role, actual.Role); Assert.Equal(12, actual.ReminderHours); Assert.Equal(4, actual.EscalationHours);
    }
    [Theory]
    [InlineData(S.Draft,100000000)] [InlineData(S.InApproval,100000001)] [InlineData(S.Approved,100000002)] [InlineData(S.Rejected,100000003)] [InlineData(S.Closed,100000004)]
    public void LegacyConsumersReceiveCompatibleStatus(int status, int expected) => Assert.Equal(expected, FmWorkflowPolicy.Legacy(status));
}
