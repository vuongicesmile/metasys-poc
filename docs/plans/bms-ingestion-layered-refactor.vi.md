# Refactor BMS Ingestion theo kiến trúc nhiều tầng

Trạng thái: **đã triển khai trong source local ngày 2026-09-17**

Phạm vi: `BMS.IngestionApp` và bốn class library phục vụ ingestion

Không đổi: `FakeMetasysApi`, SQL schema, port 5100/5200, COV/SSE contract hoặc `DataverseSyncWorker`

Tài liệu này vừa là implementation reference, vừa là bài thực hành để có thể tự làm lại.
Docs UI tương tác nằm tại `docs/interactive/bms-ingestion-refactor-lab/index.html`.

## 1. Kết quả sau khi tách

Tên project dùng đúng theo source hiện tại:

```text
MetasysPoc.sln
├─ BmsIngestionApp/BMS.IngestionApp.csproj       Presentation / executable
├─ BMS.Ingestion.Business/                       use case, ports, worker
├─ BMS.Ingestion.Common/                         configuration dùng chung
├─ BMS.Ingestion.Domain/                         model nghiệp vụ thuần
└─ BMS.Ingestion.DataAccess/                     HTTP/SSE và SQL adapters
```

Luồng runtime không đổi:

```text
FakeMetasysApi :5100
  └─ catalog JSON + COV SSE
          ↓
BMS.IngestionApp :5200
  ├─ Presentation khởi động host và expose status API
  ├─ Business điều phối catalog → subscribe → event
  └─ DataAccess ghi SQL
          ↓
FM_Central.raw.bms_building
FM_Central.raw.bms_equipment
FM_Central.raw.bms_reading
```

Không tạo solution ingestion riêng và không tách HTTP/SQL thành hai DataAccess
projects. Cả hai adapter nằm trong một project `BMS.Ingestion.DataAccess`, được
phân biệt bằng class/folder `Services`.

## 2. Hiểu vai trò từng layer

| Project | Chứa gì | Không được chứa |
| --- | --- | --- |
| `BMS.Ingestion.Domain` | `CovEvent`, Building, Equipment, Point, subscription models | SQL, HTTP, ASP.NET, worker |
| `BMS.Ingestion.Common` | `AppSettings`, `MetasysSettings`, `SqlSettings`, runtime options | SQL command, HTTP call, business workflow |
| `BMS.Ingestion.Business` | ports, `CovIngestionWorker`, status use case | `SqlConnection`, JSON/SSE implementation |
| `BMS.Ingestion.DataAccess` | `MetasysClient`, `BmsReadingRepository` | endpoint, UI, app startup |
| `BMS.IngestionApp` | `Program`, endpoint, DI/composition root, appsettings | SQL query và SSE parser |

Quy tắc dependency:

```text
BMS.IngestionApp
  ├─→ DataAccess ─→ Business ─→ Domain
  │       ├───────────────┬───→ Common
  │       └───────────────└───→ Domain
  ├─→ Business ───────────────→ Common + Domain
  └─→ Common + Domain

Common → không reference project nội bộ
Domain → không reference project nội bộ
```

Thực tế các project reference được phép:

- `Business -> Common + Domain`
- `DataAccess -> Business + Common + Domain`
- `BMS.IngestionApp -> Business + Common + Domain + DataAccess`
- `Domain` không reference project nội bộ nào.

Ý nghĩa quan trọng: Business khai báo nhu cầu qua interface; DataAccess implement nhu
cầu đó. Business không biết dữ liệu đang đến từ HTTP hay được lưu bằng SQL Server.

## 3. Hiểu ba bảng raw trước khi đọc code

### 3.1 `raw.bms_building`

Một row là một Building trong catalog nguồn.

| Column | Ý nghĩa | Ghi chú |
| --- | --- | --- |
| `building_code` | Mã ổn định như `BLD001` | Primary key; dùng để upsert |
| `name` | Tên hiển thị | Có thể đổi mà không đổi identity |
| `source_building` | Mã/tên từ source | Truy vết nguồn |
| `description` | Mô tả | Có thể null |
| `source_updated_at` | Source được cập nhật lúc nào | Khác thời gian ingest |
| `ingested_at` | SQL nhận record lúc nào | SQL điền UTC |

