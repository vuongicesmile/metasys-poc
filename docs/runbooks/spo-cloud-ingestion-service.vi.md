# SPO Cloud Ingestion Service: build, deploy và test

Ngày kiểm tra: 2026-09-14.

Trạng thái hiện tại: parser, mapper, durable job store, Dataverse writer, Azure Functions,
Bicep và dry-run CLI đã implement. Chưa có cloud deployment receipt vì Azure CLI hiện
chỉ đăng nhập tenant `31983a93-6f80-4356-a4e1-a65055e8327e` ở tenant-level, không có
subscription/resource group. Không có record Dataverse nào được ghi bởi dry-run.

Read-only live check lúc `2026-09-14T08:35:15Z` xác nhận đúng organization
`ab191700-b99e-f111-aaa0-000d3a80bb96`: Building/Equipment/Point đều là Standard,
ba alternate key đều Active, hai lookup relationship tồn tại; command đồng thời validate
`fmc_bmsreading` vẫn là Elastic và Choice contract không đổi. Không có provisioning/sync.

## 1. Luồng đã implement

```text
SharePoint file event
  -> Power Automate: metadata before -> content -> raw Blob -> metadata after
  -> POST FinalizeSpoCapture
  -> immutable manifest + Azure Storage Queue
  -> ProcessSpoJob (lease)
  -> parse CSV/JSON/XLSX
  -> validate toàn file
  -> Building -> Equipment -> current Point -> elastic Reading
  -> Blob row receipts + Completed manifest
```

Không tạo table/column Dataverse mới. Target vẫn là:

| Dataset | Table hiện có | Trạng thái mapping |
| --- | --- | --- |
| Building | `fmc_bmsbuilding` | Enabled |
| Equipment | `fmc_bmsequipment` | Enabled, nhưng chỉ nhận Choice live đã đăng ký |
| Water meter | `fmc_bmsequipment` | Enabled dưới Choice `WaterMeter` |
| Electricity readings | `fmc_bmspoint`, `fmc_bmsreading` | Enabled |
| Water readings | `fmc_bmspoint`, `fmc_bmsreading` | Enabled |
| Electric meter catalog | `fmc_bmsequipment` | Enabled dưới Choice `ElectricMeter` |
| Fault/Maintenance/KPI | Không có target chuẩn | Disabled |

Các file chính:

| File/project | Ý nghĩa |
| --- | --- |
| `config/spo-ingestion.json` | Route folder → mapper, allowlist extension, row/file limits, UTC offset |
| `SPO.Ingestion/SPO.Ingestion.Business/Parsing/TabularParser.cs` | CSV quoted newline, JSON array, XLSX sheet; reject formula và zip expansion quá lớn |
| `SPO.Ingestion/SPO.Ingestion.Business/Mapping/SpoBronzeMapper.cs` | Typed Dataverse `Entity`, decimal 4 số, UTC, stable GUID/partition, TTL |
| `SPO.Ingestion/SPO.Ingestion.DataAccess/Dataverse/DataverseBronzeWriter.cs` | Resolve alternate key/lookup, ownership, current-state concurrency và batch history |
| `SPO.Ingestion/SPO.Ingestion.DataAccess/Storage/BlobJobStore.cs` | Raw hash, JobKey, manifest, queue, lease, receipt |
| `SPO.Ingestion/SPO.Ingestion.Business/Processing/SpoJobProcessor.cs` | Validate-first, chunk file lớn, dependency retry và terminal status |
| `SPO.Ingestion/SPO.Ingestion.Functions/Functions.cs` | HTTP finalizer, queue consumer và 5-minute reconciler |
| `infra/spo-ingestion/main.bicep` | Storage, containers, queue, Windows Consumption Function App và Application Insights |
| `scripts/deploy-spo-ingestion.ps1` | Deploy hạ tầng, publish ZIP và deploy Function code |

## 2. Contract quan trọng

