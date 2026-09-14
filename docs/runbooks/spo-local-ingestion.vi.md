# Local SPO ingestion runbook

## Mục đích

Đây là đường chạy demo khi chưa có Azure subscription. Local CLI đọc bản copy
các file đã lấy từ SharePoint, dùng cùng parser/mapper/writer của logic cloud và
ghi trực tiếp vào các Bronze table hiện có trong Dataverse.

Không dùng flow archive cũ `FMC - Copy SPO Demo File (Manual)`. Flow cũ chỉ ghi
binary vào `fmc_spofile`; local runner này ghi dữ liệu nghiệp vụ vào:

- `building.csv` -> `fmc_bmsbuilding`
- `water_meter.csv` -> `fmc_bmsequipment`
- `electricity_reading_hourly.csv` -> `fmc_bmspoint` + `fmc_bmsreading`
- `water_reading_hourly.csv` -> `fmc_bmspoint` + `fmc_bmsreading`

## Điều kiện

- Đã đăng nhập developer Dataverse token theo cấu hình hiện có của
  `DataverseSyncWorker`.
- File mẫu nằm trong thư mục `data/`.
- Organization trong `DataverseSyncWorker/appsettings.json` là environment dev
  đã được kiểm tra.

## Chạy

Từ repository root:

```powershell
dotnet build .\MetasysPoc.sln -c Release

dotnet run --project .\SpoIngestion.Cli -c Release --no-build -- `
  ingest-local --root .\data --config .\config\spo-ingestion.json `
  --source spo-building --utc-now 2026-09-14T00:00:00Z
```

`--source` là tùy chọn. Bỏ `--source` để chạy tất cả source enabled; runner tự
xếp thứ tự Building -> Equipment/WaterMeter -> readings để lookup parent tồn tại
trước. Receipt đầy đủ được lưu dưới `.artifacts/spo-local/`.

Ví dụ chạy từng bước:

```powershell
dotnet run --project .\SpoIngestion.Cli -c Release --no-build -- `
  ingest-local --root .\data --config .\config\spo-ingestion.json --source spo-building

dotnet run --project .\SpoIngestion.Cli -c Release --no-build -- `
  ingest-local --root .\data --config .\config\spo-ingestion.json --source spo-water-meter

dotnet run --project .\SpoIngestion.Cli -c Release --no-build -- `
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

## Kiểm tra sau khi chạy

- Mở model-driven app, xem `BMS Buildings`, `BMS Equipment`, `BMS Points`.
- Với readings, kiểm tra Point trước rồi kiểm tra `BMS Readings`.
- Đọc file receipt mới nhất trong `.artifacts/spo-local/` để biết số row đã
  delivered/skipped và GUID Dataverse.
- `equipment.csv` hiện có nhiều `equipment_type` ngoài Choice contract nên phải
  trả `Invalid/SPO-CHOICE`; đó là hành vi đúng, không tự mở rộng schema.
