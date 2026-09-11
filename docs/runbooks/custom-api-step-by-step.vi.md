# Custom API lab: từ code local đến nút trong Model-driven app

Tài liệu này giải thích implementation thật của `fmc_RequestBmsSync` trong repo. Bản
tương tác nằm tại `docs/interactive/custom-api-lab`: nội dung bài học ở bên trái,
source có số dòng ở bên phải. Click một dòng để xem dòng đó làm gì.

> Phạm vi an toàn: lab và simulator chạy local, không đăng nhập và không gọi
> Dataverse. Chỉ các lệnh có nhãn **ghi cloud** mới thay đổi Developer environment.

## 1. Hiểu bài toán trước khi viết code

Người dùng bấm **Request BMS Sync** trong `FMC BMS Demo`. App không được kết nối SQL
trực tiếp. Nó gọi một Dataverse Custom API; main operation plug-in tạo hoặc dùng lại
một row `fmc_syncrequest`. `DataverseSyncWorker` mới là tiến trình claim row đó, đọc
`FM_Central` và đồng bộ dữ liệu.

```text
Model-driven app / caller
  -> POST /api/data/v9.2/fmc_RequestBmsSync
  -> Dataverse Custom API contract
  -> FMCentralBms.Plugins.RequestBmsSync
  -> create/reuse fmc_syncrequest
  -> response: RequestId, Created, Status, Message
  -> Business Event -> Power Automate demo

DataverseSyncWorker (chạy độc lập)
  -> claim request -> đọc SQL -> ghi point/history -> complete request
```

Custom API là một message do solution định nghĩa. Plug-in là code xử lý message đó.
Nút là một caller. Flow là subscriber của business event. Bốn thành phần liên quan
nhưng không thay thế nhau.

| Thành phần | Trách nhiệm | Không làm |
| --- | --- | --- |
| Custom API | Định nghĩa tên message, input, output, quyền execute | Không tự có business logic |
| `RequestBmsSync` plug-in | Validate input, chống request trùng, tạo queue row | Không đọc SQL |
| App web resource | Sinh GUID, `POST`, hiển thị response | Không tự sync dữ liệu |
| Power Automate | Nhận business event để demo/orchestrate | Không xử lý từng BMS reading |
| Worker | Claim queue và chạy SQL-to-Dataverse | Không cần người dùng giữ browser mở |

## 2. Đọc contract của `fmc_RequestBmsSync`

Contract đã triển khai là global unbound Action:

| Thuộc tính | Giá trị | Ý nghĩa |
| --- | --- | --- |
| Unique name | `fmc_RequestBmsSync` | Endpoint và `context.MessageName` phải khớp chính xác |
| Binding | Global / `0` | Không gắn với một record hay table cụ thể |
| Action | `isfunction=false` | Gọi bằng HTTP `POST`, vì có side effect |
| Private | `false` | Caller bên ngoài plug-in assembly có thể gọi nếu có quyền |
| Workflow enabled | `true` | Cho phép dùng trong Power Automate |
| Processing steps | Async Only / `1` | Cho phép subscriber bất đồng bộ cho business event |
| Execute privilege | `prvCreatefmc_syncrequest` | Caller phải có quyền tạo Sync Request |
| Main operation | `FMCentralBms.Plugins.RequestBmsSync` | Class C# xử lý message |

Input duy nhất là `ClientRequestId` kiểu String, optional. Giá trị nên là GUID. Caller
giữ nguyên GUID khi retry để plug-in trả lại cùng request thay vì tạo request khác.

Output:

| Name | Dataverse type | Giá trị |
| --- | --- | --- |
| `RequestId` | Guid (`12`) | Primary ID của `fmc_syncrequest` |
| `Created` | Boolean (`0`) | `true` khi lần gọi này tạo row |
| `Status` | Integer (`7`) | `789100000` Queued hoặc trạng thái row được reuse |
| `Message` | String (`10`) | Mô tả ngắn nhánh xử lý |

