# Local SPO ingestion runbook

## Mục đích

Đây là đường chạy demo khi chưa có Azure subscription. Local CLI đọc bản copy
các file đã lấy từ SharePoint, dùng cùng parser/mapper/writer của logic cloud và
ghi trực tiếp vào các Bronze table hiện có trong Dataverse.

Không dùng flow archive cũ `FMC - Copy SPO Demo File (Manual)`. Flow cũ chỉ ghi
binary vào `fmc_spofile`; local runner này ghi dữ liệu nghiệp vụ vào:

- `building.xlsx` -> `fmc_bmsbuilding`
- `electric_meter.xlsx` -> `fmc_bmsequipment` với Equipment Type `Electric Meter`
- `water_meter.xlsx` -> `fmc_bmsequipment`
- `electricity_reading_hourly.xlsx` -> `fmc_bmspoint` + `fmc_bmsreading`
- `water_reading_hourly.xlsx` -> `fmc_bmspoint` + `fmc_bmsreading`

## Điều kiện

- Đã đăng nhập developer Dataverse token theo cấu hình hiện có của
  `DataverseSyncWorker`.
- File mẫu nằm trong thư mục `data/`.
- Organization trong `Dataverse.SyncWorker/Dataverse.SyncWorker.App/appsettings.json` là environment dev
  đã được kiểm tra.

## Chạy

Từ repository root:

```powershell
dotnet build .\MetasysPoc.sln -c Release

dotnet run --project .\SPO.Ingestion\SPO.Ingestion.Cli -c Release --no-build -- `
  ingest-local --root .\data --config .\config\spo-ingestion.json `
  --source spo-building --utc-now 2026-09-14T00:00:00Z
```

`--source` là tùy chọn. Bỏ `--source` để chạy tất cả source enabled; runner tự
xếp thứ tự Building -> Equipment/ElectricMeter/WaterMeter -> readings để lookup parent tồn tại
trước. Receipt đầy đủ được lưu dưới `.artifacts/spo-local/`.

Ví dụ chạy từng bước:

```powershell
dotnet run --project .\SPO.Ingestion\SPO.Ingestion.Cli -c Release --no-build -- `
  ingest-local --root .\data --config .\config\spo-ingestion.json --source spo-building

dotnet run --project .\SPO.Ingestion\SPO.Ingestion.Cli -c Release --no-build -- `
  ingest-local --root .\data --config .\config\spo-ingestion.json --source spo-water-meter

dotnet run --project .\SPO.Ingestion\SPO.Ingestion.Cli -c Release --no-build -- `
  ingest-local --root .\data --config .\config\spo-ingestion.json --source spo-electric-meter

dotnet run --project .\SPO.Ingestion\SPO.Ingestion.Cli -c Release --no-build -- `
  ingest-local --root .\data --config .\config\spo-ingestion.json `
  --source spo-electricity-reading --current-only

dotnet run --project .\SPO.Ingestion\SPO.Ingestion.Cli -c Release --no-build -- `
  ingest-local --root .\data --config .\config\spo-ingestion.json --source spo-water-reading
```

## Cơ chế an toàn

1. Resolve source bằng `pathPrefix` + extension, không dùng filename để chọn
   mapping.
2. Parse và validate toàn file trước; lỗi Choice/required/ownership thì không
   ghi partial file.
3. Building/Equipment dùng business key và upsert idempotent.
4. Reading chỉ ghi event mới nhất vào Point; history dùng GUID/partition ổn định
   và `UpsertMultiple` theo batch.
5. Chạy lại cùng file không tạo duplicate; receipt phân biệt Delivered,
   SkippedOlderCurrent và duplicate.

## Chuẩn dữ liệu điện

- Mỗi row `electric_meter.xlsx` tạo một `fmc_bmsequipment` với Choice
  `ElectricMeter=789100003` và lookup về Building.
- Mỗi meter trong `electricity_reading_hourly.xlsx` chỉ tạo hai Point nghiệp vụ:
  `Energy` (`kWh`) và `Demand` (`kW`). `voltage_v` và `power_factor` vẫn có thể
  tồn tại trong file nguồn nhưng không được materialize thành Point.
- Object ID ổn định là `<electric_meter_id>/energy_kwh` và
  `<electric_meter_id>/demand_kw`. Cả hai Point bắt buộc lookup về Electric Meter.
