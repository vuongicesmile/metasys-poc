using DataverseSyncWorker.Abstractions;
using DataverseSyncWorker.Models;
using DataverseSyncWorker.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MetasysPoc.Tests;

public sealed class CommandProcessorTests
{
    [Fact]
    public async Task Idle_does_not_read_sql_or_run_a_batch()
    {
        var requests = new Requests { Request = null };
        var ledger = new Ledger();
        var engine = new Engine();
        var result = await Create(requests, ledger, engine).TryRun(default);
        Assert.False(result.Found);
        Assert.Equal("Idle", result.State);
        Assert.Equal(0, ledger.Reads);
        Assert.Null(engine.Cutoff);
    }

    [Theory]
    [InlineData(0, "Succeeded", SyncRequestStatuses.Succeeded)]
    [InlineData(2, "CompletedWithIssues", SyncRequestStatuses.CompletedWithIssues)]
    public async Task New_request_captures_cutoff_and_baseline_before_completion(
        long deadLetters, string state, int status)
    {
        var requests = new Requests { Request = NewRequest(null) };
        var ledger = new Ledger { Current = new(10, 10 - deadLetters, deadLetters, deadLetters, 0) };
        var result = await Create(requests, ledger, new()).TryRun(default);
        Assert.Equal(state, result.State);
        Assert.Equal(100, requests.Request!.CutoffId);
        Assert.Equal(ledger.Current.DeliveredRows, requests.Request.BaselineDelivered);
        Assert.Equal(status, requests.Completion!.Status);
        Assert.Equal(0, requests.Completion.Delivered);
        Assert.Equal(0, requests.Completion.Quarantined);
    }

    [Fact]
    public async Task Busy_engine_requeues_without_completing()
    {
        var requests = new Requests();
        var engine = new Engine { RunBatch = _ => Task.FromResult(new BatchResult(0, 0, 0, Busy: true)) };
        var result = await Create(requests, new(), engine).TryRun(default);
        Assert.Equal("RequeuedBusy", result.State);
        Assert.True(requests.Requeued);
        Assert.Null(requests.Completion);
        Assert.Equal(100, engine.Cutoff);
    }

    [Fact]
    public async Task Completion_uses_ledger_receipts_instead_of_batch_counters()
    {
        var requests = new Requests { Request = NewRequest(100) with { BaselineDelivered = 2 } };
        var ledger = new Ledger();
        var engine = new Engine { RunBatch = _ => {
            ledger.Current = new(10, 10, 0, 0, 0);
            return Task.FromResult(new BatchResult(8, 999, 999));
        }};
        var result = await Create(requests, ledger, engine).TryRun(default);
        Assert.Equal("Succeeded", result.State);
        Assert.Equal(8, requests.Completion!.Delivered);
        Assert.Equal(0, requests.Completion.Quarantined);
        Assert.Equal(1, requests.Completion.Batches);
        Assert.Equal(1, requests.ProgressCalls);
    }

    [Fact]
    public async Task Failure_reports_authoritative_partial_receipts_and_sanitized_error()
    {
        var requests = new Requests();
        var ledger = new Ledger();
        var engine = new Engine { RunBatch = _ => {
            ledger.Current = new(10, 4, 6, 1, 5);
            throw new InvalidOperationException("credential=secret");
        }};
        var result = await Create(requests, ledger, engine).TryRun(default);
        Assert.Equal("Failed", result.State);
        Assert.Equal(SyncRequestStatuses.Failed, requests.Completion!.Status);
        Assert.Equal(4, requests.Completion.Delivered);
        Assert.Equal(1, requests.Completion.Quarantined);
        Assert.DoesNotContain("secret", requests.Completion.Error!);
    }

    [Fact]
    public async Task Host_cancellation_does_not_mark_request_failed()
    {
        using var cancellation = new CancellationTokenSource();
        var requests = new Requests();
        var engine = new Engine { RunBatch = ct => {
            cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new BatchResult(0, 0, 0));
        }};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(requests, new(), engine).TryRun(cancellation.Token));
        Assert.Null(requests.Completion);
        Assert.False(requests.Requeued);
    }

    private static CommandProcessor Create(Requests requests, Ledger ledger, Engine engine) =>
        new(requests, ledger, engine, new SyncOptions(), NullLogger<CommandProcessor>.Instance);

    private static SyncRequest NewRequest(long? cutoff) => new(Guid.NewGuid(), "test", cutoff, 0, 0, 0, "1");
    private sealed record Completion(int Status, int Batches, long Delivered, long Quarantined, string? Error);

    private sealed class Requests : ISyncRequestStore
    {
        public SyncRequest? Request = NewRequest(100);
        public Completion? Completion;
        public bool Requeued;
        public int ProgressCalls;
        public Task<SyncRequest?> Claim(string owner, CancellationToken ct) => Task.FromResult(Request);
        public Task<SyncRequest> Initialize(SyncRequest request, string owner, long cutoff,
            CutoffSummary baseline, CancellationToken ct) => Task.FromResult(Request = request with {
                CutoffId = cutoff, BaselineDelivered = baseline.DeliveredRows, BaselineDeadLetters = baseline.DeadLetterRows });
        public Task Progress(SyncRequest request, string owner, int batches, long delivered,
            long quarantined, CutoffSummary summary, CancellationToken ct)
        { ProgressCalls++; return Task.CompletedTask; }
        public Task Complete(SyncRequest request, string owner, int status, int batches, long delivered,
            long quarantined, CutoffSummary summary, string? error, CancellationToken ct)
        { Completion = new(status, batches, delivered, quarantined, error); return Task.CompletedTask; }
        public Task Requeue(SyncRequest request, string owner, int batches, long delivered,
            long quarantined, CutoffSummary summary, CancellationToken ct)
        { Requeued = true; return Task.CompletedTask; }
    }

    private sealed class Ledger : ISyncLedger
    {
        public CutoffSummary Current = new(10, 0, 10, 0, 10);
        public int Reads;
        public Task<long> MaxId(CancellationToken ct) { Reads++; return Task.FromResult(100L); }
        public Task<CutoffSummary> Summary(long cutoffId, CancellationToken ct)
        { Reads++; return Task.FromResult(Current); }
    }

    private sealed class Engine : ISyncEngine
    {
        public long? Cutoff;
        public Func<CancellationToken, Task<BatchResult>> RunBatch = _ => throw new InvalidOperationException("Unexpected batch");
        public Task<BatchResult> Run(CancellationToken ct, long? cutoffId = null)
        { Cutoff = cutoffId; return RunBatch(ct); }
    }
}
