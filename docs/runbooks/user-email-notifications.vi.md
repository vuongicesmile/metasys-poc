# Runbook notification người dùng qua email

Run từ `D:\metasys-poc`. Target phải là:

```text
Organization: ab191700-b99e-f111-aaa0-000d3a80bb96
Environment:  5abcb0e5-99b2-e51f-aa0e-90d84405798b
Solution:     FMCentralBms
```

## 1. Cơ chế

```text
terminal fmc_syncrequest
  -> QueueSyncNotification plug-in
  -> Pending fmc_notification
  -> FMC - Send User Email Notification
  -> Office 365 Outlook SendEmailV2
  -> Sent/Failed receipt trong fmc_notification
```

Notification không chạy trong transaction SQL sync. Vì vậy lỗi mail không làm
request đã hoàn tất quay lại Failed.

## 2. Kiểm tra local

```powershell
dotnet build .\MetasysPoc.sln -c Release
py -m pytest .\scripts\tests\test_user_notification_flow.py -q
py -m py_compile .\scripts\provision-user-notification-flow.py
```

DLL cần đăng ký:

```text
Dataverse.Plugin\FMCentralBms.Plugins\bin\Release\net48\FMCentralBms.Plugins.dll
```

Version hiện tại: `1.0.0.5`.

## 3. Provision schema và plug-in

Đây là cloud mutation, chỉ chạy khi target `pac org who` đúng organization ở
đầu tài liệu.

```powershell
pac org who --environment https://org06cbc9ec.crm5.dynamics.com/
dotnet run --project .\DataverseSyncWorker -c Release --no-build -- --provision
dotnet run --project .\DataverseSyncWorker -c Release --no-build -- --register-plugin
```

Kết quả cần có:

- standard table `fmc_notification`;
- alternate key `fmc_notification_correlationkey`;
- async PostOperation step `BMS: Queue notification on Sync Request completion`;
- filtering attributes `fmc_status`;
- assembly version `1.0.0.5`.

## 4. Tạo Outlook connection reference

Trong solution `FMCentralBms`, tạo connection reference:

```text
Display name: FM Central Office 365 Outlook
Logical name: fmc_sharedoffice365outlook
Connector: Office 365 Outlook
Connection: tài khoản/service mailbox được phép gửi email
```

Không commit OAuth token hoặc credential. Production nên dùng mailbox/service
identity được quản trị phê duyệt, không phụ thuộc tài khoản cá nhân.

Kiểm tra connection:

```powershell
pac connection list --environment https://org06cbc9ec.crm5.dynamics.com/
py .\scripts\provision-user-notification-flow.py bootstrap-reference --outlook-connection-id <connection-id>
```

Nếu bỏ `--outlook-connection-id`, script dùng connection ID đã kiểm chứng của
Developer environment nêu ở đầu tài liệu. Khi triển khai sang environment khác,
luôn truyền ID của connection vừa tạo tại environment đích.

## 5. Deploy flow

```powershell
py .\scripts\provision-user-notification-flow.py render
py .\scripts\provision-user-notification-flow.py deploy
py .\scripts\provision-user-notification-flow.py verify
```

Flow phải active với tên `FMC - Send User Email Notification`.

## 6. Smoke test email

Lệnh sau tạo một outbox row test cho email của user Dataverse hiện tại và chờ
tối đa 55 giây để đọc receipt:

```powershell
py .\scripts\provision-user-notification-flow.py test
py .\scripts\provision-user-notification-flow.py test-producer
```

Pass khi output có:

```text
fmc_status = 789121001  # Sent
fmc_attempts = 1
fmc_sentat != null
fmc_flowrunid != null
```

Sau đó kiểm tra Inbox/Junk với subject:

```text
[FMC BMS] Notification smoke test
```

`test-producer` không chạy SQL ingestion/sync. Nó tạo một Sync Request cô lập,
chuyển record sang Succeeded để kiểm tra async plug-in -> outbox -> email.

## 7. Test bằng SQL sync thật

1. Mở worker:

   ```powershell
   .\START-SQL-TO-DATAVERSE.cmd
   ```

2. Trong `FMC BMS Demo`, bấm `Request BMS Sync`, hoặc chạy flow
   `FMC - Request SQL to Dataverse Sync`.
3. Chờ `fmc_syncrequest.fmc_status` sang Succeeded/CompletedWithIssues/Failed.
4. Kiểm tra `fmc_notification` có đúng một row với:
   - `Regarding Table = fmc_syncrequest`;
   - `Regarding ID` bằng request ID;
   - correlation key chứa request ID + terminal status;
   - receipt `Sent` hoặc lỗi cụ thể `Failed/Skipped`.

## 8. Xử lý lỗi

| Triệu chứng | Kiểm tra |
| --- | --- |
| Không có outbox row | Plug-in step active, Plugin Trace Log và quyền Create trên `fmc_notification` |
| Row `Skipped` | `fmc_requestedby`/`createdby` không resolve được `internalemailaddress` |
| Row mãi `Pending` | Flow/callback inactive hoặc connection reference invalid |
| Row `Failed` | Mở run theo `fmc_flowrunid`; kiểm tra Outlook connection/mailbox policy |
| Flow không activate | Owner của flow phải có quyền dùng cả Dataverse và Outlook connection references |
| Có email nhưng receipt update lỗi | Kiểm tra quyền Write `fmc_notification`; không gửi test lại mù quáng để tránh duplicate |

## 9. Export source solution

Export vào đường dẫn tạm trước, so sánh rồi mới merge vì `dataverse/FMCentralBms`
có thể chứa thay đổi local khác:

```powershell
pac solution export --name FMCentralBms --path .\.artifacts\notification-solution\FMCentralBms.zip --managed false --environment https://org06cbc9ec.crm5.dynamics.com/
pac solution unpack --zipfile .\.artifacts\notification-solution\FMCentralBms.zip --folder .\.artifacts\notification-solution\unpacked --packagetype Unmanaged
```
