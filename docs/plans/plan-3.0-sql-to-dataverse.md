# PLAN 3.0 — FM_Central SQL Server → Dataverse

> Historical design. Implementation amendments: see
> [Dataverse deployment](../reference/dataverse-deployment.md).
> Paths and commands in the original design below are relative to the repository root.
> Elastic history uses a deterministic GUID and built-in partition key (custom
> alternate keys are unsupported). Per-row SQL delivery receipts replace the
> high-watermark-only algorithm to handle out-of-order commits. Current state
> uses latest event time; TTL is calculated from event age. These amendments
> take precedence over the original design below.

> Implementation receipt (2026-09-07): the current Developer environment uses
> the interactive identity already signed in through the Azure CLI bundle in
> `rmit-fm-data`. `DataverseSyncWorker` acquires a token on demand through
> `scripts/get-dataverse-token.py`, verifies the Organization ID before writing,
> and stores no token or refresh token in this repository. `FMCentralBms` has
> been provisioned and exported; the initial backlog and live reconciliation
> completed successfully. A service principal remains required for unattended
> production deployment.

## 1. Mục tiêu

Đưa dữ liệu BMS từ SQL Server hiện tại vào đúng Dataverse environment đang dùng,
không thay thế SQL Server và không làm gián đoạn luồng Metasys COV hiện tại.

```text
Fake Metasys API :5100
          |
          | COV / SSE
          v
BmsIngestionApp :5200
          |
          | INSERT
          v
FM_Central.raw.bms_reading       <- nguồn raw và full history
          |
          | incremental by id
          v
DataverseSyncWorker
          |
          | OAuth + UpsertMultiple
          v
Vuong Nguyen Quoc's Environment
https://org06cbc9ec.crm5.dynamics.com/
          |
          +-- fmc_bmspoint        (standard table/current state)
          +-- fmc_bmsreading      (elastic table/recent history)
```

Environment đã xác nhận bằng PAC CLI:

| Thuộc tính | Giá trị |
|---|---|
| Friendly name | Vuong Nguyen Quoc's Environment |
| Environment ID | `5abcb0e5-99b2-e51f-aa0e-90d84405798b` |
| Organization ID | `ab191700-b99e-f111-aaa0-000d3a80bb96` |
| Organization URL | `https://org06cbc9ec.crm5.dynamics.com/` |

Không tạo environment Dataverse mới.

## 2. Hiện trạng nguồn

Nguồn dữ liệu:

```text
SQL Server: localhost
Database:   FM_Central
Table:      raw.bms_reading
```

Các cột generic dùng để đồng bộ:

| SQL | Kiểu | Ý nghĩa |
|---|---|---|
| `id` | bigint | Watermark và external identity |
| `object_id` | varchar(100) | Mã BMS point |
| `object_name` | varchar(200) | Tên point |
| `object_type` | varchar(100) | WaterConsumption, Temperature... |
| `building` | varchar(100) | Tòa nhà |
| `reading_time` | datetime2 | Thời điểm reading |
| `reading_value` | decimal(18,4) | Giá trị |
| `unit` | varchar(50) | m3, C, L/s... |
| `source_system` | varchar(100) | Nguồn phát sinh |
| `ingested_at` | datetime2 | Thời điểm vào SQL |

Tại thời điểm lập plan bảng có 496 dòng. Các cột wide-table cũ (`meter_id`,
`water_flow`, `water_consumption`, `temperature`) được giữ lại nhưng không dùng
cho pipeline mới.

## 3. Quyết định kiến trúc

Không khuyến nghị dùng Power Automate chạy một flow cho từng COV. Với 3 point
thay đổi mỗi 3 giây, mức lý thuyết là:

```text
3 × (86,400 / 3) = 86,400 readings/ngày
```

Thiết kế được chọn:

1. SQL Server tiếp tục giữ raw/full history và là system of record.
2. Thêm một .NET Worker riêng tên `DataverseSyncWorker` vào solution hiện tại.
3. Worker chạy trên chính máy/on-prem hiện tại và chỉ cần outbound HTTPS tới
   Dataverse; không cần on-premises data gateway cho luồng custom C# này.