- Filename không chọn mapper. `pathPrefix` + extension trong config chọn mapper; match 0 hoặc nhiều hơn 1 đều fail.
- Mỗi source enabled khai báo `ownedKeyPrefix` (`BLD`, `EQ`, `WM`, `EM`). Key ngoài vùng được gán trả `SPO-OWNERSHIP`, giúp catalog không tùy ý overwrite key của SQL.
- Timestamp telemetry hiện không có zone trong CSV, nên hai source reading khai báo `timestampUtcOffset: +07:00`; mapper convert sang UTC đúng một lần. Timestamp có `Z`/offset trong file được ưu tiên.
- SharePoint event không ghi `fmc_lastsqlid`, `fmc_sqlreadingid` hoặc `fmc_sqlingestedat`.
- Point dùng identity tương thích SQL worker: `metasys-point|FMC|objectId`.
- SPO history dùng identity riêng: `spo-reading|sourceNamespace|meter|UTC timestamp|metric`; ordinal/file name không phải business identity.
- History TTL tính từ event time. Event hết hạn vẫn có thể cập nhật current Point nhưng không được cấp TTL mới.
- Water và Electricity Point phải resolve `fmc_bmsequipment` theo alternate key thật. Equipment phải resolve Building thật. File con đến trước file cha chuyển `WaitingDependency`, reconciler thử lại mỗi 5 phút, tối đa 12 attempt.
- Điện được chuẩn hóa còn hai Object Type: `Energy` dùng `kWh` và `Demand` dùng `kW`. Cột `voltage_v` và `power_factor` không tạo Point để tránh làm danh mục Object Type khó hiểu.
- Point đã có nhưng `fmc_sourcesystem` không phải `SharePoint` trả `SourceOwnershipConflict`, không overwrite source SQL tùy ý.
- Job được lease 60 giây và renew mỗi 35 giây. Save terminal state bắt buộc đúng lease; stale worker không thể complete manifest.

## 3. Build và dry-run local

Chạy từ root repository:

```powershell
dotnet restore .\MetasysPoc.sln
dotnet build .\MetasysPoc.sln -c Release --no-restore
dotnet test .\tests\MetasysPoc.Tests\MetasysPoc.Tests.csproj -c Release --no-restore
dotnet run --project .\SPO.Ingestion\SPO.Ingestion.Cli -c Release --no-restore -- `
  preview-all --root .\data --config .\config\spo-ingestion.json `
  --utc-now 2026-09-14T00:00:00Z
```

Storage smoke test dùng Azurite ở terminal riêng (bản Azurite local 3.29 cần skip
API-version check của Azure SDK mới):

```powershell
azurite --silent --skipApiVersionCheck --location .\.artifacts\azurite
```

```powershell
$env:RUN_SPO_AZURITE_TESTS = '1'
dotnet test .\tests\MetasysPoc.Tests\MetasysPoc.Tests.csproj -c Release --no-build --no-restore
Remove-Item Env:\RUN_SPO_AZURITE_TESTS
```

Kết quả hiện tại: 40/40 tests pass, gồm raw SHA-256, cùng capture → cùng Job ID,
conditional manifest save dưới Blob lease và cleanup containers/queue test riêng.

Dry-run cố định trên bộ `data/` ngày 2026-09-14:

| Source | Input | Kết quả |
| --- | ---: | --- |
| Building | 120 | Ready → 120 Buildings |
| Equipment | 1.921 | Invalid → 132 valid candidates, 1.789 `SPO-CHOICE`; validate-first nên ghi 0 |
| Water meter | 120 | Ready → 120 Equipment |
| Electric meter | 240 | Ready → 240 Equipment loại Electric Meter |
| Electricity | 172.800 | Ready → 480 Points; retained/expired Readings tính theo 2 metrics và TTL tại thời điểm chạy |
| Water | 86.400 | Ready → 240 Points, 90.240 retained Readings, 82.560 expired metrics |

