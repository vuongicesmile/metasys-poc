# Bài plugin 02 — Không cho đổi Equipment Code sau khi tạo

Trạng thái: **ý tưởng và bài thực hành, chưa triển khai lên Dataverse**.
Ngày soạn: 2026-09-10. Thời gian dự kiến: 30–45 phút sau khi mở được PRT.

## 1. Kết quả bạn sẽ nhìn thấy

Bạn tạo một Equipment có Code `UI-LOCK-001`, sau đó thử sửa thành
`UI-LOCK-002` và bấm Save. Plugin hiện lỗi:

```text
BMS-EQUIPMENT-002: Khong duoc doi Equipment Code sau khi tao. Hay giu nguyen ma ban dau.
```

Đổi Name hoặc Description thì vẫn lưu được. Bạn thao tác hoàn toàn trong
**FMC BMS Demo**, dùng form **Information** bình thường. Không cần bỏ field
bắt buộc hoặc dùng form Plugin Test của bài trước: cả mã cũ và mã mới đều có
giá trị, nên validation required trên UI không chặn trước.

Lý do chọn rule này: `fmc_equipmentcode` là alternate key của Equipment và
được dùng trong việc đối chiếu catalog với SQL. Sửa mã tùy ý trên Dataverse có
thể làm sai việc đối chiếu. Rule bảo vệ mã đã có, không sửa mã, GUID, lookup hay
dữ liệu SQL. Đổi mã thực sự là một tác vụ migration riêng.

## 2. Điểm mới: Pre Image

Khi Update, `Target` chứa các cột caller gửi lên, không phải toàn bộ record.
`Pre Image` là snapshot các cột bạn chọn ở thời điểm trước thao tác hiện tại.

| Thành phần | Ví dụ | Ý nghĩa |
| --- | --- | --- |
| `Target["fmc_equipmentcode"]` | `UI-LOCK-002` | Mã đang được đề nghị lưu |
| `PreEntityImages["Before"]["fmc_equipmentcode"]` | `UI-LOCK-001` | Mã đang được lưu trước thao tác |
| So sánh hai giá trị | Khác nhau | Ném exception để từ chối Update |

