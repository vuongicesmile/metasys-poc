using DataverseSyncWorker.Abstractions;
using DataverseSyncWorker.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataverseSyncWorker.Services;

public sealed class SyncWorker(ISyncEngine engine, ICommandProcessor commands, SyncOptions options, RuntimeState status,
    IIntegrationFailureClassifier failures, ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Disabled là trạng thái hợp lệ: host vẫn khởi động được nhưng không đọc/ghi dữ liệu.
        if (!options.Enabled) { status.Set(new("Disabled")); return; }
        if (!options.HasCredentials)
        {
            // Không retry khi chưa có credentials vì retry không thể tự sửa cấu hình.
            status.Set(new("AwaitingCredentials", Error: "Configure the Dataverse application identity, then restart."));
            return;
        }
        if (options.ExecutionMode == "CommandDriven")
        {
            // Production flow queue fmc_syncrequest; worker chỉ claim request được flow tạo.
            await RunCommandDriven(ct);
            return;
        }
        // Continuous mode phù hợp local/simple deployment: mỗi vòng đọc một batch pending.
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // SyncEngine tự serialize local process và SQL application lock giữa các process.
                var result = await engine.Run(ct);
                status.Set(new(result.Busy ? "Busy" : result.Read == 0 ? "Idle" : "Syncing", DateTime.UtcNow, result));
                if (result.Read == options.BatchSize) continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is InvalidOperationException || failures.IsPermanent(ex))
            {
                // Lỗi schema, quyền hoặc authentication permanent phải dừng để người vận hành sửa.
                status.Set(new("Blocked", DateTime.UtcNow, Error: "Configuration, authentication, schema or permanent Dataverse error. Correct the cause and restart; SQL delivery remains pending."));
                logger.LogWarning("Sync requires attention ({ErrorType}). No automatic retry for permanent failures", ex.GetType().Name);
                return;
            }
            catch (Exception ex)
            {
                // Không đưa nội dung exception vào API public vì lỗi upstream có thể chứa thông tin request/credential.
                status.Set(new("Failed", DateTime.UtcNow, Error: "Synchronization failed; delivery remains pending. Check schema, SQL and application permissions."));
                logger.LogWarning("Sync failed ({ErrorType}); delivery remains pending", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(options.PollIntervalSeconds), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        }
    }

    private async Task RunCommandDriven(CancellationToken ct)
    {
        // CommandIdle nghĩa là worker đang sống nhưng chưa có request cần claim.
        status.Set(new("CommandIdle"));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // TryRun xử lý tối đa một request hoặc trả Idle nếu queue đang rỗng.
                var result = await commands.TryRun(ct);
                status.Set(new(result.State == "Idle" ? "CommandIdle" : result.State,
                    DateTime.UtcNow, RequestId: result.RequestId));
                if (result.Found && result.State is not ("Requeued" or "RequeuedBusy")) continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                status.Set(new("CommandFailed", DateTime.UtcNow,
                    Error: "Command polling failed. Check Dataverse connectivity and worker logs."));
                logger.LogWarning("Command polling failed ({ErrorType})", ex.GetType().Name);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(options.CommandPollIntervalSeconds), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        }
    }
}