Tên parameter phân biệt hoa thường trong code. `ClientRequestId` khác
`clientRequestId`; `RequestId` khác `requestid`.

## 3. Những file cần tạo hoặc copy ở local

Khi làm một Custom API tương tự, không chỉ copy một DLL. Cần hiểu mỗi artifact:

```text
plugins/FMCentralBms.Plugins/
  RequestBmsSync.cs                 # business handler
  FMCentralBms.Plugins.csproj       # net48, signing, assembly version
  FMCentralBms.Plugins.snk          # private signing key; không đưa vào docs/download

DataverseSyncWorker/
  Program.cs                        # route --register-plugin
  Services/DataversePluginProvisioner.cs
                                      # upload DLL + tạo Custom API/parameters
  Services/DataverseProvisioner.cs  # schema fmc_syncrequest + active key

dataverse/app-source/
  BmsEventDemo.html                 # UI gọi endpoint

scripts/
  Invoke-BmsEventDemo.ps1           # deploy/verify/test UI + flow demo

dataverse/FMCentralBms/
  customapis/...                    # source export từ solution
  WebResources/...                  # exported page
  Workflows/...                     # exported cloud flow
```

Để tạo API mới trong cùng assembly, thường copy một `.cs` handler làm mẫu rồi đổi:
class, message constant, input/output contract và logic. Không copy `.snk` ra project
khác nếu chưa quyết định assembly identity. Không upload `.pdb`, `.deps.json`, project
folder hoặc cả thư mục `publish`; registration cần đúng assembly package/DLL mà project
sinh ra.

DLL của project này sau Release build nằm ở:

```text
plugins/FMCentralBms.Plugins/bin/Release/net48/FMCentralBms.Plugins.dll
```

Thư mục `publish` có thể tồn tại do Power Apps MSBuild target. Path trong Visual Studio
Build Output mới là bằng chứng file vừa build nằm ở đâu.

## 4. Đọc từng dòng `RequestBmsSync.cs`

Mở tab **Đọc code** của interactive lab và chọn `RequestBmsSync.cs`. Lab lấy trực tiếp
source hiện tại, đánh số đủ 132 dòng và có giải thích cho từng dòng, kể cả dấu ngoặc.

Các vùng logic chính:

| Dòng | Vai trò |
| --- | --- |
| 1–4 | Import .NET, WCF fault, Dataverse SDK và query API |
| 6–17 | Namespace, class và các constant của message/table/status |
| 19–34 | Lấy tracing/context/service factory, kiểm tra message, tạo correlation/pipeline |
| 36–42 | Idempotency theo `ClientRequestId` |
| 44–50 | Chỉ cho một request Queued/Running trên cùng pipeline |
| 52–68 | Tạo row queue và trả response mới |
| 69–77 | Xử lý race bằng active alternate key |
| 80–92 | Parse và chuẩn hóa `ClientRequestId` |
| 94–103 | Query theo correlation ID |
| 105–116 | Query request đang active |
| 118–121 | Lấy row đầu hoặc `null` |
| 123–130 | Gán bốn output parameter |

Ba service lấy từ `serviceProvider` có nhiệm vụ khác nhau:

- `ITracingService`: ghi diagnostic line vào Plug-in Trace Log.
- `IPluginExecutionContext`: chứa message, user, organization, input và output.
- `IOrganizationServiceFactory`: tạo `IOrganizationService` để query/create row.

`context.UserId` được dùng để tạo Organization Service, nên quyền Dataverse của caller
được tôn trọng. `context.InitiatingUserId` được lưu vào `fmc_requestedby`, cho biết ai
khởi phát request ban đầu.

## 5. Tại sao code chống trùng theo hai lớp

Lớp một là correlation ID. UI sinh một GUID cho lần bấm. Nếu HTTP timeout rồi UI/caller
retry cùng GUID, `FindByCorrelation` trả row đã tạo và `Created=false`.