`Before` là alias do bạn đặt trong PRT, không phải từ khóa mặc định.
Không cần gọi `Retrieve` để lấy lại record và không gọi `Update` bên trong
plugin này. [Microsoft: entity images](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in#define-entity-images).

Quy tắc chi tiết:

| Tình huống | Xử lý |
| --- | --- |
| Create Equipment mới | Rule này không chạy; rule Building cũ vẫn chạy |
| Update chỉ gửi Name/Description | Không cần kiểm tra Code |
| Update gửi Code y nguyên | Cho qua |
| Update đổi Code sang mã khác | Chặn với `BMS-EQUIPMENT-002` |
| Update chỉ đổi hoa/thường hoặc khoảng trắng trong Code | Cũng chặn; không tự chuẩn hóa identity |
| Update gửi Code = null | Chặn nếu request tới server; UI thường chặn trước |
| Thiếu image hoặc mã cũ trong image | Báo `BMS-EQUIPMENT-CONFIG-002` để sửa cấu hình/dữ liệu |

Đây là rule cho mọi caller khi đi qua Update, không chỉ riêng UI. Với worker,
gửi lại cùng mã phải được cho qua. Sau khi triển khai cần kiểm tra đường ghi
catalog thực tế trước khi coi là đã nghiệm thu toàn bộ integration. Đặc biệt,
upsert bằng một mã mới có thể tạo record khác: rule này không phát hiện mọi
trường hợp trùng thiết bị ngoài đời hoặc thay thế reconciliation.

## 3. Chuẩn bị

Mở PowerShell ở `D:\metasys-poc`:

```powershell
Set-Location D:\metasys-poc
Test-Path .\plugins\FMCentralBms.Plugins\FMCentralBms.Plugins.csproj
Test-Path .\plugins\FMCentralBms.Plugins\FMCentralBms.Plugins.snk
pac org who --environment https://org06cbc9ec.crm5.dynamics.com/
```

Hai `Test-Path` phải trả `True`. Khóa `.snk` được giữ ở local; nếu clone trên
máy khác, xem [hướng dẫn signing key](../../plugins/FMCentralBms.Plugins/README.md).
Giữ nguyên key và assembly name để update đúng assembly đã triển khai.

Đối chiếu organization ID `ab191700-b99e-f111-aaa0-000d3a80bb96`, environment
`5abcb0e5-99b2-e51f-aa0e-90d84405798b`, solution `FMCentralBms`.

Mở PRT:

```powershell
pac tool prt
```

PRT là ứng dụng Windows riêng. PAC tải và mở công cụ ở lần chạy đầu; nếu lỗi
NuGet thì phải xử lý việc tải trước, chưa thể thực hiện các bước đăng ký bên dưới.
PRT có phiên đăng nhập riêng: **Create New Connection**, đăng nhập tài khoản
Developer, chọn đúng organization URL trên. [Microsoft: tải/mở PRT](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/tool#pac-tool-prt).

## 4. Thêm một file C# vào project cũ

Trong VS Code/Visual Studio, tạo file:

```text
D:\metasys-poc\plugins\FMCentralBms.Plugins\PreventEquipmentCodeChange.cs
```

Giữ nguyên file `RequireEquipmentBuilding.cs`. Hai class sẽ cùng nằm trong
một DLL. Project SDK hiện tại tự đưa file `.cs` trong thư mục vào build.

Copy toàn bộ code sau, không copy dấu ```:

```csharp
using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    public sealed class PreventEquipmentCodeChange : IPlugin
    {
        private const string CodeColumn = "fmc_equipmentcode";
        private const string ImageAlias = "Before";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(
                typeof(IPluginExecutionContext));

            if (!string.Equals(context.MessageName, "Update",
                    StringComparison.OrdinalIgnoreCase) ||
                context.Stage != 10 || context.Mode != 0)
                return;

            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target) ||
                target.LogicalName != "fmc_bmsequipment")
                return;

            if (!target.Contains(CodeColumn))
                return;

            if (!context.PreEntityImages.Contains(ImageAlias))
                throw new InvalidPluginExecutionException(
                    "BMS-EQUIPMENT-CONFIG-002: Thieu Pre Image alias Before. " +
                    "Hay kiem tra cau hinh plugin step.");

            var before = context.PreEntityImages[ImageAlias];
            var oldCode = before.GetAttributeValue<string>(CodeColumn);
            var newCode = target.GetAttributeValue<string>(CodeColumn);

            if (string.IsNullOrWhiteSpace(oldCode))
                throw new InvalidPluginExecutionException(
                    "BMS-EQUIPMENT-CONFIG-002: Khong doc duoc ma cu. " +
                    "Kiem tra cot fmc_equipmentcode trong Pre Image va du lieu goc.");

            if (string.Equals(oldCode, newCode, StringComparison.Ordinal))
                return;

            var trace = (ITracingService)serviceProvider.GetService(
                typeof(ITracingService));
            trace?.Trace(
                "PreventEquipmentCodeChange blocked Update; CorrelationId={0}",
                context.CorrelationId);

            throw new InvalidPluginExecutionException(
                "BMS-EQUIPMENT-002: Khong duoc doi Equipment Code sau khi tao. " +
                "Hay giu nguyen ma ban dau.");
        }
    }
}
```

## 5. Hiểu từng khối code

| Code | Tại sao cần |
| --- | --- |
| `using System` | Các kiểu C# như `IServiceProvider`, `StringComparison` |
| `using Microsoft.Xrm.Sdk` | `IPlugin`, `Entity`, context và exception của Dataverse |
| `public sealed class ... : IPlugin` | Class công khai để Dataverse phát hiện và gọi; tên khác class bài 01 |
| `CodeColumn`, `ImageAlias` | Gom tên cột/alias vào hằng số để tránh gõ lệch ở nhiều chỗ |
| `Execute(...)` | Entry point được gọi khi step khớp request |
| `GetService(typeof(IPluginExecutionContext))` | Lấy message, stage, Target và image của lần chạy |
| `MessageName == Update`, stage `10`, mode `0` | Chỉ xử lý Update, PreValidation, Synchronous đúng cấu hình bài |
| Kiểm tra Target và `LogicalName` | Không đọc nhầm payload hoặc table khác |
| `!target.Contains(CodeColumn)` | Caller không gửi Code thì không có ý định sửa cột này |
| `PreEntityImages.Contains(ImageAlias)` | Nếu quên đăng ký image, báo lỗi cấu hình thay vì âm thầm bỏ qua rule |
| `before.GetAttributeValue<string>(...)` | Đọc Code cũ dưới dạng chuỗi từ snapshot |
| `target.GetAttributeValue<string>(...)` | Đọc Code mới; explicit null vẫn là đề nghị xóa Code |
| `IsNullOrWhiteSpace(oldCode)` | Không kết luận đổi mã khi không có baseline; cần kiểm tra image hoặc record cũ |
| `StringComparison.Ordinal` | So sánh chính xác, có phân biệt hoa/thường; đây là chính sách của bài |
| `return` khi bằng nhau | Cho phép request gửi lại cùng Code, tránh chặn replay không đổi mã |
| `trace?.Trace(...)` | Ghi correlation ID nếu có tracing service; không phải popup |
| `throw new InvalidPluginExecutionException(...)` | Chặn lưu và trả thông báo nghiệp vụ cho UI |

Vì sao chọn PreValidation? Mục tiêu là từ chối thay đổi trước thao tác lưu.
Update hỗ trợ Pre Image ở stage này. Synchronous giúp UI nhận kết quả ngay
khi Save. Trong request lồng nhau, PreValidation vẫn có thể nằm trong transaction;
không coi stage 10 là đảm bảo luôn ở ngoài transaction.

Filtering Attributes chỉ kiểm tra cột có trong payload; nó không đảm bảo giá trị
thực sự đã thay đổi. Vì vậy vẫn cần so sánh `oldCode` và `newCode` trong code.
[Microsoft: filtering attributes](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/best-practices/business-logic/include-filtering-attributes-plugin-registration).

## 6. Build DLL mới

Mở `plugins/FMCentralBms.Plugins/FMCentralBms.Plugins.csproj`. Nếu version hiện
tại vẫn là `1.0.0.0`, đổi hai dòng thành:

```xml
<AssemblyVersion>1.0.0.1</AssemblyVersion>
<FileVersion>1.0.0.1</FileVersion>
```

Nếu đã có version mới hơn, tăng phần cuối của version hiện tại. Giữ nguyên
major/minor, tên assembly và signing key. Không thay cả nội dung `.csproj` bằng
hai dòng trên. Tăng revision giúp nhận biết bản build; PRT cũng hỗ trợ update
assembly cùng version. Quy tắc SDK helper của repo là trường hợp riêng.

```powershell
dotnet build .\plugins\FMCentralBms.Plugins\FMCentralBms.Plugins.csproj -c Release
Get-Item .\plugins\FMCentralBms.Plugins\bin\Release\net48\FMCentralBms.Plugins.dll |
    Select-Object FullName, Length, LastWriteTime