- Phải ingest `spo-electric-meter` trước `spo-electricity-reading`; nếu thiếu meter,
  writer trả `WaitingDependency` thay vì tạo Point mồ côi.
- `--current-only` chỉ materialize trạng thái mới nhất của từng Point và không replay
  `fmc_bmsreading`. Dùng mode này khi cần dựng lại current state sau cleanup; bỏ flag
  khi cần ingest đầy đủ history theo TTL.

## Kiểm tra sau khi chạy

- Mở model-driven app, xem `BMS Buildings`, `BMS Equipment`, `BMS Points`.
- Với readings, kiểm tra Point trước rồi kiểm tra `BMS Readings`.
- Đọc file receipt mới nhất trong `.artifacts/spo-local/` để biết số row đã
  delivered/skipped và GUID Dataverse.
- `equipment.xlsx` hiện có nhiều `equipment_type` ngoài Choice contract nên phải
  trả `Invalid/SPO-CHOICE`; đó là hành vi đúng, không tự mở rộng schema.

## Nút Sync SharePoint với dữ liệu cloud thật

Luồng mới không đọc bản copy trong `data/`. Dữ liệu đi theo chuỗi sau:

```text
Sync SharePoint now (model-driven app)
  -> POST fmc_RequestSpoSync
  -> Power Automate: FMC - Request SPO Sync
  -> Get files (properties only), recursive dưới FMC-Inbox
  -> source key = namespace + library ID + SharePoint item ID
  -> ETag hiện tại khác fmc_spofile.fmc_etag?
       không: bỏ qua
       có: download file, kiểm tra ETag lần hai, archive vào fmc_spofile.fmc_file
  -> local CLI claim receipt có Import Status = Not Requested
  -> download File column theo block 4 MiB
  -> parser + mapper + DataverseBronzeWriter
  -> Import Status = Imported, Imported ETag = Archived ETag
```

`SharePoint item ID` giữ nguyên khi sửa nội dung hoặc đổi tên file, nên cùng một file
vẫn dùng cùng một `fmc_spofile`. `ETag` đổi khi nội dung/phiên bản SharePoint đổi, nên
nút có thể quét lại an toàn mà không import lại phiên bản cũ.

## Nút Sync All cho SQL và SharePoint

Để chạy cả hai nguồn bằng một lần bấm, mở hai local worker bằng launcher:

```powershell
.\START-ALL-DATAVERSE-SYNC.cmd
```

Launcher mở hai cửa sổ độc lập:

- `DataverseSyncWorker` chạy `CommandDriven` và chờ `fmc_syncrequest` của SQL.
- `SPO.Ingestion.Cli watch-dataverse` chờ phiên bản file đã archive trong `fmc_spofile`.

Sau đó mở `FMC BMS Demo` → `Power Automate Demo` → bấm **Sync All now**:

```text
Sync All now
  -> POST fmc_RequestFullSync
  -> Power Automate: FMC - Request Full Sync
       |-> Perform unbound action: fmc_RequestBmsSync
       `-> Perform unbound action: fmc_RequestSpoSync
  -> SQL và SharePoint tiếp tục bằng pipeline, retry và trạng thái riêng
```

`Accepted` chỉ xác nhận flow điều phối đã nhận yêu cầu. Kiểm tra flow
`FMC - Request Full Sync` để thấy hai action dispatch; kiểm tra `Sync Requests`,
`SPO Files` và hai cửa sổ worker để xác nhận import thực tế. Nếu máy local tắt,
SQL request vẫn ở queue và file SharePoint vẫn có thể được archive, nhưng Bronze
chưa hoàn tất cho đến khi worker tương ứng chạy lại.

### 1. Chạy consumer local

Chạy liên tục trước khi bấm nút trong app:

```powershell
dotnet run --project .\SPO.Ingestion\SPO.Ingestion.Cli -c Release --no-build -- `
  watch-dataverse --config .\config\spo-ingestion.json `
  --appsettings .\Dataverse.SyncWorker\Dataverse.SyncWorker.App\appsettings.json `
  --max 20 --poll-seconds 10
```

Ý nghĩa:

- `watch-dataverse`: không đọc thư mục `data/`; polling `fmc_spofile` trên Dataverse.
- `--max 20`: một vòng claim tối đa 20 file để tránh một process giữ queue quá lâu.
- `--poll-seconds 10`: khi chưa có file chờ, kiểm tra lại sau 10 giây.
- `Ctrl+C`: dừng an toàn sau action đang chạy.