Lớp hai là active pipeline. Dù hai caller dùng hai GUID khác nhau, nếu pipeline đã có
Queued hoặc Running request thì `FindActive` reuse request đó. Điều này tránh hai worker
run cùng mục tiêu `organizationId:FMC`.

Vẫn tồn tại race:

```text
Caller A: FindActive -> none
Caller B: FindActive -> none
Caller A: Create -> thắng active key
Caller B: Create -> OrganizationServiceFault
Caller B: FindActive -> row của A -> trả Created=false
```

Vì vậy `fmc_activekey` phải là alternate key trên `fmc_syncrequest`, và row active phải
giữ giá trị pipeline. Khi request hoàn tất, worker giải phóng active key theo contract
của `SyncRequestStore`; không được bỏ key chỉ để làm demo chạy qua.

`catch (FaultException<OrganizationServiceFault>)` chỉ biến fault thành reuse nếu sau
fault thật sự tìm thấy winner. Nếu không có winner, `throw` giữ nguyên lỗi để schema,
permission hoặc lỗi platform không bị che giấu.

## 6. Build, signing, version và chọn đúng DLL

Project plug-in target `net48` vì Dataverse sandbox nạp .NET Framework plug-in assembly.
`SignAssembly=true` và `AssemblyOriginatorKeyFile` tạo strong name. Full name gồm assembly
name, version, culture và public key token.

Build từ root:

```powershell
dotnet build .\plugins\FMCentralBms.Plugins\FMCentralBms.Plugins.csproj `
  --configuration Release
```

Kiểm tra file:

```powershell
Get-Item .\plugins\FMCentralBms.Plugins\bin\Release\net48\FMCentralBms.Plugins.dll |
  Select-Object FullName, Length, LastWriteTime
```

Khi cập nhật assembly đã đăng ký, tăng ít nhất build/revision trong
`AssemblyVersion`, ví dụ `1.0.0.2` thành `1.0.0.3`. Dataverse yêu cầu assembly fullname
unique khi tạo mới; không đăng ký một assembly mới có cùng name/public key rồi mong nó
tự thay bản cũ. Hãy update đúng existing assembly hoặc dùng helper idempotent.

Thông báo “fullnames must be unique (ignoring version build and revision)” thường nghĩa
là đang chọn **Register New Assembly** cho DLL vốn đã tồn tại. Dùng **Update** trong PRT
hoặc chạy `--register-plugin` của repo.

## 7. Tạo schema queue và active key

Handler phụ thuộc `fmc_syncrequest` và các column:

| Column | Dữ liệu được ghi |
| --- | --- |
| `fmc_name` | `SQL Sync <UTC ISO-8601>` |
| `fmc_command` | `DrainPending` |
| `fmc_pipeline` | `<organizationId>:FMC` |
| `fmc_activekey` | bằng pipeline khi request active |
| `fmc_correlationid` | GUID của caller hoặc GUID do plug-in sinh |
| `fmc_requestedby` | initiating user ID dạng text |
| `fmc_status` | `789100000` Queued |

Schema được quản lý trong `DataverseProvisioner`; không tạo column thủ công rồi quên
cập nhật provisioner. Với thay đổi schema đã được phép, quy trình là:

```powershell
dotnet run --project .\DataverseSyncWorker -- --provision
```

Lệnh này **ghi cloud**. Trước khi chạy phải xác nhận URL và organization đúng Developer.
Sau đó kiểm tra key status là Active. Local code định nghĩa key chưa chứng minh key đã
sẵn sàng trên Dataverse.

Active key là cơ chế consistency ở server. Query-before-create chỉ giúp đường đi bình
thường; key mới phân xử được hai request đến đồng thời.

## 8. Upload assembly và tạo Custom API bằng SDK helper

`DataversePluginProvisioner.Register` thực hiện theo thứ tự:

1. Resolve path và đọc assembly version.
2. Tìm `pluginassembly` tên `FMCentralBms.Plugins`.
3. Create hoặc update assembly bytes trong Sandbox/Database.
4. Đảm bảo plugin type `FMCentralBms.Plugins.RequestBmsSync` tồn tại.
5. Tạo hoặc kiểm tra `customapi` `fmc_RequestBmsSync`.
6. Tạo input `ClientRequestId` và bốn output properties.

Lệnh triển khai:

```powershell
dotnet run --project .\DataverseSyncWorker --configuration Release -- `
  --register-plugin `
  .\plugins\FMCentralBms.Plugins\bin\Release\net48\FMCentralBms.Plugins.dll