`equipment.xlsx` có AHU, Chiller, Fire Pump... nhưng Dataverse hiện chỉ chuẩn hóa
`WaterMeter=789100000`, `TemperatureSensor=789100001`, `TestRig=789100002`,
`ElectricMeter=789100003`.
Config nhận thêm display aliases `Temperature Sensor` và `Test Rig` vào đúng value cũ.
Do yêu cầu không đổi schema/Choice, service reject rõ thay vì ghi sai semantic.

## 4. Điều kiện trước khi deploy cloud

1. Có Azure subscription, quyền tạo resource group, Storage và Function App.
2. Có Dataverse application registration + application user được gán role
   `FM Central BMS Integration`; không dùng developer token local làm cloud credential.
3. Role có Create/Read/Write/Append/Append To cho 4 target tables và các lookup.
4. Metadata live đúng environment `https://org06cbc9ec.crm5.dynamics.com/`, organization
   `ab191700-b99e-f111-aaa0-000d3a80bb96`, solution `FMCentralBms`.
5. Power Automate có SharePoint connection và Azure Blob Storage connection.
   Azure Blob Storage managed connector là Premium và Storage POC phải public-network
   reachable; production cần review network/DLP/license trước khi bật.

Kiểm tra account Azure trước khi chạy script:

```powershell
az account show --query "{subscription:id,name:name,tenant:tenantId}" -o json
az account list --query "[?state=='Enabled'].{subscription:id,name:name}" -o table
```

Nếu subscription là `N/A(tenant level account)`, dừng tại đây; tenant login không cấp
quyền tạo Azure resource.

## 5. Deploy Azure Storage và Function App

Không commit connection string. Đặt nó trong process environment rồi chạy script:

```powershell
$env:SPO_DATAVERSE_CONNECTION_STRING = 'AuthType=ClientSecret;Url=https://org06cbc9ec.crm5.dynamics.com;ClientId=<app-id>;ClientSecret=<secret>'

.\scripts\deploy-spo-ingestion.ps1 `
  -SubscriptionId '<subscription-guid>' `
  -ResourceGroupName 'rg-fmc-spo-dev' `
  -Location 'southeastasia' `
  -FunctionAppName 'func-fmc-spo-dev-<unique>' `
  -StorageAccountName 'stfmcspodev<unique>'

Remove-Item Env:\SPO_DATAVERSE_CONNECTION_STRING
```

Script tạo `spo-raw`, `spo-control`, queue `spo-ingestion`, Windows Consumption Function
App .NET 10 isolated và Application Insights, rồi ZIP-deploy code. Bicep không tạo hoặc
đổi Dataverse schema. Với production, thay client secret app setting bằng Key Vault
reference/rotation policy trước go-live.

Sau deploy, mở Function App → Functions → `FinalizeSpoCapture` → **Get Function URL**.
Giữ function key trong Power Automate secret/environment configuration; không ghi vào repo.

## 6. Tạo Power Automate capture flow

Tạo cloud flow trong solution `FMCentralBms`, ban đầu để **Off**. File event flow dùng
SharePoint connector, không gọi Graph API trực tiếp.

1. Trigger **When a file is created or modified (properties only)**.
   Site: `https://titancorpvncom.sharepoint.com/sites/Powerplatform`; Library: `Tài liệu`.
   Scope ở library/root để subfolder được nhận, rồi chỉ allow các prefix trong config.
2. **Get file properties** từ Item ID của trigger (`Source_properties`). Bỏ folder,
   file tạm `~$*`, extension ngoài `csv/json/xlsx` và path không đăng ký.
3. **Get file metadata** (`Metadata_before`) bằng `{Identifier}`. Lấy `ETag` và `Size`;
   reject rỗng, `Size <= 0` hoặc lớn hơn 52.428.800 bytes.
4. **Get file content** (`Get_content`) bằng cùng `{Identifier}`.
5. Khởi tạo string `CaptureBlobName`:

   ```text
   concat('captures/',formatDateTime(utcNow(),'yyyy/MM/dd/HH/'),guid(),'-',outputs('Source_properties')?['{FilenameWithExtension}'])
   ```