```sql
SELECT building_code, name, source_building,
       source_updated_at, ingested_at
FROM FM_Central.raw.bms_building
ORDER BY building_code;
```

### 3.2 `raw.bms_equipment`

Một row là một Equipment và phải thuộc Building tồn tại.

| Column | Ý nghĩa | Ghi chú |
| --- | --- | --- |
| `equipment_code` | Mã ổn định của thiết bị | Primary key |
| `name` | Tên thiết bị | Có thể đổi |
| `equipment_type` | Loại thiết bị | meter, sensor... |
| `building_code` | Building chứa thiết bị | Foreign key |
| `source_updated_at` | Source cập nhật lúc nào | Catalog freshness |
| `ingested_at` | SQL nhận lúc nào | UTC |

```sql
SELECT e.equipment_code, e.name AS equipment_name,
       e.equipment_type, e.building_code, b.name AS building_name
FROM FM_Central.raw.bms_equipment e
JOIN FM_Central.raw.bms_building b
  ON b.building_code = e.building_code;
```

Repository ghi Building trước rồi mới ghi Equipment trong cùng transaction để giữ
quan hệ này hợp lệ.

### 3.3 `raw.bms_reading`

Một row là một COV event. Bảng này là append-only history, không phải chỉ current state.

| Column | Ý nghĩa | Ghi chú |
| --- | --- | --- |
| `id` | SQL identity tăng dần | Primary key kỹ thuật |
| `object_id` | Mã point từ Metasys | Identity nghiệp vụ |
| `object_name`, `object_type` | Thông tin point | Snapshot tại lúc nhận |
| `building`, `equipment_code` | Ngữ cảnh event | Giúp nối catalog |
| `reading_time` | Event thật sự xảy ra lúc nào | Chọn current state bằng cột này |
| `reading_value` | Giá trị đo | `DECIMAL(18,4)` |
| `unit` | Đơn vị | Ví dụ `C`, `L/s` |
| `source_system` | Nguồn event | `Fake Metasys COV` trong POC |
| `ingested_at` | SQL nhận event lúc nào | Có thể trễ hơn event time |

```sql
SELECT TOP (20) id, object_id, reading_time,
       reading_value, unit, ingested_at
FROM FM_Central.raw.bms_reading
WHERE object_id = 'TEMP-001'
ORDER BY reading_time DESC, id DESC;
```

`reading_time` quyết định event mới hơn; `id` chỉ phá hòa khi timestamp bằng nhau.
Không dùng `MAX(id)` để kết luận Dataverse backlog đã giao xong.

## 4. Bản đồ file cũ sang file mới

| File ban đầu trong `BmsIngestionApp` | File mới | Tại sao tách |
| --- | --- | --- |
| `Models/CovEvent.cs` | `BMS.Ingestion.Domain/Models/CovEvent.cs` | Event là khái niệm nghiệp vụ thuần |
| `Models/MetasysCatalog.cs` | `BMS.Ingestion.Domain/Models/BmsCatalog.cs` | Building/Equipment/Point là domain models |
| `Models/AppSettings.cs` | `BMS.Ingestion.Common/Configuration/AppSettings.cs` | Configuration được nhiều layer dùng |
| `Models/IngestionStatus.cs` | `BMS.Ingestion.Business/Models/IngestionStatus.cs` | Status thuộc use case ingestion |
| `Abstractions/IMetasysClient.cs` | `BMS.Ingestion.Business/Abstractions/IMetasysClient.cs` | Business sở hữu source port |
| `Abstractions/IBmsReadingRepository.cs` | `BMS.Ingestion.Business/Abstractions/IBmsReadingRepository.cs` | Business sở hữu persistence port |
| `Services/CovIngestionWorker.cs` | `BMS.Ingestion.Business/Services/CovIngestionWorker.cs` | Worker điều phối use case |
| `Services/IngestionStatusTracker.cs` | `BMS.Ingestion.Business/Services/IngestionStatusTracker.cs` | Theo dõi trạng thái use case |
| `Services/MetasysClient.cs` | `BMS.Ingestion.DataAccess/Services/MetasysClient.cs` | HTTP/JSON/SSE là infrastructure |
| `Services/BmsReadingRepository.cs` | `BMS.Ingestion.DataAccess/Services/BmsReadingRepository.cs` | SQL là infrastructure |
| `Program.cs`, `Endpoints/`, `Hosting/` | giữ tại `BmsIngestionApp` | Presentation và composition root |