```

Đây là lệnh **ghi cloud**. Helper cố tình không chạy khi worker startup bình thường.
Các property immutable quan trọng như `bindingtype`, `isfunction`, `isprivate` và
`allowedcustomprocessingsteptype` được kiểm tra. Nếu contract cũ không tương thích,
helper dừng thay vì biến đổi message đang có caller phụ thuộc.

Custom API main operation không đăng ký như Create/Update step thông thường. Record
`customapi.plugintypeid` liên kết trực tiếp API với plugin type.

## 9. Hiểu code UI gọi Web API

`BmsEventDemo.html` chạy trong Model-driven app. Phần JavaScript quan trọng:

```javascript
const id = crypto.randomUUID();
const base = GetGlobalContext().getClientUrl();
const response = await fetch(base + '/api/data/v9.2/fmc_RequestBmsSync', {
  method: 'POST',
  credentials: 'same-origin',
  headers: {
    'Content-Type': 'application/json',
    'Accept': 'application/json',
    'OData-Version': '4.0',
    'OData-MaxVersion': '4.0'
  },
  body: JSON.stringify({ ClientRequestId: id })
});
```

`GetGlobalContext().getClientUrl()` lấy đúng organization URL của app đang mở; không
hard-code host. `same-origin` dùng session hiện tại của user. `POST` phù hợp Action có
side effect. Payload key phải trùng Unique Name của request parameter.

UI disable button trong lúc gửi, timeout sau 30 giây và luôn enable lại trong `finally`.
Timeout là kết quả chưa xác định, không đồng nghĩa server chưa tạo request. Vì vậy UI
hiển thị correlation GUID để người vận hành kiểm tra trước khi bấm lại.

## 10. Business Event và Power Automate

Để Custom API xuất hiện ở Dataverse business event trigger:

- `workflowsdkstepenabled=true`.
- `allowedcustomprocessingsteptype=1` (`Async Only`).
- API được assign vào Catalog và Category.
- Flow dùng `BusinessEventsTrigger` với `sdkmessagename=fmc_RequestBmsSync`.

Flow demo `FMC - App Request BMS Sync Event` chỉ Compose payload nhận được. Một run
Succeeded chứng minh tuyến:

```text
UI -> Custom API -> main operation plug-in -> business event -> Flow
```

Nó không chứng minh worker đã đọc SQL. Muốn kiểm tra end-to-end phải xem
`fmc_syncrequest`, worker log, SQL delivery ledger và dữ liệu point/history.

Không thiết kế một flow cho mỗi BMS reading. Ingestion và synchronization khối lượng
lớn vẫn thuộc .NET worker; business event ở đây đại diện cho command mức cao.

## 11. Deploy UI demo, verify và test

Script đã tách ba mode để tránh ghi cloud ngoài ý muốn:

```powershell
# Chỉ đọc cấu hình live
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-BmsEventDemo.ps1 -Mode Verify

# Gọi API hai lần với cùng ClientRequestId; có thể tạo queue row và Flow runs
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-BmsEventDemo.ps1 -Mode Test

