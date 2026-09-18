# Refactor Fake BMS services

Trạng thái: **đã triển khai local ngày 2026-09-18**

Phạm vi: `BMS.Fake.App` và contract dùng chung với `BMS.Ingestion.App`.
Port `5100`, URL, JSON shape và SSE contract được giữ nguyên.

## 1. Vì sao cần tách

Trước refactor, `BMS.Fake.App` có các model riêng trong app.
Trong khi đó `BMS.Ingestion.App` cũng có bản sao của `CovEvent`, `MetasysPoint`,
`BmsBuilding`, `BmsEquipment` và subscription models.

Khi cùng một contract có hai bản sao:

- sửa property ở một bên có thể làm client deserialize sai;
- test phải tham chiếu sai namespace;
- fixture dữ liệu bị trộn với state runtime;
- khó phân biệt dữ liệu nguồn giả lập với logic phát event.

Sau refactor, contract chung nằm trong `BMS.Ingestion.Domain`; Fake API chỉ giữ
fixture và các service mô phỏng hành vi vendor.

## 2. Cấu trúc sau refactor

```text
BMS.Ingestion.Domain/Models
├─ BmsCatalog.cs
│  ├─ BmsBuilding
│  ├─ BmsEquipment
│  ├─ MetasysPoint
│  └─ MetasysCatalog
└─ CovEvent.cs
   ├─ CovEvent
   ├─ SubscriptionRequest
   └─ SubscriptionResponse

BMS.Fake.DataAccess
├─ Simulation/MetasysFixture.cs
├─ Services/MetasysPointStore.cs
└─ Services/SubscriptionManager.cs

BMS.Fake.Business
└─ Services/MetasysSimulator.cs

BMS.Fake.App
├─ Endpoints/MetasysEndpoints.cs
├─ Hosting/ServiceCollectionExtensions.cs
└─ Program.cs
```

Luồng chạy:

```text
MetasysFixture
  └─ tạo catalog/point ban đầu
          ↓
MetasysPointStore
  └─ giữ mutable value + timestamp trong memory
          ↓
MetasysSimulator
  └─ tạo CovEvent theo timer
          ↓
SubscriptionManager
  └─ publish event vào channel đúng objectId
          ↓
MetasysEndpoints
  └─ serialize JSON hoặc SSE
```

## 3. Vai trò từng file

### `BMS.Ingestion.Domain/Models/BmsCatalog.cs`

Đây là contract dùng chung giữa producer và consumer.

`MetasysPoint` giữ metadata và current state:

```csharp
public sealed record MetasysPoint(string ObjectId)
{
    public string ObjectName { get; init; } = "";
    public string ObjectType { get; init; } = "";
    public string Building { get; init; } = "";
    public string EquipmentCode { get; init; } = "";
    public decimal Value { get; set; }
    public string Unit { get; init; } = "";
    public DateTime Timestamp { get; set; }

    public MetasysPoint Copy() => this with { };
}
```

- `init` bảo vệ metadata sau khi tạo object.
- `set` cho phép simulator thay đổi giá trị và timestamp.
- `Copy()` ngăn endpoint trả reference tới object mutable bên trong store.
- `record` giúp tạo bản sao bằng `with` và giữ equality thuận tiện cho test.

`CovEvent` là event immutable theo hướng dữ liệu: mỗi lần thay đổi tạo một object
mới, chứa `PreviousValue`, `CurrentValue` và `Timestamp`.

### `BMS.Fake/BMS.Fake.DataAccess/Simulation/MetasysFixture.cs`

Đây là dữ liệu nguồn cố định của simulator:

```csharp
public static IReadOnlyList<BmsBuilding> Buildings { get; } = [ ... ];
public static IReadOnlyList<BmsEquipment> Equipment { get; } = [ ... ];
public static Dictionary<string, MetasysPoint> CreatePoints() => ...;
```

Fixture không có lock, không publish event và không chứa timer. Vì vậy muốn tạo
scenario test mới chỉ cần đổi fixture, không phải sửa `MetasysPointStore`.

### `BMS.Fake/BMS.Fake.DataAccess/Services/MetasysPointStore.cs`

Store quản lý state runtime:

- trả catalog Building/Equipment;
- đọc bản copy của một hoặc tất cả point;
- kiểm tra object ID tồn tại;
- thay đổi value;
- tạo `CovEvent` với giá trị cũ/mới.

Đoạn lock có ý nghĩa vì `MetasysSimulator` và HTTP request chạy trên các thread khác
nhau:

```csharp
lock (_sync)
{
    var previousValue = point.Value;
    point.Value = newValue;
    point.Timestamp = timestamp;
    return new CovEvent { ... };
}
```

Nếu bỏ lock, request đọc point có thể thấy metadata/state giữa chừng khi simulator
đang cập nhật.

### `BMS.Fake/BMS.Fake.Business/Services/MetasysSimulator.cs`

Đây là `BackgroundService` tạo event giả lập theo `Simulator:IntervalSeconds`.

```text
timer tick
  → lấy snapshot các points
  → tính delta theo ObjectType
  → bỏ qua delta = 0
  → store.ChangeValue(...)
  → subscriptions.Publish(covEvent)
```

`delta = 0` với Temperature mô phỏng trường hợp không phát COV khi giá trị không
thay đổi. Đây là behavior của simulator, không phải bằng chứng về thuật toán COV
của Johnson Controls Metasys thật.

### `BMS.Fake/BMS.Fake.DataAccess/Services/SubscriptionManager.cs`