## 5. Làm lại từng bước

### Bước 0 — baseline trước khi move

```powershell
dotnet build .\MetasysPoc.sln -c Release -p:SignAssembly=false
dotnet test .\tests\MetasysPoc.Tests\MetasysPoc.Tests.csproj -c Release
```

Mục đích: nếu sau khi move bị lỗi, ta phân biệt được lỗi refactor với lỗi đã tồn tại.
Ghi lại invariant: port 5100/5200, API route, SQL schema, decimal precision, thứ tự
catalog trước subscription và `--no-sql` đều phải giữ nguyên.

### Bước 1 — dùng đúng class library .NET 10

Các project đã được tạo sẵn, SDK-style, target `net10.0`:

```powershell
Get-ChildItem .\BMS.Ingestion.*\*.csproj
dotnet sln .\MetasysPoc.sln list
```

Nếu tự tạo lại từ đầu:

```powershell
dotnet new classlib -n BMS.Ingestion.Domain -f net10.0
dotnet new classlib -n BMS.Ingestion.Common -f net10.0
dotnet new classlib -n BMS.Ingestion.Business -f net10.0
dotnet new classlib -n BMS.Ingestion.DataAccess -f net10.0
```

Không dùng template `.NET Framework 4.6.2`. Bốn skeleton cũ tên `BmsIngestion.*`
chỉ có `Class1.cs` đã được loại bỏ; chúng không phải bốn project hiện tại.

### Bước 2 — thêm project references đúng chiều

```powershell
dotnet add .\BMS.Ingestion.Business reference .\BMS.Ingestion.Common
dotnet add .\BMS.Ingestion.Business reference .\BMS.Ingestion.Domain

dotnet add .\BMS.Ingestion.DataAccess reference .\BMS.Ingestion.Business
dotnet add .\BMS.Ingestion.DataAccess reference .\BMS.Ingestion.Common
dotnet add .\BMS.Ingestion.DataAccess reference .\BMS.Ingestion.Domain

dotnet add .\BmsIngestionApp\BMS.IngestionApp.csproj reference .\BMS.Ingestion.Business
dotnet add .\BmsIngestionApp\BMS.IngestionApp.csproj reference .\BMS.Ingestion.Common
dotnet add .\BmsIngestionApp\BMS.IngestionApp.csproj reference .\BMS.Ingestion.Domain
dotnet add .\BmsIngestionApp\BMS.IngestionApp.csproj reference .\BMS.Ingestion.DataAccess
```

Business cần hosting/logging abstractions; DataAccess cần HTTP factory và SqlClient:

```powershell
dotnet add .\BMS.Ingestion.Business package Microsoft.Extensions.Hosting.Abstractions --version 10.0.0
dotnet add .\BMS.Ingestion.Business package Microsoft.Extensions.Logging.Abstractions --version 10.0.0
dotnet add .\BMS.Ingestion.DataAccess package Microsoft.Extensions.Http --version 10.0.0
dotnet add .\BMS.Ingestion.DataAccess package Microsoft.Data.SqlClient --version 7.0.2
```

### Bước 3 — tách Domain trước

Move models không phụ thuộc SDK vào `BMS.Ingestion.Domain/Models` và đổi namespace:

```csharp
namespace BMS.Ingestion.Domain.Models;

public sealed class CovEvent
{
    public string ObjectId { get; init; } = "";
    public decimal CurrentValue { get; init; }
    public DateTime Timestamp { get; init; }
}
```

Tại sao làm trước: các layer sau đều cần models. Domain ở đáy dependency nên move nó
trước giúp compiler chỉ ra tất cả `using` cần sửa mà không tạo dependency vòng.

Không đổi property, JSON shape, kiểu `decimal` hoặc timestamp trong bước này.

### Bước 4 — tách Common configuration

Move settings sang `BMS.Ingestion.Common/Configuration/AppSettings.cs`:

```csharp
namespace BMS.Ingestion.Common.Configuration;

public sealed class AppSettings
{
    public MetasysSettings Metasys { get; init; } = new();
    public SqlSettings Sql { get; init; } = new();
}

public sealed record IngestionRuntimeOptions(bool SqlEnabled);
```

