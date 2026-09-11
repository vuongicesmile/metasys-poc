# Tự tạo và deploy Dataverse plug-in từ local — từng bước

Ngày soạn: 2026-09-10. Bài thực hành dùng plugin đã triển khai
`FMCentralBms.Plugins.RequireEquipmentBuilding` làm mẫu. Mục tiêu: bạn hiểu
mỗi file, mỗi đoạn code, chọn đúng DLL và tự đăng ký được một rule tương tự.

Các lệnh chạy trong **Windows PowerShell**, tại `D:\metasys-poc`.
Ví dụ mở rộng ở bước 12 là bài tập, chưa phải rule đã deploy.
Lần soạn tài liệu chỉ kiểm tra local/documentation; kết quả cloud đã triển khai
được ghi riêng trong [receipt ngày 2026-09-10](../plans/dataverse-plugins.vi.md#11-deployment-receipt--developer-environment-2026-09-10).

## 1. Hiểu mình đang tạo cái gì

Rule mẫu: **Equipment phải có Building khi tạo; khi sửa, không được chủ động
xóa Building.** Đổi tên Equipment mà không gửi lại Building vẫn được phép.

Quá trình đưa code lên cloud:

```text
RequireEquipmentBuilding.cs + .csproj + .snk
                  | dotnet build (trên máy bạn)
                  v
FMCentralBms.Plugins.dll
                  | PRT Register/Update hoặc command SDK của repo
                  v
Dataverse assembly -> plugin class -> Create step + Update step
                  | thêm assembly và cả hai steps vào FMCentralBms
                  v
Solution ZIP để mang code/cấu hình sang môi trường khác
```

Quá trình chạy nghiệp vụ sau khi đã deploy:

```text
Form / API / worker gửi Equipment
    -> Dataverse chọn step phù hợp
    -> gọi RequireEquipmentBuilding.Execute(...)
    -> có Building: cho request tiếp tục
    -> thiếu hoặc xóa Building: trả BMS-EQUIPMENT-001 và chặn thao tác
```

Ở đây “publish plugin” thường gọi là **register/deploy/update assembly**.
`public` trong `public class` là phạm vi truy cập của C#, không phải lệnh upload
và không làm dữ liệu Dataverse thành công khai.

**Deploy plugin** và **sync dữ liệu** là hai việc khác nhau. Build DLL chưa gửi
gì lên Dataverse. Sync worker gửi record, không gửi DLL. Sau khi deploy,
Dataverse chạy plugin trên cloud; máy phát triển không phải mở để plugin hoạt
động. Worker local vẫn phải chạy nếu bạn muốn nó tiếp tục gửi dữ liệu từ SQL.

## 2. Chuẩn bị và xác định môi trường

```powershell
Set-Location D:\metasys-poc
dotnet --list-sdks
pac plugin init help
pac auth list
pac org who --environment https://org06cbc9ec.crm5.dynamics.com/
```

`Set-Location` đặt thư mục làm việc. `dotnet` là công cụ build; `pac` là Power
Platform CLI. Bốn lệnh sau chỉ kiểm tra tooling/danh tính, chưa deploy plugin.

Máy dùng trong bài có .NET SDK `10.0.400`, PAC `2.11.2`. SDK dùng để build có
thể là .NET 10 nhưng **DLL plugin target .NET Framework 4.8 (`net48`)**.
Microsoft hiện hỗ trợ plugin target 4.6.2–4.8, khuyến nghị 4.8.
[Microsoft: supported frameworks](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/supported-customizations#support-for-net-framework-versions).

Đối chiếu kết quả danh tính với:

| Thuộc tính | Giá trị của bài này |
| --- | --- |
| Organization URL | `https://org06cbc9ec.crm5.dynamics.com/` |
| Organization ID | `ab191700-b99e-f111-aaa0-000d3a80bb96` |
| Environment ID | `5abcb0e5-99b2-e51f-aa0e-90d84405798b` |
| Solution unique name | `FMCentralBms` |
| Publisher | `FMCentralBmsPublisher` |
| Table cần bảo vệ | `fmc_bmsequipment` — standard table |
| Lookup cần kiểm tra | `fmc_buildingid` → `fmc_bmsbuilding` |

PRT, PAC và worker có phiên authentication riêng. PAC đăng nhập được chưa bảo
đảm PRT hoặc worker đăng nhập được. Tài khoản deploy cần quyền đăng ký plugin;
dùng identity developer đã cấu hình trong dự án.

## 3. Tạo project local hoặc dùng lại project đã có

Kiểm tra trước:

```powershell
Test-Path .\plugins\FMCentralBms.Plugins\FMCentralBms.Plugins.csproj
```

**Trong repo hiện tại kết quả là `True`: mở project và tiếp tục bước 4.**
Không chạy `init` đè lên nó; đây là project của plugin đã deploy.

Nếu đang dựng lại từ đầu ở một workspace chưa có project đó, chạy một lần:

```powershell
pac plugin init --outputDirectory .\plugins\FMCentralBms.Plugins
```

Lệnh sinh project, code mẫu và signing key. Template của PAC 2.11.2 sinh
`net462`; mở file `.csproj` và đổi **chỉ** giá trị `TargetFramework` thành
`net48`. Giữ các phần khác do template tạo.
[Microsoft: pac plugin init](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/plugin#pac-plugin-init).

Trong editor, xóa file mẫu `Plugin1.cs` của project vừa tạo, thêm file
`RequireEquipmentBuilding.cs` ở cùng thư mục với `.csproj`, rồi paste code ở
bước 4. `PluginBase.cs` có thể giữ lại; rule này trực tiếp triển khai `IPlugin`
nên không cần kế thừa base class đó.

Cấu trúc cần hiểu:

```text
plugins/FMCentralBms.Plugins/
  FMCentralBms.Plugins.csproj    cấu hình build/dependencies/version
  FMCentralBms.Plugins.snk       key ký assembly, giữ ổn định khi cập nhật
  RequireEquipmentBuilding.cs   logic nghiệp vụ
  PluginBase.cs                 helper do template sinh; mẫu này không dùng
  bin/Release/net48/            kết quả build
  obj/                         file trung gian do build sinh
```

Các dòng quan trọng trong [project thật](../../plugins/FMCentralBms.Plugins/FMCentralBms.Plugins.csproj):

```xml
<TargetFramework>net48</TargetFramework>
<SignAssembly>true</SignAssembly>
<AssemblyOriginatorKeyFile>FMCentralBms.Plugins.snk</AssemblyOriginatorKeyFile>
<AssemblyVersion>1.0.0.0</AssemblyVersion>
<FileVersion>1.0.0.0</FileVersion>
```

| Dòng/property | Ý nghĩa khi thực hành |
| --- | --- |
| `TargetFramework` | Chọn runtime mà DLL nhắm tới; không đổi sang `net10.0` giống worker. |
| `SignAssembly` | Yêu cầu build tạo strong-name signature. |
| `AssemblyOriginatorKeyFile` | Chỉ file key dùng để ký; file phải tồn tại. |
| `AssemblyVersion` | Version identity của DLL; command SDK của repo so sánh giá trị này để quyết định upload lại. |
| `FileVersion` | Version hiển thị của file; bài này tăng cùng AssemblyVersion cho dễ theo dõi. |

Ba package trong project hiện tại:

| Package | Dùng để làm gì |
| --- | --- |
| `Microsoft.CrmSdk.CoreAssemblies` | Cung cấp `IPlugin`, `Entity`, `EntityReference`, context và exception để compile code. |
| `Microsoft.PowerApps.MSBuild.Plugin` | Các build/package targets từ template Power Apps. |
| `Microsoft.NETFramework.ReferenceAssemblies` | Reference assemblies .NET Framework phục vụ build. |

`PrivateAssets="All"` trong PackageReference kiểm soát dependency truyền sang
project/package khác; nó không phải cơ chế giấu secret. `.snk` là key ký code,
không phải token đăng nhập Dataverse; giữ cùng key trong source có kiểm soát
để những lần update còn cùng assembly identity.

Nếu chuyển source sang máy khác, lấy `.csproj`, các file `.cs` cần thiết và
`.snk` (cùng file build config chung nếu project mới có dùng). Máy nhận chạy
restore/build để tạo lại `bin`/`obj`. Chỉ có DLL thì deploy được bản đã compile,
nhưng chưa có source để sửa rule.

## 4. Copy code và hiểu từng dòng

Mở [RequireEquipmentBuilding.cs](../../plugins/FMCentralBms.Plugins/RequireEquipmentBuilding.cs).
Dưới đây là toàn bộ 47 dòng của source tại thời điểm soạn; copy **nội dung
code**, không copy dấu hàng rào Markdown.

```csharp
using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    /// <summary>
    /// Ensures every Equipment create or explicit Building lookup update has a Building.
    /// </summary>
    public sealed class RequireEquipmentBuilding : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var trace = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(
                typeof(IPluginExecutionContext));
            trace?.Trace("RequireEquipmentBuilding START Message={0}; Stage={1}; Mode={2}; Depth={3}; CorrelationId={4}; OperationId={5}",
                context.MessageName, context.Stage, context.Mode, context.Depth, context.CorrelationId, context.OperationId);

            bool isCreate = string.Equals(context.MessageName, "Create",
                StringComparison.OrdinalIgnoreCase);
            bool isUpdate = string.Equals(context.MessageName, "Update",
                StringComparison.OrdinalIgnoreCase);

            if ((!isCreate && !isUpdate) || context.Stage != 10 || context.Mode != 0)
            {
                trace?.Trace("RequireEquipmentBuilding SKIP: unsupported message, stage or mode.");
                return;
            }

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target) ||
                target.LogicalName != "fmc_bmsequipment")
            {
                trace?.Trace("RequireEquipmentBuilding SKIP: missing/invalid Target or different table.");
                return;
            }

            // Update payloads contain only the columns sent by the caller. An omitted
            // lookup means "leave it unchanged"; an explicit null clears the lookup.
            if (isUpdate && !target.Contains("fmc_buildingid"))
            {
                trace?.Trace("RequireEquipmentBuilding SKIP: Update omitted fmc_buildingid; lookup unchanged.");
                return;
            }

            if (target.GetAttributeValue<EntityReference>("fmc_buildingid") == null)
            {
                trace?.Trace("RequireEquipmentBuilding BLOCK BMS-EQUIPMENT-001: Building missing/null; LookupIncluded={0}; CorrelationId={1}",
                    target.Contains("fmc_buildingid"), context.CorrelationId);

                throw new InvalidPluginExecutionException(
                    "BMS-EQUIPMENT-001: Equipment phai thuoc mot Building. " +
                    "Hay chon Building truoc khi luu.");
            }
            trace?.Trace("RequireEquipmentBuilding PASS: Building supplied; validation completed.");
        }
    }
}
```

Số dòng dưới đây tính từ `using System;` là dòng 1, bao gồm dòng trống:

| Dòng | Đọc như thế nào | Vì sao cần |
| --- | --- | --- |
| 1 | Dùng namespace `System`. | Có `IServiceProvider`, `StringComparison` và các kiểu cơ bản. |
| 2 | Dùng Dataverse SDK. | C# nhận ra `IPlugin`, `Entity` và các kiểu SDK. |
| 4–5 | Nhóm class vào namespace `FMCentralBms.Plugins`. | Tên đầy đủ khi đăng ký là namespace + tên class. |
| 6–8 | XML comment cho người đọc/editor. | Không thực thi business logic. |
| 9 | `public`: truy cập từ ngoài assembly; `sealed`: không cho kế thừa; `: IPlugin`: triển khai interface plugin. | Dataverse có entry point theo contract `IPlugin`. `sealed` là lựa chọn thiết kế của mẫu, không phải nút deploy. |
| 11–12 | Hàm `Execute` nhận service provider từ Dataverse. `void` nghĩa là không trả object kết quả. | Dataverse gọi hàm này khi step khớp request. |
| 13–17 | Xin tracing service và context; ghi START với message, stage, mode, depth, correlation ID và operation ID. | Lấy message, stage, mode, payload và correlation ID. `var` để compiler suy ra kiểu, không phải biến dynamic. |
| 19–22 | Tạo hai biến bool xác định Create hoặc Update. | `OrdinalIgnoreCase` so sánh tên không phân biệt chữ hoa/thường. |
| 24–28 | Không phải Create/Update, hoặc stage khác 10, hoặc mode khác 0 thì thoát handler. | Logic được viết cho PreValidation synchronous. Đăng ký nhầm Stage 20 thì code này sẽ bỏ qua. |
| 30 | Kiểm tra input có `Target`. | Không truy cập một key không tồn tại. |
| 31 | Kiểm tra Target là `Entity`, đồng thời đặt tên biến `target`. | Handler này đọc payload record; không xử lý mọi loại message. |
| 32–36 | Table phải đúng `fmc_bmsequipment`, nếu khác thì thoát. | Dùng logical name, không dùng nhãn “Equipment” trên UI. |
| 38–44 | Nếu Update không gửi `fmc_buildingid`, không kiểm tra cột này. | Update thường chỉ gửi vài cột; thiếu key không có nghĩa là xóa lookup. |
| 46–47 | Đọc lookup dưới dạng `EntityReference`; nếu null thì vi phạm. | Reference chứa logical name + GUID Building. Thiếu key cũng cho null nên Create thiếu Building bị chặn. |
| 48–49 | Ghi BLOCK với mã lỗi, trạng thái có/thiếu lookup và correlation ID. | Hỗ trợ tìm lỗi khi trace logging được bật. `?.` chỉ gọi nếu service khác null; `{0}`, `{1}` được thay bằng hai giá trị phía sau. |
| 51–53 | Ném exception với mã lỗi nghiệp vụ. | Request bị từ chối; caller nhận thông báo để sửa dữ liệu. Dấu `+` nối hai chuỗi thành một thông báo. |
| 54–58 | Ghi PASS khi Building được cung cấp, rồi đóng các khối. | Dấu `{}` xác định phạm vi code; dòng trống giúp dễ đọc. |

Cú pháp `!` nghĩa là “không”, `&&` là “và”, `||` là “hoặc”, `!=` là “khác”,
`==` là “bằng”. C# đánh giá `||` từ trái sang phải và dừng khi đủ điều kiện;
vì vậy dòng 25 không đọc Target khi dòng 24 đã phát hiện không có Target.

**`return` chỉ kết thúc plugin này**, cho Dataverse tiếp tục pipeline;
**`throw` chặn request**. Một plugin khác hoặc validation khác của Dataverse
vẫn có thể từ chối sau khi handler này return.
[Microsoft: handle exceptions](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/handle-exceptions).

Ví dụ quan trọng nhất về Update:

| Payload theo SDK | Kết quả rule này |
| --- | --- |
| Create: chỉ có `fmc_name` | Bị chặn vì không có Building. |
| Create: có `fmc_buildingid = EntityReference(...)` | Cho tiếp tục; nền tảng còn kiểm tra reference/quyền. |
| Update: chỉ có `fmc_name` | Giữ nguyên Building; step Update có filter nên thường không được gọi. |
| Update: có `fmc_buildingid = null` | Step chạy và chặn xóa lookup. |
| Update: gửi lại đúng Building đang có | Step vẫn có thể chạy; lookup hợp lệ nên cho tiếp tục. |

`Target` không phải bản đầy đủ đã lưu của record. Nếu rule tương lai cần so
sánh giá trị cũ/mới, phải thiết kế thêm Pre Image và chọn các cột cần đọc.
Rule hiện tại không cần truy vấn thêm và không tự gọi `Update` trong plugin.
[Microsoft: execution context and plug-in code](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/write-plug-in).

> Source tracing cập nhật local ngày 2026-09-11, version `1.0.0.1`; chưa phải bằng chứng DLL cloud đã update. Xem [cách xem trace](dataverse-plugin-tracing.vi.md).

## 5. Build: từ code thành đúng DLL để upload

```powershell
dotnet build .\plugins\FMCentralBms.Plugins\FMCentralBms.Plugins.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed; khong deploy DLL cu.' }
$pluginDll = (Resolve-Path .\plugins\FMCentralBms.Plugins\bin\Release\net48\FMCentralBms.Plugins.dll).Path
Get-Item -LiteralPath $pluginDll | Select-Object FullName, Length, LastWriteTime
[System.Reflection.AssemblyName]::GetAssemblyName($pluginDll).FullName
Get-FileHash -LiteralPath $pluginDll -Algorithm SHA256
```

`dotnet build` restore NuGet nếu cần, compile `.cs`, ký bằng `.snk`, rồi tạo
DLL. `-c Release` chọn cấu hình Release. Ba lệnh cuối cho biết file nào vừa
build, assembly identity và hash của chính file sẽ upload.

Đường dẫn đầy đủ:

```text
D:\metasys-poc\plugins\FMCentralBms.Plugins\bin\Release\net48\FMCentralBms.Plugins.dll
```

Khi dùng PRT trên cùng máy, chọn trực tiếp file này trong hộp Browse.
Nếu deploy từ máy khác, copy file đó sang máy chạy PRT, rồi Browse tới bản copy.

| File bạn thấy | Có upload trong cách đăng ký DLL của bài này không? |
| --- | --- |
| `FMCentralBms.Plugins.dll` | **Có: đây là code đã compile.** |
| `FMCentralBms.Plugins.pdb` | Không cần cho registration này; giữ phục vụ debug phù hợp. |
| `Microsoft.Xrm.Sdk.dll`, `Microsoft.Crm.Sdk.Proxy.dll` | Không đăng ký làm plugin của bạn; SDK runtime được nền tảng cung cấp. |
| DLL dependency khác trong `bin` | Không upload cả thư mục một cách mặc định. Nếu code mới thật sự cần thư viện ngoài, thiết kế plugin package/dependencies riêng. |
| `.cs`, `.csproj`, `.snk` | Dùng để phát triển/build, không chọn trong hộp Register Assembly. |
| `.nupkg` do template sinh | Không dùng trong đường DLL của bài này; plugin package là một cách triển khai khác. |
| `DataverseSyncWorker.dll` hoặc `.exe` | Không: đây là ứng dụng gửi dữ liệu/chạy deployment helper trên máy ngoài Dataverse. |
| DLL dưới `dataverse/FMCentralBms/PluginAssemblies` | Bản xuất từ cloud của lần export trước; khi vừa sửa code, chọn DLL mới trong `bin/Release/net48`. |

Cách đăng ký một DLL này phù hợp với rule đang chỉ dùng SDK/nền tảng. Signing
và dependency packaging là hai việc khác nhau.
[Microsoft: assembly and dependency packaging](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/build-and-package).

Project hiện đã có trong `MetasysPoc.sln`. Chỉ khi dựng mới, thêm một lần:

```powershell
dotnet sln .\MetasysPoc.sln add .\plugins\FMCentralBms.Plugins\FMCentralBms.Plugins.csproj
```

`.sln` là nhóm project để build chung, không phải Dataverse solution ZIP.

## 6. Upload assembly bằng Plug-in Registration Tool (PRT)

Đây là đường thực hành chính để tự làm plugin khác. Mở tool:

```powershell
pac tool prt
```

Trong PRT:

1. Chọn **Create new connection**. Đăng nhập bằng tài khoản developer; chọn
   Office 365 theo giao diện PRT. Với MFA, dùng interactive sign-in.
2. Nếu có nhiều organization, hiển thị danh sách và chọn đúng Developer org ở
   bước 2; kiểm tra URL kết nối trước khi Register/Update.
3. Tìm assembly `FMCentralBms.Plugins` trong danh sách.
4. Nếu chưa có: **Register → Register New Assembly**.
5. Nút `...`/Browse: chọn đúng `FMCentralBms.Plugins.dll` của bước 5.
6. Chọn class `FMCentralBms.Plugins.RequireEquipmentBuilding` trong danh sách
   được PRT đọc từ DLL; đặt Isolation **Sandbox**, Location **Database**.
7. Bấm **Register Selected Plugins**, chờ thông báo thành công rồi Refresh.

Nếu assembly đã có như Developer hiện tại, dùng **Update** theo bước 11.
Đừng tạo thêm hai steps trùng rule chỉ để học lại.

Kết quả sau khi upload lần đầu là cây Assembly → Plugin Type. Đến đây mới có
code trên cloud; bước 7 sẽ khai báo khi nào Dataverse gọi code.
PRT đọc class từ DLL và xử lý upload, bạn không có thư mục server Dataverse
để tự paste DLL vào đó.
[Microsoft: connect and register an assembly](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/tutorial-write-plug-in#register-plug-in).

## 7. Tạo hai steps — gắn code vào sự kiện

Mở rộng assembly, click phải **class** `RequireEquipmentBuilding` →
**Register New Step**. Tạo step Create trước, sau đó làm lại cho Update.
Nếu đã có đúng step thì mở Properties/Update và kiểm tra cấu hình.

Các giá trị dưới đây bám theo [hai step đã export của repo](../../dataverse/FMCentralBms/SdkMessageProcessingSteps):

| Ô trong PRT | Step Create | Step Update |
| --- | --- | --- |
| Name | `BMS: Require Building on Equipment Create` | `BMS: Require Building on Equipment Update` |
| Message | `Create` | `Update` |
| Primary Entity | `fmc_bmsequipment` | `fmc_bmsequipment` |
| Secondary Entity | Trống | Trống |
| Filtering Attributes | Trống | Chỉ `fmc_buildingid` |
| Event Handler | `FMCentralBms.Plugins.RequireEquipmentBuilding` | Cùng class |
| Event Pipeline Stage | `PreValidation` | `PreValidation` |
| Execution Mode | `Synchronous` | `Synchronous` |
| Execution Order | `10` | `10` |
| Run in User's Context | `Calling User` | `Calling User` |
| Deployment | `Server` | `Server` |
| Unsecure/Secure Configuration | Trống | Trống |
| Images | Không thêm | Không thêm |
| Sau khi lưu | Enabled | Enabled |

Ý nghĩa của từng lựa chọn:

- **Message + Primary Entity**: “gọi class này khi Create/Update Equipment”.
- **Filtering Attributes**: Update chỉ quan tâm request có gửi Building.
  Có gửi lại cùng giá trị vẫn đủ điều kiện gọi step.
- **PreValidation**: kiểm tra sớm trước thao tác chính. Guard trong code đòi
  `context.Stage == 10`, vì vậy code và registration phải khớp.
- **Synchronous**: caller chờ validation, nên nhận lỗi ngay khi vi phạm.
  Code tương ứng `context.Mode == 0`.
- **Execution Order 10**: thứ tự tương đối giữa các steps cùng sự kiện/stage.
  Số này không phải Stage 10 và không phải số giây chờ.
- **Calling User**: context của người/app đã gửi request.
- **Server**: thực thi phía Dataverse.
- **Images/Configuration trống**: code trên không đọc các dữ liệu bổ sung này.

Một class được dùng lại ở hai steps vì có hai loại request cần bảo vệ.
Step chọn lúc gọi; code quyết định request có hợp lệ hay không.
[Microsoft: step registration](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in#step-registration).

**Điểm đặc biệt của worker hiện tại:** `DataverseWriter` dùng `UpsertRequest`
cho Equipment standard. Khi upsert, Dataverse đi theo nhánh Create hoặc
Update tương ứng; hai steps trên bảo vệ đường ghi đó. Không tự áp dụng kết
luận này cho elastic `fmc_bmsreading`, có pipeline Upsert khác.
[Microsoft: Upsert behavior](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/use-upsert-insert-update-record).

## 8. Thêm assembly và steps vào solution FMCentralBms

Trong [Power Apps maker portal](https://make.powerapps.com):

1. Chọn đúng environment ở góc trên.
2. **Solutions → FMCentralBms**.
3. **Add existing → More → Developer → Plug-in assembly**.
4. Chọn `FMCentralBms.Plugins`, bấm Add.
5. **Add existing → More → Developer → Plug-in step**.
6. Chọn cả hai steps ở bước 7, bấm Add.
7. Kiểm tra Objects có assembly và hai steps.

Nếu đăng ký bằng command SDK của repo thì bước thêm vào solution này đã được
command thực hiện; vào portal để kiểm tra, không cần thêm bản sao.

Thêm vào solution giúp export/import mang đủ code lẫn registration. PRT
đăng ký assembly không tự thêm các steps vào custom solution `FMCentralBms`.
Nút **Publish all customizations** không compile source hoặc upload DLL mới.
Assembly đã đăng ký với steps enabled sẽ phục vụ các request phù hợp;
solution membership phục vụ quản lý/đóng gói.
[Microsoft: add assembly and steps to a solution](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in#add-your-assembly-to-a-solution).

## 9. Cách repo đã tự động hóa thay cho thao tác PRT

Chỉ dùng cách này cho **rule hiện tại**. Bạn chọn PRT hoặc command SDK;
không cần thực hiện cả hai để deploy cùng một phiên bản.

Sau build thành công, ở PowerShell tại root:

```powershell
$pluginDll = (Resolve-Path .\plugins\FMCentralBms.Plugins\bin\Release\net48\FMCentralBms.Plugins.dll).Path
dotnet run --project .\DataverseSyncWorker -c Release --no-launch-profile -- --register-plugin "--plugin-path=$pluginDll"
if ($LASTEXITCODE -ne 0) { throw 'Registration failed; doc loi truoc khi tiep tuc.' }
```

Dùng `Resolve-Path` trước khi `dotnet run` để nhận **absolute path**.
`dotnet run --project` có working directory của project; đường relative hoặc
default path của helper dễ trỏ sai. `"--plugin-path=..."` giữ cả option làm
một argument, kể cả khi đường dẫn có dấu cách.

| Phần command | Tác dụng |
| --- | --- |
| `dotnet run --project .\DataverseSyncWorker` | Chạy ứng dụng chứa deployment helper. |
| `-c Release` | Chọn build worker Release; không tự build lại project plugin. |
| `--no-launch-profile` | Không lấy launch settings của profile phát triển. |
| `--` | Các argument phía sau được truyền cho chương trình C#. |
| `--register-plugin` | Vào nhánh deployment ở `Program.cs`, rồi thoát trước khi chạy background sync. |
| `--plugin-path=...` | Chỉ DLL đã build cần đọc/upload. |

[Program.cs](../../DataverseSyncWorker/Program.cs) gọi
[DataversePluginProvisioner.Register](../../DataverseSyncWorker/Services/DataversePluginProvisioner.cs).
Các bước bên trong tương ứng thao tác tay như sau:

| Code/helper | Việc nó thực hiện |
| --- | --- |
| `Path.GetFullPath`, `File.Exists` | Xác định và kiểm tra đường dẫn DLL local. |
| `AssemblyName.GetAssemblyName(...).Version` | Đọc AssemblyVersion của DLL. |
| `connection.Get()` | Lấy ServiceClient đã auth; `DataverseConnection` gọi WhoAmI và so với ExpectedOrganizationId trước writes. |
| `EnsureAssembly` | Tìm assembly theo tên; tạo nếu thiếu; update content khi version khác. |
| `Convert.ToBase64String(await File.ReadAllBytesAsync(path))` | Đọc bytes DLL và mã hóa để đặt vào `pluginassembly.content`; đây chính là phần gửi code lên cloud. |
| `new OptionSetValue(2)` cho isolation, `(0)` cho source | Sandbox và Database trong request SDK; Choice cần đúng kiểu SDK. |
| `EnsurePluginType` | Tìm/tạo record class, liên kết với assembly qua `pluginassemblyid`. |
| `FindMessage`, `FindMessageFilter` | Tìm GUID message và filter của table trong org hiện tại. Không hard-code GUID từ environment khác. |
| `EnsureStep` | Tìm step theo tên; tạo/update stage, mode, filter, thứ tự, handler và trạng thái. |
| `new EntityReference("plugintype", pluginTypeId)` | Step trỏ tới class đã đăng ký bằng GUID. |
| `AddToSolution`, `AddSolutionComponentRequest` | Đưa assembly (component type 91) và steps (92) vào FMCentralBms. |

Hai giới hạn bạn cần nhớ khi làm lại:

1. **Helper giữ bytes cloud nếu AssemblyVersion không đổi**, kể cả source
   local vừa sửa. Vì vậy phải tăng AssemblyVersion trước khi cập nhật bằng
   command này; chỉ tăng FileVersion chưa đủ.
2. **Helper hard-code assembly/class/table/hai step names của rule hiện tại.**
   Đổi `--plugin-path` sang DLL bất kỳ không biến nó thành generic deployer.
   Để học plugin mới, dùng PRT. Muốn tự động hóa rule mới thì bổ sung definition
   và kiểm thử helper, không chỉ đổi tên file.

Việc rerun tránh tạo bản trùng khi các tên còn đúng như helper mong đợi; nó
vẫn update/re-enable hai steps. Đây không phải read-only preview và cũng
không phải một transaction cho toàn bộ registration.

PAC hiện có `pac plugin push`, nhưng help của `2.11.2` yêu cầu `--pluginId`.
Đừng coi lệnh này là cách tự tạo toàn bộ assembly/class/steps lần đầu.
[Microsoft: pac plugin push](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/plugin#pac-plugin-push).

## 10. Kiểm thử plugin và kiểm thử sync dữ liệu

### 10.1. Test rule trực tiếp bằng API

Copy **toàn bộ PowerShell block ở [API test trong plan](../plans/dataverse-plugins.vi.md#7-test-api-thiếu-building-có-building-xóa-lookup-đổi-tên)**
vào một file riêng, ví dụ `.artifacts/Test-EquipmentBuildingPlugin.ps1`, rồi
chạy từ root. Trong VS Code: File → New Text File, paste block, Save As đường
dẫn trên; chọn UTF-8 with BOM nếu chạy bằng Windows PowerShell 5.1. Nếu chưa
có thư mục `.artifacts`, tạo thư mục đó trong workspace trước khi Save As.
Không chỉ copy vài lệnh cuối vì chúng dùng functions và biến được khai báo
đầu block.

```powershell
Set-Location D:\metasys-poc
powershell -NoProfile -ExecutionPolicy Bypass -File .\.artifacts\Test-EquipmentBuildingPlugin.ps1
```

Script này **ghi cloud**: tạo Equipment riêng với code `DEMO-PLUGIN-<GUID>`
và xóa đúng GUID test trong `finally`. Cần Building `BLDG-A` đã có trong org.
Nó dùng developer token helper và kiểm tra Organization ID trước khi test.

| Phần test script | Tác dụng cần hiểu |
| --- | --- |
| Đọc `appsettings.json` | Lấy URL/path token helper của repo; không upload config vào plugin. |
| `& $pluginConfig.Dataverse.DeveloperTokenPython ...` | Chạy helper lấy token cho API caller. Token giữ trong biến, không in ra. |
| `WhoAmI` | Xác nhận đúng organization trước khi tạo record. |
| `Invoke-BmsPluginDemoApi` | Function dùng chung để GET/POST/PATCH/DELETE qua Web API. |
| `Assert-BmsPluginRejected` | Chỉ PASS khi lỗi chứa `BMS-EQUIPMENT-001`; lỗi permission/network không được coi là plugin PASS. |
| `[guid]::NewGuid()` | Tạo ID riêng cho test, không mượn identity nguồn SQL. |
| `'fmc_buildingid@odata.bind'` | Web API gán lookup bằng entity-set path + GUID Building. |
| `PATCH` + `If-Match: *` | Chỉ update record đã tồn tại, tránh vô tình upsert tạo mới. |
| `finally` | Dọn record demo khi rời phần test, kể cả khi một assertion lỗi. Nếu cleanup lỗi, dùng GUID hiển thị để kiểm tra lại. |

Script mong đợi bốn kết quả:

1. Create thiếu Building → bị chặn với `BMS-EQUIPMENT-001`.
2. Create có Building → thành công.
3. Update clear Building → bị chặn với cùng mã lỗi.
4. Update chỉ đổi tên → thành công, GET xác nhận Building giữ nguyên.

Khác nhau về cách biểu diễn lookup:

```text
SDK C#:       fmc_buildingid = new EntityReference("fmc_bmsbuilding", guid)
Web API ghi:  "fmc_buildingid@odata.bind": "/fmc_bmsbuildings(<guid>)"
Web API đọc:  "_fmc_buildingid_value": "<guid>"
```

Các tên API này lấy từ schema hiện có; khi dùng table khác phải kiểm tra lại
entity-set/navigation names. Test bằng form có thể bị UI chặn trước khi tới
plugin, nên cần test API và kiểm tra mã lỗi của chính rule.

### 10.2. Test với worker

Bắt đầu bằng lệnh chỉ đọc:

```powershell
dotnet run --project .\DataverseSyncWorker -c Release --no-launch-profile -- --verify
```

Khi có pending SQL readings và bạn muốn thực hiện sync:

```powershell
dotnet run --project .\DataverseSyncWorker -c Release --no-launch-profile -- --enqueue
dotnet run --project .\DataverseSyncWorker -c Release --no-launch-profile -- --process-command-once
dotnet run --project .\DataverseSyncWorker -c Release --no-launch-profile -- --verify
```

`--enqueue` tạo hoặc dùng lại `fmc_syncrequest`. `--process-command-once`
claim tối đa một request và có thể xử lý nhiều batches trong cutoff của
request đó. `--verify` đọc ledger, catalog/quan hệ và tối đa 25 retained-history
samples. Các lệnh này không deploy DLL.

Trong mỗi batch, worker upsert Building → Equipment → Point/history. Plugin
được gọi ở bước ghi Equipment; validation hợp lệ cho phép batch tiếp tục.

Nếu request `Requeued`, nó đã nhường lại sau execution window; có thể chạy
`--process-command-once` tiếp hoặc để CommandDriven worker xử lý. `Idle` có
thể nghĩa là một worker khác đang giữ request; xem request ID để kết luận.
Nếu request `Failed`, xem lỗi; không reset ledger/SQL identity để bỏ qua lỗi.

Khi PendingRows bằng 0, command có thể kết thúc trước khi chạy catalog; vì
vậy một request 0 delivered không chứng minh plugin đã chạy. Để kiểm thử
end-to-end, cần có reading mới/pending từ quy trình ingestion trong
[runbook SQL → Dataverse](sql-to-dataverse-runbook.vi.md).

Receipt lịch sử của bài: request `475ba7a0-bcac-f111-aaad-00224819a344`
`Succeeded`, cutoff `81773`, delivered `54578`, quarantined `0`.
Sau đó verify báo pending/dead-letter `0`. Đây là mốc của lần deploy trước;
lần thực hành của bạn phải kiểm tra trạng thái mới.

## 11. Sửa code, upload lại và lưu solution export

### 11.1. Cập nhật một plugin đã đăng ký

1. Sửa `.cs` local.
2. Trong `.csproj`, tăng build/revision, ví dụ `AssemblyVersion` và
   `FileVersion` từ `1.0.0.0` lên `1.0.0.1`. Giữ assembly name, namespace/class
   và signing key khi cập nhật cùng rule.
3. Build Release theo bước 5; kiểm tra đúng file/version.
4. PRT: chọn **assembly hiện có → Update → Browse DLL mới → Update Selected
   Plugins**. Nếu dùng SDK helper cho rule cũ, chạy lại command ở bước 9.
5. Kiểm tra steps vẫn enabled và trỏ đúng class. Nếu chỉ sửa logic bên trong
   class, không cần tạo lại hai steps.
6. Nếu thêm class mới, upload assembly xong phải tạo steps cho **class mới**.
7. Chạy API test, và kiểm tra worker nếu rule ảnh hưởng đường sync.

Hướng update bằng PRT giữ registration hiện có; không cần Unregister assembly
để thay code. [Microsoft: update an assembly](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/tutorial-update-plug-in#update-the-plug-in-assembly-registration).

AssemblyVersion, FileVersion và **solution version** là ba giá trị khác nhau.
Solution version được đặt trong solution; tăng trước khi đóng gói release mới.
Thay major/minor của assembly cần xét lại references/steps; ví dụ bài này chỉ
dùng build/revision cho update thông thường.

### 11.2. Export code và registration đã có trên cloud

Sau khi kiểm tra solution membership, export vào thư mục mới:

```powershell
$pluginRelease = Join-Path 'D:\metasys-poc\.artifacts' ('plugin-release-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $pluginRelease | Out-Null
pac solution export --name FMCentralBms --environment https://org06cbc9ec.crm5.dynamics.com/ --path "$pluginRelease\FMCentralBms_unmanaged.zip" --managed false
if ($LASTEXITCODE -ne 0) { throw 'Export failed.' }
pac solution unpack --zipfile "$pluginRelease\FMCentralBms_unmanaged.zip" --folder "$pluginRelease\unpacked" --packagetype Unmanaged
if ($LASTEXITCODE -ne 0) { throw 'Unpack failed.' }
git diff --no-index -- .\dataverse\FMCentralBms "$pluginRelease\unpacked"
```

`git diff --no-index` trả code 1 khi có khác biệt; không có nghĩa export thất
bại. Review phần liên quan rồi merge vào `dataverse/FMCentralBms`, giữ các sửa
đổi local khác. Các vị trí cần nhìn:

| File/folder trong unpack | Chứa gì |
| --- | --- |
| `PluginAssemblies/<assembly-id>/...dll` | Bytes assembly lấy từ cloud. |
| `...dll.data.xml` | Metadata của assembly/types trong export. |
| `SdkMessageProcessingSteps/{step-id}.xml` | Message, table, stage, mode, filter và handler của mỗi step. |
| `Other/Solution.xml` | Manifest/root components; cần có assembly và steps. |

XML này là kết quả export để lưu version/review/package; copy XML vào thư
mục repo không tự tạo step trên cloud. Solution ZIP mang components đã chọn,
không copy hàng Building/Equipment/readings hay raw SQL. Nếu chuyển sang
environment khác, làm theo [quy trình deployment](../plans/dataverse-plugins.vi.md#9-đưa-plug-in-vào-solution-và-triển-khai)
với target/schema/dependencies cụ thể.

## 12. Bài tập: tự làm một rule tương tự

Ví dụ mới: **Building phải có tên; tên chỉ gồm khoảng trắng cũng không hợp lệ.** Mục này
minh họa việc đổi table/field/type/class và registration; chưa thêm rule vào
source runtime hoặc cloud trong lần soạn tài liệu này. Chỉ bật rule khi đó
là quy tắc nghiệp vụ bạn muốn áp dụng cho mọi caller của table.

Cách ít file nhất: thêm class vào **cùng project** `FMCentralBms.Plugins`.
Một DLL có thể chứa nhiều class plugin; không phải mỗi rule cần một DLL riêng.

Trong editor, copy file `RequireEquipmentBuilding.cs` thành file mới
`RequireBuildingName.cs`, giữ file cũ. Thay nội dung file mới bằng:

```csharp
using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    public sealed class RequireBuildingName : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(
                typeof(IPluginExecutionContext));
            bool isCreate = string.Equals(context.MessageName, "Create",
                StringComparison.OrdinalIgnoreCase);
            bool isUpdate = string.Equals(context.MessageName, "Update",
                StringComparison.OrdinalIgnoreCase);

            if ((!isCreate && !isUpdate) || context.Stage != 10 || context.Mode != 0)
                return;
            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target) ||
                target.LogicalName != "fmc_bmsbuilding")
                return;
            if (isUpdate && !target.Contains("fmc_name"))
                return;

            string name = target.GetAttributeValue<string>("fmc_name");
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidPluginExecutionException(
                    "BMS-BUILDING-001: Building phai co ten khong rong.");
        }
    }
}
```

So sánh để biết **copy xong phải sửa gì**:

| Thành phần | Rule Equipment cũ | Rule Building mới |
| --- | --- | --- |
| File/class | `RequireEquipmentBuilding` | `RequireBuildingName` |
| Namespace | `FMCentralBms.Plugins` | Giữ nguyên vì cùng project |
| Table trong code và PRT | `fmc_bmsequipment` | `fmc_bmsbuilding` |
| Field trong code và Update filter | `fmc_buildingid` | `fmc_name` |
| Kiểu đọc field | `EntityReference` cho lookup | `string` cho text |
| Điều kiện không hợp lệ | Reference bằng null | `IsNullOrWhiteSpace`: null, chuỗi rỗng hoặc chỉ khoảng trắng |
| Mã lỗi | `BMS-EQUIPMENT-001` | `BMS-BUILDING-001` |
| DLL upload | `FMCentralBms.Plugins.dll` | Cùng DLL, nay chứa thêm class |

`string.IsNullOrWhiteSpace` là validation; code không tự trim hoặc viết lại
tên. Muốn tự sửa payload là một yêu cầu khác, cần chọn stage/logic phù hợp.

Sau khi thêm file mới:

1. Tăng AssemblyVersion/FileVersion của project, build Release.
2. PRT **Update assembly hiện có**, chọn DLL mới và giữ cả class cũ lẫn class mới.
3. Refresh, mở class `FMCentralBms.Plugins.RequireBuildingName`.
4. Đăng ký hai steps: Create/Update trên `fmc_bmsbuilding`, PreValidation,
   Synchronous, order 10, Calling User, Server. Create filter trống;
   Update filter chỉ `fmc_name`.
5. Đặt tên khác rule cũ: `BMS: Require Name on Building Create` và
   `BMS: Require Name on Building Update`.
6. Thêm **hai steps mới** vào FMCentralBms; assembly đã là thành phần solution.
7. Test bằng record Building riêng: thiếu tên bị chặn, tên hợp lệ pass,
   update tên null/rỗng bị chặn, update cột khác không gửi tên pass.
   Khi gọi API, phải đổi entity set thành `fmc_bmsbuildings`, dùng đúng
   `fmc_buildingcode`/GUID test và kiểm tra mã `BMS-BUILDING-001`.
8. Dọn đúng record test; kiểm tra sync và export như bước 10–11.

**Không dùng `--register-plugin` hiện tại để tạo steps cho class mới này**:
helper vẫn chỉ biết `RequireEquipmentBuilding`.

Nếu muốn assembly riêng, dùng `pac plugin init` với thư mục mới, ví dụ
`plugins/FmLab.Plugins`, target `net48`, key riêng do template sinh và namespace
phù hợp. Copy rule vào project mới, build rồi đăng ký DLL mới bằng PRT.
Các giá trị table/field/filter/stage vẫn phải khớp code; không copy GUID
assembly/step của bài cũ sang registration mới.

## 13. Khi làm không chạy: kiểm tra theo triệu chứng

| Triệu chứng | Kiểm tra đầu tiên |
| --- | --- |
| Build báo không tìm được `IPlugin`/`Entity` | Package `Microsoft.CrmSdk.CoreAssemblies` và `using Microsoft.Xrm.Sdk`; chạy restore/build. |
| Build lỗi signing key | `.snk` có đúng tên/đường dẫn trong `.csproj` không? Giữ key cũ khi update. |
| Không thấy class trong PRT | Chọn đúng DLL mới; class public, concrete, triển khai `IPlugin`; build đã thành công. |
| Có assembly nhưng validation không chạy | Có steps enabled không; đúng table/message/stage/mode/filter không? |
| Đã sửa code nhưng cloud vẫn logic cũ | Build lại, Browse đúng Release DLL; helper có bỏ qua vì AssemblyVersion không đổi không? |
| Đổi tên Equipment không hiện trace rule này | Bình thường: Update không gửi `fmc_buildingid`, không khớp filter. |
| API test lỗi nhưng không có mã nghiệp vụ | Xem authentication, quyền, schema hoặc rule khác; không coi là PASS. |
| Form không cho bỏ trống Building | Form có thể chặn trước plugin; dùng API test để chứng minh server validation. |
| Có DLL trong ZIP nhưng sang đích không chạy | Kiểm tra export có cả hai steps và đích đã import/activate thành công. |
| Worker lỗi sau thêm rule | Xem payload nghiệp vụ có thỏa rule mới không; lỗi còn pending phải sửa nguyên nhân. |

Khi cần dừng rule trong Developer để xử lý lỗi, disable **đúng steps của rule**
trong PRT rồi kiểm tra lại. Thao tác đó tạm bỏ validation của rule; update
code và test trước khi enable lại. Giữ source/version/export tốt để có thể
phát hành lại. Không cần gỡ toàn bộ solution chỉ để cập nhật một plugin.