# Tạo/cập nhật web resource, flow, catalog registration; ghi cloud
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Invoke-BmsEventDemo.ps1 -Mode Deploy
```

Test idempotency đạt khi hai response có cùng `RequestId`; lần retry có
`Created=false`. Trong UI, mở app `FMC BMS Demo` → **BMS Catalog** →
**Power Automate Demo** → **Request BMS Sync**.

Nếu worker tắt, request ở Queued là đúng. Chỉ start worker theo operating runbook khi
bạn thực sự muốn sync SQL; mở trang demo không tự start local process.

Sau thay đổi solution-aware, export/unpack lại `FMCentralBms` và review diff. File local
được tạo trước deployment không phải bằng chứng cloud đã đổi; export receipt cũng chỉ
là bằng chứng của thời điểm export.

## 12. Tracing và xử lý lỗi theo triệu chứng

Các marker của handler:

| Marker | Ý nghĩa |
| --- | --- |
| `START` | API vào handler, đã có pipeline/correlation |
| `REUSE_BY_ID` | Retry cùng `ClientRequestId` |
| `REUSE_ACTIVE` | Pipeline đã có Queued/Running request |
| `CREATED` | Tạo request mới |
| `RACE_REUSE` | Caller khác thắng active key |

Tracing giúp trả lời “đã vào plugin chưa?” và “đi nhánh nào?” mà không thay đổi output
contract. Không ghi access token, connection string hoặc payload nhạy cảm vào trace.

| Triệu chứng | Kiểm tra đầu tiên |
| --- | --- |
| HTTP 404 cho endpoint | Custom API Unique Name và publish/export |
| HTTP 400 `BMS-SYNC-001` | `ClientRequestId` có phải GUID string |
| HTTP 403 | caller có `prvCreatefmc_syncrequest` |
| `Created=false` | thường là idempotency/reuse, không phải lỗi |
| Nút timeout | tra correlation ID và Flow run trước khi retry |
| API xanh nhưng SQL chưa sync | worker có chạy/claim request không |
| Flow không có run | workflow On, catalog/category, business-event settings |
| assembly fullname unique | update assembly hiện có, không Register New |

Plug-in Trace Log cần được bật ở mức phù hợp trên Developer. `Download Log File` trong
model-driven error dialog không phải nguồn trace duy nhất; xem table Plug-in Trace Log
hoặc dùng tooling read-only. Tắt trace All khi không còn cần nếu environment có yêu cầu
dung lượng nghiêm ngặt.

## 13. Bài tập: tạo một Custom API tương tự

Ví dụ `fmc_RequestEquipmentHealthCheck` nhận `EquipmentId` và trả `JobId`, `Created`,
`Message`. Làm theo checklist:

1. Viết contract trước: global Action, input/output và privilege.
2. Tạo class `RequestEquipmentHealthCheck : IPlugin` trong assembly hiện có.
3. Validate `EquipmentId` là GUID; không tin input string.
4. Tạo/reuse một queue row, không chạy health check dài trong synchronous handler.
5. Thêm plugin type và Custom API vào `DataversePluginProvisioner`.
6. Build Release, tăng assembly version và update existing assembly.
7. Test handler branches local; deploy Developer; verify metadata live.
8. Tạo caller UI/flow sau khi contract ổn định.
9. Export/unpack solution và review source diff.

Template handler tối thiểu:

```csharp
public sealed class RequestEquipmentHealthCheck : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(
            typeof(IPluginExecutionContext));

        if (!string.Equals(context.MessageName,
                "fmc_RequestEquipmentHealthCheck",
                StringComparison.Ordinal))
            return;

        var raw = context.InputParameters["EquipmentId"] as string;
        Guid equipmentId;
        if (!Guid.TryParse(raw, out equipmentId))
            throw new InvalidPluginExecutionException(
                "BMS-HEALTH-001: EquipmentId must be a GUID string.");

        // Tạo hoặc reuse queue row ở đây; không chạy tác vụ dài trong request UI.
        context.OutputParameters["JobId"] = Guid.NewGuid();
        context.OutputParameters["Created"] = true;
        context.OutputParameters["Message"] = "Queued health check.";
    }
}
```

Trước khi deploy, dùng tab **Thiết kế contract** của lab. Nó kiểm tra Action/Function,
binding, Async Only, privilege, input và output tối thiểu. Simulator chỉ dạy nhánh logic;
nó không thay thế unit test SDK, metadata verification hoặc test trên Developer.