6. Azure Blob Storage **Create blob (V2)**:
   container `/spo-raw`, Blob name=`CaptureBlobName`, content=`File Content`.
7. **Get file metadata** (`Metadata_after`) lần nữa. Nếu ETag khác
   `Metadata_before`, terminate Failed; raw orphan không có manifest sẽ không được ingest.
8. HTTP POST đến Function URL `api/spo/captures/finalize`, content type JSON:

   ```json
   {
     "sourcePath": "@{outputs('Source_properties')?['{FullPath}']}",
     "rawBlobName": "@{variables('CaptureBlobName')}",
     "sourceETag": "@{body('Metadata_before')?['ETag']}",
     "contentLength": @{int(body('Metadata_before')?['Size'])}
   }
   ```

   Nếu designer biến `contentLength` thành string, dùng Expression/object để giá trị
   được gửi là JSON number. Finalizer tự đọc raw Blob và tính SHA-256; flow không tự bịa
   hàm hash. HTTP 202 trả `jobId`, `jobKey`, `status`.
9. Catch scope ghi lỗi vào run history và fail run. Chỉ coi capture thành công khi HTTP
   finalizer trả 202.

Giữ trigger concurrency nhỏ lúc pilot. Idempotency thực tế nằm ở
`JobKey = hash(sourcePath, ETag, raw SHA-256, mapping version)`, không phụ thuộc
concurrency=1. Azure Blob connector có throttling/size limits; bật chunking nếu action
cho phép và vẫn giữ giới hạn service 50 MiB.

## 7. Thứ tự test UI

Flow event không tự ăn các file đã tồn tại trước khi bật. Sau khi flow On, dùng SharePoint UI:

1. Upload lại hoặc sửa `building.xlsx` trong `FMC-Inbox/01-Master/Building`.
2. Đợi manifest Completed; mở Model-driven app → BMS Buildings, kiểm tra code/name.
3. Upload lại `water_meter.xlsx`; sau Completed, kiểm tra BMS Equipment.
4. Upload `water_reading_hourly.xlsx`; kiểm tra BMS Points trước, rồi retained BMS Readings.
5. Upload `electric_meter.xlsx`; kiểm tra 240 Equipment có type `Electric Meter` và Building lookup.
6. Upload `electricity_reading_hourly.xlsx`; kiểm tra 480 current points, chỉ có `Energy/kWh` và `Demand/kW`, tất cả có Equipment lookup.
7. Upload lại đúng bytes/ETag event: có thể thêm queue message nhưng target identity không duplicate.
8. Upload `equipment.xlsx`: run phải fail validation `SPO-CHOICE`, không có partial Equipment write.

Trong Azure Portal:

- Storage Browser → `spo-control/manifests/<jobId>.json`: `Completed`, counters và error.
- `spo-control/receipts/<jobId>/`: point/history receipt segments có target GUID/partition.
- Application Insights Logs: tìm `Processing SPO job`, `WaitingDependency`,
  `SourceOwnershipConflict`, retry/throttling.
- Queue poison: sau 5 dequeue cho lỗi runtime; validation contract nằm ở Failed manifest.

## 8. Phần chưa hoàn tất

- Chưa deploy Azure vì thiếu subscription, nên chưa có Function URL/storage connection/receipt live.
- Chưa tạo event flow vì Blob connection và Function URL chưa tồn tại.
- Chưa implement SharePoint List adapter/source registration, initial full-library scan,
  scan cursor, status/retry UI hoặc Silver refresh grouping (Phase 5-6).
- Chưa mở rộng Choice Equipment; `equipment.xlsx` chủ động bị reject theo contract hiện tại.
- Chưa chạy live Dataverse write/replay/load test; build và dry-run không thay thế deployment evidence.

Khi có subscription, thực hiện section 4-7, lưu deployment outputs/run IDs dưới
`.artifacts/` và cập nhật runbook bằng receipt live thay vì đổi dòng trạng thái bằng suy đoán.