Muốn test một vòng rồi thoát:

```powershell
dotnet run --project .\SPO.Ingestion\SPO.Ingestion.Cli -c Release --no-build -- `
  ingest-dataverse-once --config .\config\spo-ingestion.json `
  --appsettings .\Dataverse.SyncWorker\Dataverse.SyncWorker.App\appsettings.json --max 20
```

### 2. Bấm nút trong app

1. Mở `FMC BMS Demo`.
2. Chọn trang `Sync Data` / `Power Automate Demo`.
3. Bấm **Sync SharePoint now**.
4. Custom API trả `Request ID`; đây là xác nhận flow đã được phát event, chưa phải
   xác nhận mọi row đã import.
5. Mở **Open SPO flow** để xem `Run history` và action `List_files`.
6. Mở table `SPO Files`: file mới/đổi phải có `Archive Status = Archived`, sau đó
   consumer local đổi `Import Status` từ `Not Requested` sang `Imported`.

### 3. Test file mới và file cập nhật

File mới:

1. Upload một file vào đúng folder mapping, ví dụ
   `FMC-Inbox/01-Master/Building/building.xlsx`.
2. Bấm nút; flow tạo một `fmc_spofile`, archive binary và local consumer import.

File cập nhật:

1. Sửa row `BLD001` trong cùng file rồi upload/replace đúng file SharePoint cũ.
2. Bấm nút; item ID không đổi nhưng ETag đổi.
3. Flow update receipt cũ và đặt `Import Status = Not Requested`.
4. Local consumer upsert `BLD001` theo `fmc_buildingcode`; không tạo Building trùng.
5. Bấm lại khi file không đổi: flow thấy ETag trùng và action `Skip_unchanged` chạy;
   Dataverse không import lại.

### 4. Folder được nhận

Flow chỉ archive `.xlsx` trong các prefix đang enabled:

- `/Shared Documents/FMC-Inbox/01-Master/Building/`
- `/Shared Documents/FMC-Inbox/01-Master/Equipment/`
- `/Shared Documents/FMC-Inbox/01-Master/ElectricMeter/`
- `/Shared Documents/FMC-Inbox/01-Master/WaterMeter/`
- `/Shared Documents/FMC-Inbox/02-Telemetry/Electricity/`
- `/Shared Documents/FMC-Inbox/02-Telemetry/Water/`

File ngoài các prefix trên bị bỏ qua. Kích thước tối đa là 50 MiB; `fmc_file` và
`fmc_filesize` phải được provision với cùng giới hạn trước khi enable flow.

### 5. Deploy các thành phần của nút

```powershell
dotnet build .\MetasysPoc.sln -c Release

dotnet run --project .\DataverseSyncWorker -c Release --no-build -- `
  --provision-spo-ingestion

dotnet run --project .\DataverseSyncWorker -c Release --no-build -- `
  --register-plugin --plugin-path=.\Dataverse.Plugin\FMCentralBms.Plugins\bin\Release\net48\FMCentralBms.Plugins.dll

python .\scripts\provision-spo-sync-flow.py render
python .\scripts\provision-spo-sync-flow.py deploy
python .\scripts\provision-spo-sync-flow.py test

python .\scripts\provision-full-sync-flow.py render
python .\scripts\provision-full-sync-flow.py deploy
python .\scripts\provision-full-sync-flow.py test
```

`--provision-spo-ingestion` nâng giới hạn File column lên 50 MiB và không tạo thêm
Bronze table. `--register-plugin` deploy `RequestSpoSync` cùng assembly version mới.
Script Python bind connection references, tạo/enable flow, deploy web resource của
nút và verify callback registration. Script chỉ dùng developer login hiện có; không
lưu access token vào source hoặc artifact.

`provision-full-sync-flow.py` tạo/enable flow orchestration, bind Dataverse
connection reference hiện có và publish nút **Sync All now**. Flow chỉ dispatch
hai Custom API con, không copy logic ingest và không tạo table mới.

Lần import ETag đầu tiên của telemetry phải replay toàn bộ history. Trong lần đo dev
2026-09-15, file electricity 172.800 rows mất khoảng 37 phút với batch 100 và request
tuần tự. Đây là đường local an toàn để demo, chưa phải throughput/SLA production.
Sau khi `Imported ETag` bằng `Archived ETag`, lần quét không đổi không queue lại file.