`AppSettings` là configuration, không phải business entity. `IngestionRuntimeOptions`
tách trạng thái command-line `--no-sql` khỏi cấu hình file để test dễ hơn.

### Bước 5 — tạo Business ports

Business khai báo điều nó cần, không khai báo cách HTTP/SQL thực hiện:

```csharp
public interface IMetasysClient
{
    Task<MetasysCatalog> ReadCatalogAsync(CancellationToken ct);
    Task<SubscriptionResponse> SubscribeAsync(
        IEnumerable<string> objectIds, CancellationToken ct);
    IAsyncEnumerable<CovEvent> ReadEventsAsync(
        string subscriptionId, CancellationToken ct);
}

public interface IBmsReadingRepository
{
    Task PersistCatalogAsync(
        IReadOnlyList<BmsBuilding> buildings,
        IReadOnlyList<BmsEquipment> equipment,
        CancellationToken cancellationToken = default);
    Task InsertAsync(CovEvent covEvent,
        CancellationToken cancellationToken = default);
}
```

Ý nghĩa: unit test có thể truyền fake implementation. Sau này đổi SQL Server thành hệ
lưu trữ khác thì worker không cần biết.

### Bước 6 — move orchestration vào Business

`CovIngestionWorker` giữ đúng thứ tự:

```text
ReadCatalogAsync
  → PersistCatalogAsync (nếu SQL enabled)
  → SubscribeAsync
  → ReadEventsAsync
  → InsertAsync từng event (nếu SQL enabled)
```

Business không được import SQL/HTTP implementation. Kiểm tra:

```powershell
rg -n "SqlConnection|Microsoft.Data.SqlClient|System.Net.Http.Json|HttpClient" `
  .\BMS.Ingestion.Business .\BMS.Ingestion.Common .\BMS.Ingestion.Domain -g "*.cs"
```

Kết quả mong đợi: không có match.

### Bước 7 — move HTTP/SSE và SQL vào DataAccess

`MetasysClient : IMetasysClient` sở hữu:

- URL tương đối của catalog/subscription;
- JSON serialization;
- `HttpCompletionOption.ResponseHeadersRead`;
- parse SSE line bắt đầu bằng `data:`;
- dispose response, stream và kiểm tra cancellation.

Giữ đoạn này vì stream có thể đã buffer line khi cancellation được yêu cầu:

```csharp
ct.ThrowIfCancellationRequested();
```

`BmsReadingRepository : IBmsReadingRepository` sở hữu:

- `SqlConnection`, command và parameters;
- transaction catalog;
- Building được upsert trước Equipment;
- validation code trùng/Building thiếu;
- `DECIMAL(18,4)` và append-only insert của reading.

Không chuyển SQL string vào worker hoặc endpoint.

### Bước 8 — để Presentation chỉ compose ứng dụng

`BmsIngestionApp` còn:

```text
Program.cs
Endpoints/IngestionEndpoints.cs
Hosting/ServiceCollectionExtensions.cs
appsettings.json
```

DI mapping interface sang implementation:

```csharp
services.AddSingleton<IngestionStatusTracker>();
services.AddSingleton<IBmsReadingRepository>(
    _ => new BmsReadingRepository(settings.Sql.ConnectionString));