4. Dataverse giữ master/current state trong standard table.
5. Nếu thực sự cần xem reading-level history trong Power Apps, dùng elastic table
   và TTL. Không lưu lịch sử vô hạn trong Dataverse.
6. Đồng bộ theo SQL identity `id`, dùng upsert và checkpoint để chạy lại không trùng.

Lý do dùng elastic table cho history: Microsoft định vị elastic table cho tải ghi
lớn/bursty và IoT sensor data; table có `partitionid` và TTL tự động. Standard
table vẫn phù hợp hơn cho current state vì cần quan hệ, consistency và trải nghiệm
model-driven app.

## 4. Dataverse solution

Tạo unmanaged solution trong environment hiện tại:

```text
Display name: FM Central BMS
Unique name:  FMCentralBms
Publisher:    publisher hiện tại hoặc publisher mới
Prefix:       fmc
Version:      1.0.0.0
```

Không tạo component trực tiếp ngoài solution. Sau này solution có thể export để
đưa sang UAT/Production mà không đổi code đồng bộ.

## 5. Dataverse data model

### 5.1 `fmc_bmspoint` — Standard table

Một record cho mỗi BMS point.

| Display name | Logical name | Type | SQL mapping |
|---|---|---|---|
| Name | `fmc_name` | Text 200 | `object_name` |
| Object ID | `fmc_objectid` | Text 100 | `object_id` |
| Object Type | `fmc_objecttype` | Text 100 | `object_type` |
| Building | `fmc_building` | Text 100 | `building` |
| Current Value | `fmc_currentvalue` | Decimal 18,4 | `reading_value` |
| Unit | `fmc_unit` | Text 50 | `unit` |
| Last Reading Time | `fmc_lastreadingtime` | DateTime, UTC | `reading_time` |
| Source System | `fmc_sourcesystem` | Text 100 | `source_system` |
| Last SQL ID | `fmc_lastsqlid` | Text 20 | `id` converted to text |

Alternate key:

```text
AK_fmc_bmspoint_objectid = fmc_objectid
```

Mỗi batch cập nhật current state chỉ upsert reading mới nhất của từng
`object_id`. Nếu worker đọc được nhiều reading của cùng point, chỉ record có `id`
lớn nhất được ghi vào table này.

### 5.2 `fmc_bmsreading` — Elastic table

Một record cho mỗi COV, chỉ dùng khi người dùng cần recent history trong
Dataverse/Power Apps.

| Display name | Logical name | Type | SQL mapping |
|---|---|---|---|
| Name | `fmc_name` | Text 200 | `object_id + reading_time` |
| External Key | `fmc_externalkey` | Text 100 | `FMC-` + `id` |
| SQL Reading ID | `fmc_sqlreadingid` | Text 20 | `id` converted to text |
| Object ID | `fmc_objectid` | Text 100 | `object_id` |
| Object Name | `fmc_objectname` | Text 200 | `object_name` |
| Object Type | `fmc_objecttype` | Text 100 | `object_type` |
| Building | `fmc_building` | Text 100 | `building` |
| Reading Time | `fmc_readingtime` | DateTime, UTC | `reading_time` |
| Reading Value | `fmc_readingvalue` | Decimal 18,4 | `reading_value` |
| Unit | `fmc_unit` | Text 50 | `unit` |
| Source System | `fmc_sourcesystem` | Text 100 | `source_system` |
| SQL Ingested At | `fmc_sqlingestedat` | DateTime, UTC | `ingested_at` |
| Partition ID | `partitionid` | Text | `object_id` |
| Time to live | `ttlinseconds` | Integer | cấu hình retention |

Alternate key:

```text
AK_fmc_bmsreading_externalkey = fmc_externalkey
```

Dùng `FMC-123` thay vì `FM_Central:123` vì một số ký tự như `:` không phù hợp
cho alternate-key URL của Dataverse.

