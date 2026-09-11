# Test nút Custom API và Power Automate bằng UI

Runbook này dành cho tính năng đã deploy trên Developer environment ngày
2026-09-11. Nó không start `DataverseSyncWorker` và không tự đọc SQL.

## 1. Mở app

Mở trực tiếp:

```text
https://org06cbc9ec.crm5.dynamics.com/main.aspx?appid=d19f4897-d227-4df3-8361-988f97c53e89
```

Trong navigation, chọn **BMS Catalog → Power Automate Demo**.

Nếu chưa thấy menu mới:

1. Nhấn `Ctrl+F5` để tải lại app metadata.
2. Kiểm tra đang ở app **FMC BMS Demo**, không phải màn hình Tables trong maker.
3. Đợi một phút sau publish rồi mở lại link trực tiếp.

## 2. Bấm thử

1. Nhấn **Request BMS Sync**.
2. Không cần chọn table, record hay nhập `Reason`.
3. Chờ khung trạng thái chuyển sang màu xanh.
4. Ghi lại `Request ID`, `Tạo mới`, `Status` và `Mã lần bấm`.

Kết quả bình thường:

```text
Dataverse đã nhận yêu cầu.
Request ID: <guid>
Tạo mới: true hoặc false
Status: 789100000
```

`Tạo mới=false` không phải lỗi. Nó có nghĩa pipeline đang có một request Queued
hoặc Running và Custom API đã reuse request đó để tránh sync trùng.

## 3. Kiểm tra Power Automate

Nhấn **Xem Power Automate** trên trang demo, sau đó:

1. Mở **Run history**.
2. Chọn run mới nhất đúng thời điểm vừa bấm.
3. Cả trigger và action **Demo_received** phải có dấu xanh `Succeeded`.
4. Mở `Demo_received` để xem payload event và message demo.

Flow cần kiểm tra:

```text
FMC - App Request BMS Sync Event
```

Flow chạy thành công chỉ chứng minh:

```text
UI -> Custom API -> plug-in -> queue -> business event -> Power Automate
```

Nó chưa chứng minh dữ liệu SQL đã sync. Muốn request được xử lý, phải chạy
`DataverseSyncWorker` theo runbook SQL-to-Dataverse.

## 4. Kiểm tra request trong Dataverse UI

Nếu app chưa có page cho Sync Request, vào maker portal:

1. Chọn Developer environment.
2. Mở solution `FMCentralBms`.
3. Mở table `fmc_syncrequest` → **Edit data**.
4. Tìm row theo `Request ID` trên trang demo.

Khi worker chưa chạy, mong đợi:

| Column | Giá trị |
| --- | --- |
| `fmc_status` | Queued (`789100000`) |
| `fmc_command` | `DrainPending` |
| `fmc_pipeline` | `ab191700-b99e-f111-aaa0-000d3a80bb96:FMC` |
| `fmc_activekey` | giống `fmc_pipeline` |
| `fmc_correlationid` | mã lần bấm nếu request vừa được tạo |

Không sửa `Status`, `Active Pipeline Key`, lease hoặc worker owner bằng tay.

## 5. Test bằng script dành cho developer

Chạy từ `D:\metasys-poc`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-BmsEventDemo.ps1 -Mode Verify

powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-BmsEventDemo.ps1 -Mode Test
```

`Verify` chỉ đọc live configuration. `Test` gọi Custom API hai lần với cùng một
`ClientRequestId`; nó phải trả cùng `RequestId`, lần retry có `Created=false`.

`Test` có side effect: có thể tạo một queued `fmc_syncrequest` và hai flow runs.

## 6. Lỗi thường gặp

### Không thấy trang Power Automate Demo

- Mở đúng app URL ở mục 1 và hard refresh.
- Chạy `Invoke-BmsEventDemo.ps1 -Mode Verify`.
- Nếu verify báo thiếu component, chạy `-Mode Deploy` bằng đúng Developer login.

### Nút trả HTTP 403

User thiếu quyền execute/create tương ứng với `prvCreatefmc_syncrequest`. Gán role
requestor phù hợp; không đổi Custom API sang chạy SYSTEM để né security.

### Nút trả `BMS-SYNC-001`

`ClientRequestId` không phải GUID hợp lệ. Trang chuẩn tự sinh GUID; lỗi này thường
chỉ xảy ra khi caller tùy chỉnh gửi payload sai.

### Nút xanh nhưng worker không xử lý

Đây là bình thường nếu worker đang tắt. Custom API chỉ enqueue. Dùng
[Power Automate trigger → SQL sync](power-automate-sql-sync.vi.md) để start và
theo dõi worker có kiểm soát.

### Flow không có run mới

1. Kiểm tra flow đang `On`.
2. Kiểm tra trigger chọn catalog `FMC BMS Demo`, category `App Events`, table
   `(none)`, action `fmc_RequestBmsSync`.
3. Không đổi Custom API khỏi `Async Only` và không xóa catalog assignment.
4. Kiểm tra callback registration trước khi recreate flow.