```

Chỉ tiếp tục khi build thành công. File chọn trong PRT là:

```text
D:\metasys-poc\plugins\FMCentralBms.Plugins\bin\Release\net48\FMCentralBms.Plugins.dll
```

| File | Làm gì với nó? |
| --- | --- |
| `PreventEquipmentCodeChange.cs` | Source bạn vừa viết; giữ trong project |
| `RequireEquipmentBuilding.cs` | Giữ lại để rule Building cũ vẫn có trong DLL |
| `FMCentralBms.Plugins.dll` trong `bin/Release/net48` | Chọn file này để Update Assembly |
| `.snk` | Dùng local khi ký DLL; không upload hay commit key |
| `Microsoft.Xrm.Sdk.dll` và các DLL SDK | Không chọn làm plugin assembly |
| DLL trong `dataverse/FMCentralBms/PluginAssemblies` | Bản export cũ, chưa tự cập nhật sau khi bạn build |

## 7. Update assembly trong PRT

1. Kết nối đúng Developer environment, chọn chế độ hiển thị theo Assembly.
2. Tìm assembly **FMCentralBms.Plugins** đã có.
3. Chọn assembly → **Update** (hoặc Update Assembly trong menu chuột phải).
4. Browse đến DLL ở bước 6; kiểm tra class mới xuất hiện và giữ class cũ.
5. Thực hiện Update, Refresh danh sách.
6. Mở rộng assembly, phải thấy cả `RequireEquipmentBuilding` và
   `PreventEquipmentCodeChange`.

Không Unregister assembly cũ: hai step kiểm tra Building đang gắn vào nó.
Không tạo assembly khác chỉ để làm bài này. PRT upload bytes của DLL lên
Dataverse; từ đó server có class mới, nhưng **class chưa có step thì chưa chạy**.

Lệnh `--register-plugin` của worker hiện chỉ tạo type/steps cho
`RequireEquipmentBuilding`. Không dùng helper đó thay các bước PRT của bài 02.

## 8. Đăng ký một Update step

Chuột phải class **PreventEquipmentCodeChange → Register New Step**:

| Field trong PRT | Giá trị |
| --- | --- |
| Message | `Update` |
| Primary Entity | `fmc_bmsequipment` |
| Secondary Entity | Để trống |
| Filtering Attributes | Chỉ chọn `fmc_equipmentcode` |
| Event Handler | `FMCentralBms.Plugins.PreventEquipmentCodeChange` |
| Name | `BMS: Prevent Equipment Code Change` |
| Run in User's Context | `Calling User` |
| Event Pipeline Stage | `PreValidation` (10) |
| Execution Mode | `Synchronous` (0) |
| Execution Order | `20` |
| Deployment | `Server` |
| Unsecure/Secure Configuration | Để trống |

Bấm **Register New Step**. Không tạo Create step: mã phải được phép nhập ở lần
tạo đầu tiên. Order 20 giúp xác định thứ tự với rule Building cũ ở rank 10;
hai rank không có nghĩa là plugin nào chạy lâu hơn.

Chưa test Save ở thời điểm này; hoàn tất image ở bước kế tiếp trước.

## 9. Đăng ký Pre Image — bước dễ bị quên

1. Chọn chính step **BMS: Prevent Equipment Code Change**, không chọn assembly.
2. Chuột phải → **Register New Image**.
3. Điền:

| Field | Giá trị |
| --- | --- |
| Image Type | Chọn `Pre Image`, không chọn Post Image |
| Name | `Before` |
| Entity Alias | `Before` — phải khớp `ImageAlias` trong C# |
| Parameters / Attributes | Chỉ chọn `fmc_equipmentcode`; bỏ chọn All Columns |
| Message Property Name, nếu công cụ hiển thị | `Target` |

4. Bấm **Register Image**.
5. Mở rộng step để kiểm tra image vừa tạo. Kiểm tra step đang **Enabled**.

`Name` giúp bạn nhận ra image trên UI; **Entity Alias** mới là key code dùng
để đọc snapshot. Đặt Name đúng nhưng Alias sai vẫn gây lỗi cấu hình.

## 10. Test hoàn toàn trong app

Mở [FMC BMS Demo](https://org06cbc9ec.crm5.dynamics.com/main.aspx?appid=d19f4897-d227-4df3-8361-988f97c53e89).

### 10.1. Tạo record test

1. BMS Equipment → New → chọn form **Information** nếu có bộ chọn form.
2. Tab General: Name = `Demo Lock Code`.
3. Tab BMS Details and Relationships:
   Equipment Code = `UI-LOCK-001` (hoặc một mã mới chưa tồn tại),
   Equipment Type = `Test Rig`, Building = một Building đang có.
4. Save; record phải được tạo. Ghi lại Code ban đầu để khôi phục giá trị trên
   form khi thử sửa thất bại.

### 10.2. Thử đổi mã

1. Trên record đã lưu, đổi Equipment Code từ `UI-LOCK-001` sang `UI-LOCK-002`.
2. Bấm ra ngoài ô để hoàn tất nhập rồi Save.
3. Mong đợi hộp thoại có `BMS-EQUIPMENT-002`; server giữ mã cũ.
4. Đóng hộp thoại. Ô trên form có thể vẫn chứa mã mới chưa lưu. Nhập lại mã
   ban đầu rồi Save, hoặc bỏ các thay đổi chưa lưu và mở lại record.
5. Đổi Name thành `Demo Lock Code - Renamed` → Save: phải thành công.

Đừng dùng Code của record SQL đang sync để thử bài. Các test này tạo dữ liệu
thật trong Dataverse. Tên thiết bị từ SQL cũng có thể được worker ghi đè ở lần
sync tiếp theo, nên dùng record demo riêng.

### 10.3. Bảng nghiệm thu

| Test | Expected | Bạn ghi kết quả |
| --- | --- | --- |
| Tạo Equipment mới, có đủ Building | Thành công | |
| Đổi Code sang mã chưa có | Lỗi `BMS-EQUIPMENT-002` | |
| Đổi Name, giữ Code | Thành công | |
| Đổi Description, giữ Code | Thành công | |
| Đổi chữ hoa thành chữ thường trong Code | Lỗi `BMS-EQUIPMENT-002` | |
| Mở lại sau khi bỏ thay đổi bị chặn | Code cũ vẫn còn | |
| Tạo thiếu Building trên form Plugin Test bài 01 | Lỗi `BMS-EQUIPMENT-001` vẫn hoạt động | |

UI có thể không gửi cột khi giá trị cuối không đổi; vì vậy gõ lại cùng Code và
Save thành công chưa chứng minh branch “same value” đã chạy. Branch đó cần
kiểm thử handler hoặc request integration có gửi lại Code nếu nghiệm thu kỹ hơn.

## 11. Đưa step vào solution và giữ bản export

1. Maker Portal → đúng environment → Solutions → `FMCentralBms`.
2. Trong Plug-in assemblies, kiểm tra assembly `FMCentralBms.Plugins` đã có.
3. **Add existing → More → Developer → Plug-in step**; chọn
   **BMS: Prevent Equipment Code Change** rồi Add.
4. Kiểm tra cả hai step Building cũ và step mới cùng nằm trong solution.

Assembly đã ở solution không tự làm step mới thành thành viên solution.
Image là thành phần con của step; kiểm tra image đi cùng khi export.

Export vào thư mục mới để không đè source đang sửa:

```powershell
$lessonStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$lessonExport = Join-Path 'D:\metasys-poc\.artifacts' "plugin-code-lock-$lessonStamp"
New-Item -ItemType Directory -Path $lessonExport | Out-Null
pac solution export --name FMCentralBms --managed false `
    --environment https://org06cbc9ec.crm5.dynamics.com/ `
    --path (Join-Path $lessonExport 'FMCentralBms.zip')