Partition strategy:

```text
partitionid = object_id
```

TTL đề xuất cho POC:

```text
30 ngày = 2,592,000 giây
```

Sau POC, chọn 30/90/180 ngày dựa trên capacity thực tế. SQL vẫn giữ toàn bộ lịch
sử nên TTL không gây mất dữ liệu nguồn.

## 6. SQL integration metadata

Không thêm trạng thái sync trực tiếp vào `raw.bms_reading`. Tạo schema riêng:

```sql
CREATE SCHEMA integration;
```

### 6.1 `integration.dataverse_sync_state`

```text
pipeline_name          varchar(100) PK
last_successful_id     bigint
last_started_at        datetime2
last_completed_at      datetime2
status                 varchar(30)
last_error             nvarchar(2000)
updated_at             datetime2
```

Checkpoint ban đầu:

```text
pipeline_name = BMS_READING_TO_DATAVERSE
last_successful_id = 0
```

### 6.2 `integration.dataverse_dead_letter`

```text
id                     bigint identity PK
bms_reading_id         bigint
target_table           varchar(100)
payload_json           nvarchar(max)
error_message          nvarchar(4000)
attempt_count          int
first_failed_at        datetime2
last_failed_at         datetime2
resolved_at            datetime2 null
```

Dead-letter không được làm dừng toàn bộ pipeline sau khi xác định chính xác row
lỗi. Tuy nhiên checkpoint chỉ được advance khi tất cả row trước nó đã thành công
hoặc đã được ghi nhận rõ vào dead-letter theo chính sách được duyệt.

## 7. Dataverse authentication trong environment hiện tại

PAC CLI hiện đang đăng nhập interactive; credential đó dùng để tạo/deploy
solution trong giai đoạn phát triển, không dùng làm credential chạy nền.

Cho runtime:

1. Tạo Microsoft Entra app registration.
2. Tạo Dataverse application user cho app trong environment ID
   `5abcb0e5-99b2-e51f-aa0e-90d84405798b`.
3. Tạo security role `FM Central BMS Integration`.
4. Chỉ cấp Create/Read/Write cho `fmc_bmspoint` và `fmc_bmsreading`; không cấp
   System Administrator.
5. Dùng certificate cho production. Client secret được chấp nhận trong POC.
6. Không commit secret vào `appsettings.json`.

Biến môi trường .NET:

```text
Dataverse__Url=https://org06cbc9ec.crm5.dynamics.com/
Dataverse__TenantId=<tenant-guid>
Dataverse__ClientId=<app-registration-client-id>
Dataverse__ClientSecret=<secret-only-for-poc>
Dataverse__HistoryEnabled=true
Dataverse__HistoryTtlSeconds=2592000
Dataverse__BatchSize=100
Dataverse__PollIntervalSeconds=10
```

Package dự kiến:

```text
Microsoft.PowerPlatform.Dataverse.Client
Microsoft.Data.SqlClient
```

Dùng `ServiceClient`, không dùng username/password hay Office365 auth cũ.

## 8. Incremental synchronization algorithm

### 8.1 Đọc batch

```sql
SELECT TOP (@batch_size)
    id,
    object_id,
    object_name,
    object_type,
    building,
    reading_time,
    reading_value,
    unit,
    source_system,
    ingested_at
FROM raw.bms_reading
WHERE id > @last_successful_id
ORDER BY id;
```

Batch size khởi đầu là 100.

### 8.2 Validate

Trước khi gửi:

- `object_id` không rỗng.
- `reading_time` được chuẩn hóa UTC.
- `reading_value` nằm trong precision Dataverse đã cấu hình.
- `external_key = FMC-{id}` là duy nhất.
- Trong một `UpsertMultiple` payload không có duplicate alternate key.

### 8.3 Ghi Dataverse

Với mỗi SQL batch:

