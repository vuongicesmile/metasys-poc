# Vận hành Power Automate trigger → SQL-to-Dataverse

Ngày cập nhật: 2026-09-08. Phạm vi: Developer environment
`5abcb0e5-99b2-e51f-aa0e-90d84405798b`.

## Thành phần đã triển khai

- Flow: `FMC - Request SQL to Dataverse Sync` — Activated.
- Queue: `fmc_syncrequest` trong solution `FMCentralBms`.
- Worker: `DataverseSyncWorker`, `ExecutionMode=CommandDriven`.
- Pipeline: `ab191700-b99e-f111-aaa0-000d3a80bb96:FMC`, lấy từ environment variable `fmc_SyncPipeline`.

Power Automate tạo hoặc dùng lại một request `Queued/Running`; worker mới đọc SQL,
upsert hai bảng BMS và ghi delivery receipt. Flow không đăng nhập SQL và không
chạy executable từ cloud.

## Chạy pilot trên máy developer

1. Double-click [START-SQL-TO-DATAVERSE.cmd](../../START-SQL-TO-DATAVERSE.cmd).
2. Giữ cửa sổ worker mở; trạng thái chờ bình thường là `CommandIdle`.
3. Vào Power Automate, chọn đúng environment, mở solution `FMCentralBms`.
4. Chạy flow `FMC - Request SQL to Dataverse Sync`.
5. Mở view `Queued or Running Sync Requests` hoặc `Completed Sync Requests` để theo dõi.

Kết quả hợp lệ:

- `Succeeded`: hết pending và dead-letter trong cutoff.
- `CompletedWithIssues`: dòng hợp lệ đã xử lý nhưng còn dead-letter.
- `Failed`: kiểm tra worker log, SQL, auth, schema và permissions; không reset ledger.
- `Queued` lâu: worker/service không chạy hoặc không kết nối được Dataverse.
- `Running` hết heartbeat: worker khác sẽ reclaim sau khi lease hết hạn.

Mỗi request chụp `MAX(id)` một lần vào `fmc_requestedcutoffid`. Dữ liệu SQL mới
hơn cutoff chờ request kế tiếp. Delivery ledger vẫn là nguồn xác định pending;
không dùng cutoff như high watermark để bỏ qua transaction commit muộn.

## Kiểm tra và chẩn đoán

Kiểm tra chỉ đọc:

```powershell
.\scripts\Start-DataverseSync.ps1 -Mode Verify
```

Enqueue/process trực tiếp chỉ dành cho chẩn đoán có kiểm soát; cả hai đều ghi dữ liệu:

```powershell
dotnet run --project .\DataverseSyncWorker -c Release -- --enqueue
dotnet run --project .\DataverseSyncWorker -c Release -- --process-command-once
```

`--enqueue` coalesce request đang hoạt động. `--process-command-once` nhận tối đa
một request nhưng request đó có thể xử lý nhiều batch đến cutoff.

## Cài Windows Service production

Không dùng phiên Azure CLI cá nhân cho service. Cần Entra application, Dataverse
application user, certificate có private key trong `LocalMachine\My`, role
`FM Central BMS Integration`, và quyền SQL cho service account.

Sau khi các prerequisite đã được quản trị viên chuẩn bị:

```powershell
.\scripts\Install-DataverseSyncService.ps1 `
  -ClientId <application-id> `
  -CertificateThumbprint <thumbprint> `
  -ServiceAccount 'NT AUTHORITY\NetworkService'
```

Installer publish Release vào `C:\ProgramData\FMCentralBms\DataverseSyncWorker`,
ghi cấu hình production không chứa secret, đăng ký Automatic startup và restart
recovery. Mặc định installer không start service. Trước khi start, cấp đúng quyền
SQL và read private key cho service account; sau đó:

```powershell
Start-Service FMCentralDataverseSync
Get-Service FMCentralDataverseSync
```

Nếu chưa có application identity/certificate thì tiếp tục dùng launcher pilot;
không cài service bằng developer token.

## Tài liệu liên quan

- [Plan và implementation receipt](../plans/power-automate-sql-to-dataverse.vi.md)
- [Chi tiết SQL delivery contract](sql-to-dataverse-runbook.vi.md)
- [Dataverse deployment](../reference/dataverse-deployment.md)
