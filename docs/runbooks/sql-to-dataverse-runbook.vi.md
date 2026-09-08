# Quy trình SQL → Dataverse

## Kích hoạt cho các lần sau

Sau khi hoàn thành thiết lập lần đầu, mở
[START-SQL-TO-DATAVERSE.cmd](../../START-SQL-TO-DATAVERSE.cmd) bằng double-click.

Hoặc chạy từ PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\metasys-poc\scripts\Start-DataverseSync.ps1 -Mode Run
```

Worker chạy ở `CommandDriven`, chờ request từ Power Automate rồi tự đọc toàn bộ
dữ liệu SQL chưa xử lý đến cutoff, gửi sang Dataverse và ghi nhận kết quả. Không
cần gửi POST run-once hay nhờ Codex xử lý từng batch.

Giữ cửa sổ chạy mở và máy hoạt động. Nhấn Ctrl+C để dừng. Sau khi đóng chương
trình, đăng xuất hoặc khởi động lại máy, kích hoạt lại bằng cùng file/lệnh.
Pilot hiện chạy theo launcher. Code và installer Windows Service đã có, nhưng
chưa đăng ký service vì application identity/certificate runtime chưa sẵn sàng.

## 1. Xác nhận cấu hình — làm khi thiết lập hoặc đổi môi trường

Cấu hình ở [appsettings.json](../../DataverseSyncWorker/appsettings.json).

| Thành phần | Cấu hình POC hiện tại |
| --- | --- |
| SQL Server / database | localhost / FM_Central |
| Bảng nguồn | raw.bms_reading |
| Dataverse | https://org06cbc9ec.crm5.dynamics.com/ |
| Organization ID cần khớp | ab191700-b99e-f111-aaa0-000d3a80bb96 |
| Solution / prefix | FMCentralBms / fmc |
| Trạng thái mới nhất | fmc_bmspoint, standard table |
| Lịch sử gần đây | fmc_bmsreading, elastic table |
| Execution mode | CommandDriven |
| Chu kỳ poll request | 10 giây |
| Batch tối đa | 100 dòng |
| History TTL mặc định | 30 ngày tính từ thời điểm reading |
| SourceId | FMC |

Các giá trị có thể được override bởi cấu hình môi trường hoặc tham số worker.
Đừng đổi SourceId, GUID mapping, partition hay ledger để chạy lại dữ liệu.

## 2. Chuẩn bị SQL và Dataverse — làm một lần

Trên máy chạy cần .NET SDK theo target của project (hiện net10.0), SQL Server
truy cập được và quyền của Windows user hiện tại đối với FM_Central.

Kiểm tra SDK và các bảng SQL:

```powershell
dotnet --version
sqlcmd -S localhost -d FM_Central -E -C -b -Q "SELECT SCHEMA_NAME(schema_id) AS schema_name, name FROM sys.tables WHERE name IN ('bms_reading','dataverse_delivery','dataverse_sync_state','dataverse_dead_letter');"
```

Nếu đang thiết lập database mới, chạy các script theo thứ tự:

```powershell
sqlcmd -S localhost -E -C -b -i D:\metasys-poc\sql\create-bms-tables.sql
sqlcmd -S localhost -E -C -b -i D:\metasys-poc\sql\create-dataverse-sync-tables.sql
```

Các script tạo cấu trúc SQL và có tác động ghi. Với database đang vận hành,
kiểm tra đúng đối tượng trước khi chạy; không xóa hay reset dữ liệu hiện có.

Dataverse cần có solution, hai bảng BMS, bảng request, các cột và role theo
[hướng dẫn deployment](../reference/dataverse-deployment.md). POC đã có bản ghi nhận provisioning
trước đây; kiểm tra thực tế nếu thiết lập lại hoặc đổi environment.
Key fmc_bmspoint_objectid phải Active.

Nếu thực sự cần provisioning, dùng danh tính có quyền triển khai và lệnh
`Start-DataverseSync.ps1 -Mode Provision` theo hướng dẫn deployment.
Provision thay đổi schema/role; không chạy mỗi lần kích hoạt đồng bộ.

## 3. Chuẩn bị đăng nhập — thiết lập lần đầu, làm lại khi hết phiên

### POC trên máy hiện tại

Không truyền ClientId để tái sử dụng cấu hình Developer. Helper trong repo lấy
access token qua Azure CLI bundle ở rmit-fm-data; worker tự lấy lại token khi cần.

Đường dẫn WSL, Windows Python của bundle và phiên Azure CLI của đúng Windows
user phải còn dùng được. Kiểm tra khả năng lấy token mà không in token:

```powershell
Set-Location D:\metasys-poc
$syncConfig = Get-Content -LiteralPath .\DataverseSyncWorker\appsettings.json -Raw | ConvertFrom-Json
& $syncConfig.Dataverse.DeveloperTokenPython $syncConfig.Dataverse.DeveloperTokenScript --probe
```

TOKEN_OK xác nhận lấy được token; bước 4 mới kiểm tra truy cập SQL/Dataverse.
Nếu phiên Azure CLI hết hạn, đăng nhập lại bằng Azure CLI của bundle với đúng
tenant/tài khoản. Không dán token vào appsettings hoặc Git.

Phiên đăng nhập cá nhân có thể hết hạn hoặc cần xác thực lại. Vì vậy cách POC
này không bảo đảm chạy 24/7 hoàn toàn không cần người thao tác.

### Runtime vận hành lâu dài

Chuẩn bị Entra application, Dataverse application user và certificate có private
key trong certificate store My của tài khoản chạy worker. Gán role
FM Central BMS Integration với quyền runtime phù hợp.

Khi đã có các giá trị thực, chạy:

```powershell
.\scripts\Start-DataverseSync.ps1 -Mode Run -ClientId <application-id> -CertificateThumbprint <thumbprint>
```

Launcher tắt đường đăng nhập Developer bằng override cấu hình khi ClientId được
truyền rõ ràng. Dùng certificate không hỏi client secret. Nếu chỉ truyền ClientId,
launcher hỏi secret ở hidden prompt; cách đó không phù hợp cho tác vụ tự chạy.

Nếu gọi worker trực tiếp thay vì launcher, phải tự chọn đúng cấu hình auth; xem
[DataverseConnection](../../DataverseSyncWorker/Services/DataverseConnection.cs).
Code đã hỗ trợ Windows Service và có installer. Để chạy sau khởi động hoặc khi
chưa đăng nhập, cần application identity/certificate và service account có quyền
SQL/private key theo [Power Automate runbook](power-automate-sql-sync.vi.md).

## 4. Kiểm tra trước lần chạy đầu

```powershell
.\scripts\Start-DataverseSync.ps1 -Mode Verify
```

Lệnh đọc SQL và Dataverse, không bắt đầu vòng lặp đồng bộ. Worker kiểm tra
Organization ID; nếu khác cấu hình thì từ chối sử dụng connection.

Kết quả gồm số dòng nguồn, đã giao, đang chờ, dead-letter và tối đa 25 mẫu lịch sử
còn trong thời gian lưu giữ. PendingRows lớn hơn 0 có nghĩa còn dữ liệu cần xử lý,
không nhất thiết là lỗi kết nối. Mẫu kiểm tra thành công không chứng minh mọi
dòng, current point, capacity hoặc quyền ghi đều đã được kiểm thử.

## 5. Kích hoạt đồng bộ và để worker chạy

Double-click START-SQL-TO-DATAVERSE.cmd hoặc chạy lệnh Mode Run ở đầu tài liệu,
sau đó chạy flow `FMC - Request SQL to Dataverse Sync` theo
[Power Automate runbook](power-automate-sql-sync.vi.md).

Sau mỗi lần kích hoạt, worker thực hiện:

1. Mở SQL và lấy lock cho pipeline.
2. Chọn các dòng còn thiếu delivery receipt, loại riêng các dead-letter chưa xử lý.
3. Validate giá trị và timestamp; ghi nhận riêng các dòng không hợp lệ.
4. Upsert trạng thái mới nhất của từng point.
5. Upsert lịch sử còn trong TTL bằng GUID và partition ổn định.
6. Ghi receipt khi cả point và history được yêu cầu đã thành công.
7. Tiếp tục batch kế tiếp đến cutoff, cập nhật heartbeat/progress và kết thúc request.

Dữ liệu đã giao không bị tạo bản sao chỉ vì mở lại worker. Dòng history quá tuổi
TTL được bỏ qua có chủ đích, còn SQL vẫn giữ lịch sử đầy đủ.

Nguồn phát sinh SQL hoạt động độc lập. Nếu cần dữ liệu giả mới cho demo, chạy
FakeMetasysApi và BmsIngestionApp theo [README](../../README.md). File kích hoạt này
chỉ vận hành đoạn SQL → Dataverse, không tạo thêm dữ liệu giả.

## 6. Kiểm tra kết quả và xử lý tình huống

Mở http://localhost:5300/swagger, dùng GET /api/dataverse-sync/status khi cần
xem tình trạng. Đây là kiểm tra tùy chọn; không cần gọi API để kích hoạt từng batch.

| Trạng thái | Ý nghĩa / thao tác |
| --- | --- |
| Succeeded | Request đã xử lý hết phạm vi cutoff, không có dead-letter |
| CompletedWithIssues | Đã xử lý dòng hợp lệ nhưng còn dead-letter trong cutoff |
| CommandIdle | Worker đang chạy và chờ request Queued |
| Busy | Một worker khác đang giữ pipeline lock; tránh mở nhiều cửa sổ chạy |
| Failed | Lần xử lý bị lỗi; worker sẽ thử lại theo chu kỳ |
| Blocked | Lỗi cấu hình/auth/schema/quyền; sửa nguyên nhân rồi khởi động lại |
| AwaitingCredentials | Chưa đủ cấu hình đăng nhập; bổ sung rồi khởi động lại |
| Disabled | Enabled=false; vòng lặp đồng bộ không chạy |

Theo dõi pendingRows và deadLetterRows. Idle vẫn có thể đi kèm dead-letter:
những dòng đó đang chờ xử lý nguyên nhân, không được tự bỏ qua để báo thành công.
Khi nguồn tiếp tục ghi, pendingRows có thể tăng/giảm giữa các lần quan sát.

Sau một đợt nạp, chạy Mode Verify để đối soát các mẫu thực tế. Để sửa dead-letter,
kiểm tra nguyên nhân, sửa theo quy trình dữ liệu rồi release/replay đúng dòng;
không reset toàn bộ ledger.

Xóa một dòng đã giao ở Dataverse không làm worker tự gửi lại dòng đó. Thay đổi
raw SQL sau khi đã giao cũng không thuộc cơ chế incremental hiện tại; cần resync
có phạm vi rõ ràng.

## Chọn đúng chế độ

| Mode | Công dụng |
| --- | --- |
| Run | Bật command worker, chờ request Power Automate đến khi dừng |
| Once | Xử lý đúng một batch, tối đa 100 dòng theo cấu hình hiện tại |
| Verify | Đọc trạng thái/đối soát mẫu, không đồng bộ |
| Provision | Tạo/cập nhật cấu trúc Dataverse và role khi thiết lập |

Để sử dụng hằng ngày, bật Run rồi trigger flow. Once bỏ qua request queue và chỉ
xử lý một batch, nên không phải đường vận hành Power Automate chuẩn.

## Kiểm tra launcher khi thay đổi script

```powershell
powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\scripts\tests\Test-DataverseLauncher.ps1
```

Bộ kiểm tra thay thế lệnh dotnet bằng mock để kiểm tra chọn mode, chọn danh tính
và khôi phục biến môi trường khi thành công/thất bại. Không gọi SQL hay Dataverse;
kiểm tra live vẫn dùng Mode Verify và theo dõi một lần chạy thực tế.