1. Group theo `object_id`, lấy row có `id` lớn nhất.
2. Upsert nhóm đó vào `fmc_bmspoint` bằng alternate key `fmc_objectid`.
3. Nếu history enabled, map toàn bộ batch thành `fmc_bmsreading`.
4. Gửi `UpsertMultiple` theo batch 100 row.
5. Khi cả hai target hoàn tất, cập nhật checkpoint thành `MAX(id)` của batch.
6. Tiếp tục ngay nếu còn dữ liệu; nếu hết, chờ poll interval.

Delivery semantic:

```text
SQL read + Dataverse upsert + checkpoint = at-least-once, idempotent
```

Nếu process chết sau khi Dataverse ghi xong nhưng trước checkpoint, batch được gửi
lại; alternate key khiến dữ liệu được update chứ không tạo bản sao.

## 9. Retry và throttling

Đây là integration cloud nên retry là bắt buộc dù Plan 2.0 chưa có retry.

- Retry transient network errors và HTTP 429/5xx.
- Tôn trọng `Retry-After` từ Dataverse.
- Exponential backoff có jitter cho lỗi không có `Retry-After`.
- Không retry vô hạn đối với validation/permission/schema errors.
- Giới hạn concurrency ban đầu là 1; chỉ tăng sau khi đo throughput.
- Log correlation gồm SQL batch range, target table và request ID.

Microsoft áp dụng service-protection limits theo request count, execution time và
concurrency trong cửa sổ trượt. Không hard-code throughput dựa trên con số limit;
để server điều tiết qua `Retry-After`.

## 10. Project structure sau khi triển khai

```text
metasys-poc/
|
|-- MetasysPoc.sln
|-- FakeMetasysApi/
|-- BmsIngestionApp/
|-- DataverseSyncWorker/
|   |-- Models/
|   |   |-- BmsReading.cs
|   |   |-- DataverseOptions.cs
|   |   `-- SyncStatus.cs
|   |-- Services/
|   |   |-- SqlReadingSource.cs
|   |   |-- DataverseWriter.cs
|   |   |-- SyncCheckpointStore.cs
|   |   `-- DataverseSyncBackgroundService.cs
|   |-- Program.cs
|   `-- appsettings.json
|-- dataverse/
|   `-- FM-Central-BMS solution source
|-- sql/
|   |-- create-bms-tables.sql
|   `-- create-dataverse-sync-tables.sql
`-- docs/plans/plan-3.0-sql-to-dataverse.md
```

Worker có health/status HTTP endpoint riêng:

```text
http://localhost:5300/swagger
GET /api/dataverse-sync/status
GET /api/dataverse-sync/dead-letters
POST /api/dataverse-sync/run-once
```

`run-once` chỉ bật trong Development hoặc được bảo vệ bằng authorization khi
triển khai thật.

## 11. Backfill strategy

POC hiện chỉ có hàng trăm row nên có thể backfill từ `id = 0`.

Trình tự:

1. Dừng worker incremental, không cần dừng Metasys ingestion vào SQL.
2. Ghi lại `backfill_high_watermark = MAX(raw.bms_reading.id)`.
3. Backfill `id <= backfill_high_watermark` theo batch 100.
4. Kiểm tra count và sample values.
5. Bật incremental từ watermark đó.
6. Worker xử lý những row được tạo trong lúc backfill.

Không dùng `reading_time` làm watermark vì có thể trùng thời gian hoặc đến muộn.

## 12. Capacity guardrail

Trước khi bật history:

1. Đo số row/ngày thực tế trong SQL trong ít nhất 24 giờ.
2. Kiểm tra Dataverse database capacity của environment hiện tại.
3. Chạy POC 7 ngày với TTL 30 ngày.
4. So sánh table growth và Power Platform request consumption.

Decision gate:

| Điều kiện | Quyết định |
|---|---|
| Chỉ cần current value trong app | Chỉ bật `fmc_bmspoint`, tắt history |
| Cần recent history, write volume cao | Bật elastic `fmc_bmsreading` + TTL |
| Cần complex joins/transactions trên history | Aggregate trước khi ghi standard table |
| Cần full historical analytics | Đọc từ SQL/warehouse, không copy toàn bộ vào Dataverse |