services.AddHttpClient(MetasysClient.ClientName, client =>
{
    client.BaseAddress = new Uri(settings.Metasys.BaseUrl, UriKind.Absolute);
    client.Timeout = Timeout.InfiniteTimeSpan;
});
services.AddSingleton<IMetasysClient, MetasysClient>();
services.AddHostedService<CovIngestionWorker>();
```

- `Singleton` tracker: endpoint và worker nhìn cùng một trạng thái.
- Named `HttpClient`: cấu hình source URL ở composition root.
- `HostedService`: worker tự chạy theo lifecycle của ASP.NET host.
- Endpoint chỉ đọc status/recent event, không gọi SQL trực tiếp.

### Bước 9 — sửa tests theo namespace mới

Tests reference trực tiếp project chứa type:

```powershell
dotnet add .\tests\MetasysPoc.Tests reference .\BMS.Ingestion.Domain
dotnet add .\tests\MetasysPoc.Tests reference .\BMS.Ingestion.Common
dotnet add .\tests\MetasysPoc.Tests reference .\BMS.Ingestion.Business
dotnet add .\tests\MetasysPoc.Tests reference .\BMS.Ingestion.DataAccess
```

Chỉ sửa `using`; không nới assertion để test pass. `CovIngestionWorkerTests` tiếp tục
kiểm tra thứ tự orchestration. `MetasysClientTests` tiếp tục kiểm tra JSON/SSE,
precision, disposal và cancellation.

### Bước 10 — build và test toàn solution

```powershell
dotnet build .\MetasysPoc.sln -c Release -p:SignAssembly=false
dotnet test .\tests\MetasysPoc.Tests\MetasysPoc.Tests.csproj -c Release --no-build
git diff --check
```

Receipt tại thời điểm triển khai:

```text
Build: 4 BMS projects + BMS.IngestionApp thành công
Warnings: 0
Errors: 0
Tests: 35 passed, 0 failed, 0 skipped
```

### Bước 11 — smoke test không ghi SQL

Terminal 1:

```powershell
dotnet run --project .\FakeMetasysApi -c Release --no-build
```

Terminal 2:

```powershell
dotnet run --project .\BmsIngestionApp\BMS.IngestionApp.csproj `
  -c Release --no-build -- --no-sql
```

Terminal 3:

```powershell
Invoke-RestMethod http://localhost:5200/api/ingestion/status
Invoke-RestMethod http://localhost:5200/api/ingestion/events/recent
```

Receipt đã kiểm tra:

```text
State          = Listening
SqlEnabled     = False
SubscriptionId = SUB-001
EventsReceived = 32
RowsInserted   = 0
RecentCount    = 25
```

Điều này chứng minh Presentation + Business + HTTP/SSE adapter chạy xuyên suốt mà
không tạo SQL side effect.

### Bước 12 — smoke test có SQL

Bỏ `--no-sql`, bảo đảm connection string local hợp lệ, rồi kiểm tra:

```sql
SELECT COUNT(*) FROM FM_Central.raw.bms_building;
SELECT COUNT(*) FROM FM_Central.raw.bms_equipment;
SELECT TOP (20) *
FROM FM_Central.raw.bms_reading
ORDER BY reading_time DESC, id DESC;
```

Không cần chạy `DataverseSyncWorker` để chứng minh refactor này. SQL-to-Dataverse là
service độc lập và nằm ngoài scope.

## 6. Cách đọc code mới từ đầu đến cuối

Đọc theo thứ tự này để không bị rối:

1. `BMS.Ingestion.Domain/Models/CovEvent.cs`: dữ liệu đi qua hệ thống là gì.
2. `BMS.Ingestion.Business/Abstractions/*.cs`: Business yêu cầu source/storage làm gì.
3. `BMS.Ingestion.Business/Services/CovIngestionWorker.cs`: luồng use case.
4. `BMS.Ingestion.DataAccess/Services/MetasysClient.cs`: HTTP/SSE đáp ứng source port.
5. `BMS.Ingestion.DataAccess/Services/BmsReadingRepository.cs`: SQL đáp ứng persistence port.
6. `BmsIngestionApp/Hosting/ServiceCollectionExtensions.cs`: nối interface với class thật.
7. `BmsIngestionApp/Endpoints/IngestionEndpoints.cs`: UI/API quan sát trạng thái.
8. Tests: xác nhận behavior không đổi sau refactor.

## 7. Ngoài phạm vi

- Không đổi SQL schema hoặc xóa raw history.
- Không đổi deterministic identity/delivery ledger của `DataverseSyncWorker`.
- Không provision/deploy Dataverse.
- Không triển khai Azure hosting.
- Không chứng minh kết nối Johnson Controls Metasys thật; `FakeMetasysApi` vẫn là simulator.

## 8. Definition of Done

- Tất cả project dùng đúng prefix/name `BMS.Ingestion.*`.
- Không còn source runtime dưới `BmsIngestionApp/Models`, `Services`, `Abstractions`.
- Dependency đi đúng chiều; Business/Domain/Common không biết DataAccess.
- Build Release thành công, không warning/error.
- 35 tests pass.
- `--no-sql` nhận COV nhưng `RowsInserted = 0`.
- SQL mode giữ catalog transaction, `DECIMAL(18,4)` và append-only history.
- Port, endpoint, source identity và SQL schema không đổi.