if ($LASTEXITCODE -ne 0) { throw 'Export failed; do not unpack.' }
pac solution unpack --zipfile (Join-Path $lessonExport 'FMCentralBms.zip') `
    --folder (Join-Path $lessonExport 'unpacked') --packagetype Unmanaged
if ($LASTEXITCODE -ne 0) { throw 'Unpack failed.' }
```

So sánh export với `dataverse/FMCentralBms`, merge DLL/assembly metadata, step
mới, image `Before` và manifest liên quan. Không copy đè toàn bộ export nếu có
thay đổi cloud ngoài bài. Kiểm tra XML step có cột image `fmc_equipmentcode`,
stage 10, mode 0, rank 20, filter đúng cột và type name đúng class mới.

## 12. Khi có lỗi và cách tạm tắt bài thực hành

| Hiện tượng | Kiểm tra |
| --- | --- |
| Không thấy class mới trong PRT | Build đúng project/configuration và Update đúng DLL; Refresh |
| Đổi mã vẫn lưu được | Step Enabled, đúng class/table/Update, stage 10, mode 0, filter Code |
| `BMS-EQUIPMENT-CONFIG-002: Thieu Pre Image...` | Đăng ký Pre Image và alias `Before` đúng chữ hoa/thường |
| Báo không đọc được mã cũ | Image có chọn Code chưa? Record gốc có mã chưa? |
| Thấy `BMS-EQUIPMENT-001` | Thiếu/xóa Building; điền lại Building để test riêng rule mã |
| Báo duplicate key | Dùng mã mới chưa tồn tại để tránh lẫn lỗi key với bài này |
| Name không lưu sau lần đổi mã bị chặn | Form còn giữ Code mới chưa lưu; khôi phục Code cũ trước |
| PRT không mở vì lỗi NuGet | Hoàn tất cài/mở công cụ trước, không đăng ký bằng một helper không hỗ trợ rule |

Để tạm tắt bài: trong PRT chọn **đúng step BMS: Prevent Equipment Code Change**
→ Disable. Không tắt hai step Building hoặc Unregister cả assembly.
Bật lại step sau khi sửa image/cấu hình. Nếu định đổi mã thật, phải thiết kế
reconciliation/migration với SQL; không dùng việc Disable như quy trình đổi mã.

## 13. Phạm vi và nguồn tham khảo

Bài này cung cấp source để bạn tự tạo file và làm theo. Việc soạn tài liệu
không thêm class vào project đang chạy, không update DLL trên cloud và không
đăng ký step. Các kết quả UI là tiêu chí mong đợi sau khi bạn triển khai.

- [Bài 01: build và đăng ký plugin từng bước](../runbooks/dataverse-plugin-step-by-step.vi.md).
- [App và form test đã triển khai](../runbooks/bms-demo-app.vi.md).
- [Microsoft: register plug-in, images, version và solution membership](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in).
- [Microsoft: exception của synchronous plugin trên UI](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/handle-exceptions).