## 13. Phases triển khai

### Phase A — Environment và solution

- Xác nhận publisher prefix.
- Tạo `FM Central BMS` solution trong environment hiện tại.
- Tạo standard `fmc_bmspoint`.
- Tạo elastic `fmc_bmsreading` nếu capacity gate cho phép.
- Tạo alternate keys và publish customizations.

### Phase B — Identity và security

- Entra app registration.
- Application user trong đúng environment.
- Least-privilege security role.
- Secret/certificate nằm ngoài source control.
- Connection smoke test bằng `ServiceClient.IsReady`.

### Phase C — SQL sync foundation

- Tạo schema/table checkpoint và dead-letter.
- Thêm index `raw.bms_reading(id)` nếu execution plan cần.
- Viết query incremental và integration tests.

### Phase D — Worker POC

- Scaffold `DataverseSyncWorker`.
- Map SQL row → Dataverse entities.
- Upsert current state.
- Upsert elastic history theo batch.
- Checkpoint chỉ sau success.
- Swagger status trên port 5300.

### Phase E — Backfill và reconciliation

- Backfill toàn bộ 496+ row hiện tại.
- So sánh SQL count với Dataverse count trong phạm vi watermark.
- So sánh latest value/time cho từng object.
- Test chạy lại cùng batch để chứng minh không duplicate.

### Phase F — Hardening

- Retry `Retry-After`/transient failures.
- Structured logging.
- Dead-letter replay.
- Metrics: lag, rows/sec, last checkpoint, failures.
- Certificate auth và secret rotation.
- Chuyển worker thành Windows Service nếu cần chạy liên tục.

## 14. Acceptance criteria

POC đạt khi:

1. Worker kết nối đúng organization URL hiện tại.
2. `fmc_bmspoint` có đúng một row cho mỗi `object_id`.
3. Current value/time trong Dataverse khớp SQL row mới nhất.
4. History không duplicate khi chạy lại cùng SQL batch.
5. Checkpoint tăng đơn điệu và không bỏ qua row.
6. Khi Dataverse tạm lỗi, SQL ingestion vẫn tiếp tục độc lập.
7. Sau khi Dataverse hoạt động lại, sync lag trở về 0.
8. Secret không xuất hiện trong Git, log hoặc Swagger.
9. Dashboard status cho biết `last_successful_id`, lag, batch result và lỗi gần nhất.
10. Capacity/request rate nằm trong guardrail đã chấp thuận.

## 15. Rollback

- Tắt `DataverseSyncWorker`; luồng Fake Metasys → SQL không bị ảnh hưởng.
- Không giảm/reset SQL checkpoint khi chưa điều tra.
- Dataverse rows có thể xóa theo `fmc_externalkey`/solution sau khi export backup.
- Không drop hoặc truncate `raw.bms_reading`.
- Gỡ application-user role nếu dừng integration vĩnh viễn.

## 16. Tài liệu Microsoft dùng cho quyết định

- [OAuth authentication with Dataverse](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/authenticate-oauth)
- [Dataverse SDK for .NET](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/org-service/overview)
- [Use Upsert for data integration](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/use-upsert-insert-update-record)
- [Dataverse bulk operation messages](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/bulk-operations)
- [Service protection API limits](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/api-limits)
- [Elastic tables for high-volume/IoT data](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/elastic-tables)
- [Define alternate keys](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/define-alternate-keys-portal)
- [Dataverse storage capacity](https://learn.microsoft.com/en-us/power-platform/admin/capacity-storage)

## 17. Thứ tự làm đề xuất

```text
1. Confirm publisher prefix + capacity
2. Create solution in current environment
3. Create fmc_bmspoint
4. Decide history enabled/disabled
5. Create fmc_bmsreading elastic if enabled
6. Create application user + role
7. Create SQL checkpoint/dead-letter
8. Build DataverseSyncWorker
9. Backfill
10. Validate idempotency and reconciliation
11. Enable continuous incremental sync
12. Harden and deploy
```