Mỗi subscription có:

- ID dạng `SUB-001`;
- tập object IDs;
- một `Channel<CovEvent>` để stream event.

`Publish` duyệt các subscription và chỉ ghi event vào channel có object ID tương ứng.
Endpoint SSE đọc channel bằng `ReadAllAsync(context.RequestAborted)`, nên client
đóng connection sẽ dừng stream.

### `BMS.Fake/BMS.Fake.App/Endpoints/MetasysEndpoints.cs`

Endpoint vẫn nằm trong Fake API vì đây là Presentation/transport layer:

| Route | Vai trò |
| --- | --- |
| `GET /api/metasys/buildings` | đọc catalog Building |
| `GET /api/metasys/equipment` | đọc catalog Equipment |
| `GET /api/metasys/objects` | đọc snapshot tất cả point |
| `GET /api/metasys/objects/{objectId}` | đọc một point |
| `POST /api/metasys/subscriptions` | validate object IDs và tạo subscription |
| `GET /api/metasys/subscriptions/{id}/stream` | trả SSE COV |

Endpoint không tự tạo fixture, không tự đổi point và không tự tính delta. Nó chỉ
gọi service rồi chuyển kết quả thành JSON/SSE.

## 4. Các bước tự làm lại

### Bước 1 — dùng shared Domain contract

Thêm reference từ Fake API tới Domain:

```powershell
dotnet add .\BMS.Fake\BMS.Fake.App\BMS.Fake.App.csproj reference `
  .\BMS.Ingestion\BMS.Ingestion.Domain\BMS.Ingestion.Domain.csproj
```

Sau đó đổi các model namespace cũ thành:

```csharp
using BMS.Ingestion.Domain.Models;
```

Xóa các model trùng trong app sau khi compiler không còn dùng.

### Bước 2 — tách fixture khỏi state

Di chuyển các mảng Building/Equipment và method `NewPoint` sang
`Simulation/MetasysFixture.cs`. `MetasysPointStore` chỉ còn giữ dictionary và
operation đọc/thay đổi.

Checkpoint:

```powershell
rg -n "Building A|WATER-001|NewPoint" .\BMS.Fake\BMS.Fake.DataAccess\Services
```

Kết quả mong đợi: `MetasysPointStore.cs` không còn hard-code fixture.

### Bước 3 — bảo vệ mutable state

Giữ `lock (_sync)` trong `Get`, `GetAll`, `Exists` và `ChangeValue`.
Trả `Copy()` từ `Get`/`GetAll`, không trả object trong dictionary.

Checkpoint: sửa object trả về từ `Get` trong test sẽ không làm thay đổi state store.

### Bước 4 — giữ transport contract

Không đổi:

- port `5100`;
- route `/api/metasys/...`;
- JSON property names;
- `text/event-stream`;
- dòng `data: {json}`;
- `retry: 3000`;
- subscription ID dạng `SUB-001`.

### Bước 5 — thêm test trước khi sửa simulator behavior

Test tối thiểu:

```csharp
[Fact]
public void Point_store_emits_previous_and_current_value() { ... }

[Fact]
public async Task Subscription_publishes_only_matching_object() { ... }
```

Không test bằng cách chờ timer nếu không cần; timer test dễ flaky. Test trực tiếp
`ChangeValue` và `Publish` sẽ nhanh, deterministic và chỉ kiểm tra đúng trách nhiệm
của từng service.

## 5. Cách test thủ công

Terminal 1:

```powershell
dotnet run --project .\BMS.Fake\BMS.Fake.App\BMS.Fake.App.csproj
```

Mở Swagger:

```text
http://localhost:5100/swagger
```

Kiểm tra catalog:

```powershell
Invoke-RestMethod http://localhost:5100/api/metasys/buildings
Invoke-RestMethod http://localhost:5100/api/metasys/equipment
Invoke-RestMethod http://localhost:5100/api/metasys/objects
Invoke-RestMethod http://localhost:5100/api/metasys/objects/WATER-001
```

Tạo subscription:

```powershell
$body = @{ objectIds = @('WATER-001', 'TEMP-001') } | ConvertTo-Json
Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5100/api/metasys/subscriptions `
  -ContentType 'application/json' `
  -Body $body
```

Kết quả kỳ vọng:

```json
{
  "subscriptionId": "SUB-001"
}
```

Nếu gửi object ID không tồn tại, API phải trả `400` với `unknownObjectIds`.

Chạy full flow:

```powershell
dotnet run --project .\BMS.Ingestion\BMS.Ingestion.App\BMS.Ingestion.App.csproj -- --no-sql
Invoke-RestMethod http://localhost:5200/api/ingestion/status
```

Kỳ vọng: `State = Listening`, `EventsReceived` tăng dần, `RowsInserted = 0`.

## 6. Verification receipt

Đã kiểm tra sau refactor ngày 2026-09-18:

```text
Build MetasysPoc.sln : 0 warnings, 0 errors
Tests                 : 37 passed, 0 failed, 0 skipped
Fake API objects      : 5
Fake API port         : 5100
Ingestion status      : Listening
Events received       : 23 trong smoke test
Rows inserted         : 0 với --no-sql
```

## 7. Ngoài phạm vi

- Không mô phỏng authentication hoặc network failure của vendor thật.
- Không khẳng định fixture này giống toàn bộ Metasys API production.
- Không đưa timer simulator vào SQL trực tiếp.
- Không tạo một service Dataverse trong Fake API.
