# Thiết kế Dataverse plug-in cho BMS và hướng dẫn plug-in đầu tiên

Ngày: 2026-09-10. Trạng thái: đã triển khai và kiểm thử trên Developer
environment; plug-in nằm trong solution source và deployment receipt ở mục 11.

Tài liệu nói về **Dataverse plug-in C#**. Mục tiêu đầu tiên là bảo vệ quan hệ
Equipment → Building khi dữ liệu được ghi từ form, API hoặc worker hiện tại.

Để tự làm lại với giải thích từng dòng code và từng ô đăng ký, dùng
[runbook từng bước từ local đến Dataverse](../runbooks/dataverse-plugin-step-by-step.vi.md).

## 1. Dữ liệu và code đang có

Kiểm tra chỉ đọc ngày 2026-09-09:

| Nguồn | Kết quả quan sát |
| --- | --- |
| Fake API tại port 5100 | 3 Building, 4 Equipment, 5 Point |
| SQL `FM_Central` | 6 Building, 8 Equipment; snapshot 28.160 readings |
| SQL lịch sử | 112 `object_id` khác nhau; không chỉ 5 Point mặc định |
| Quan hệ SQL Equipment → Building | Không có Equipment thiếu Building |
| Dataverse | Thiết kế dưới đây dựa trên provisioner và solution export trong repo; chưa kiểm tra live registrations trong lần chuẩn bị này |

Số readings có thể tăng khi ingestion chạy. Các catalog thêm có mã như
`BLD-A-A53E1DBF` và `EQ-WA-A53E1DBF`; không dùng tổng số hàng bằng 3/4/5 làm
điều kiện pass cho toàn bộ dữ liệu hiện tại.

Bộ fake mặc định:

| Building | Equipment | Point |
| --- | --- | --- |
| `BLDG-A` | `EQ-A-WM-001` | `WATER-001` |
| `BLDG-A` | `EQ-A-TS-001` | `TEMP-001` |
| `BLDG-B` | `EQ-B-WM-002` | `WATER-002` |
| `BLDG-TEST` | `EQ-TEST-RIG-001` | `TEST-POWER-AUTOMATE-001`, `TEST-POWER-AUTOMATE-002` |

SQL có ba bảng nghiệp vụ `raw.bms_building`, `raw.bms_equipment`,
`raw.bms_reading`. Dataverse có ba bảng standard cho Building, Equipment,
Point; thêm `fmc_bmsreading` elastic để giữ history theo retention.
`fmc_syncrequest` và delivery ledger phục vụ vận hành.

Các điểm quyết định thiết kế:

- [Fake store](../../FakeMetasysApi/Services/MetasysPointStore.cs) định nghĩa catalog và mapping mặc định.
- [Provisioner](../../DataverseSyncWorker/Services/DataverseProvisioner.BmsRelations.cs) đặt `fmc_buildingid` là `ApplicationRequired`; Point → Equipment vẫn optional để hỗ trợ dữ liệu cũ.
- [ReadingMapper](../../DataverseSyncWorker/Services/ReadingMapper.cs) tạo GUID từ SourceId và source key; payload Equipment luôn có Building lookup.
- [DataverseWriter](../../DataverseSyncWorker/Services/DataverseWriter.cs) dùng `UpsertRequest` cho standard tables; elastic history dùng `UpsertMultiple`.
- [SyncEngine](../../DataverseSyncWorker/Services/SyncEngine.cs) ghi Building, Equipment rồi Point/history; SQL acknowledgement sau khi các write cần thiết thành công.

## 2. Nên làm những plug-in nào?

| Ưu tiên | Plug-in đề xuất | Quy tắc và nơi chạy | Quyết định |
| --- | --- | --- | --- |
| 1 | `RequireEquipmentBuilding` | Equipment Create/Update: bắt buộc có Building, chặn chủ động xóa lookup | Làm đầu tiên; một class, hai steps, không cần đọc thêm dữ liệu |
| 2 | `ProtectBmsSourceIdentity` | Update Building/Equipment/Point: chặn đổi `fmc_buildingcode`, `fmc_equipmentcode`, `fmc_objectid` đã lưu | Hữu ích để bảo vệ GUID mapping; dùng pre-image so sánh, vẫn cho ghi lại cùng giá trị |
| 3 | `ValidatePointEquipmentBuilding` | Khi gán/đổi Equipment cho Point: kiểm tra Building nguồn và Building của Equipment nhất quán | Làm khi đã chốt quy tắc; phải xét cả đổi Building trên Equipment và dữ liệu cũ chưa có lookup |

Chưa cần plug-in chống trùng code hoặc chống xóa Building còn Equipment:
alternate keys và quan hệ `Restrict Delete` đã được provisioner định nghĩa.
Không tự trim/uppercase source key trong Dataverse vì SQL và deterministic GUID
vẫn dựa trên identity nguồn.

Cảnh báo vượt ngưỡng là tính năng tiếp theo khi có ngưỡng, đơn vị, chống lặp
và nơi nhận cụ thể. Có thể dùng xử lý bất đồng bộ/flow cho thông báo; không thêm
truy vấn hoặc gửi thông báo vào mỗi write history trong bài đầu tiên.

Vị trí chạy: worker gửi Equipment vào Dataverse; Dataverse gọi plug-in trước
khi lưu. Hợp lệ thì tiếp tục lưu, vi phạm thì trả lỗi cho bên gọi.
SQL ingestion và trigger sync tiếp tục do các service hiện có phụ trách.

## 3. Vì sao chọn RequireEquipmentBuilding?

Ví dụ hợp lệ: `EQ-A-WM-001` có lookup tới `BLDG-A`.
Ví dụ bị chặn: tạo Equipment không có `fmc_buildingid`, hoặc gửi update đặt lookup
này thành `null`.

`ApplicationRequired` làm cột bắt buộc trong ứng dụng nhưng không tự bảo đảm
yêu cầu này ở mọi API write. Vì vậy server validation có ích ngay cả khi form
đã hiển thị dấu bắt buộc. [Microsoft: column requirement level](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/entity-attribute-metadata#column-requirement-level).

Phạm vi bản đầu:

- Create phải có lookup; Update chỉ kiểm tra khi payload có `fmc_buildingid`.
- Update chỉ đổi tên phải thành công, kể cả payload không gửi lại Building.
- Không quét hoặc sửa dữ liệu cũ; Equipment cũ bị thiếu lookup cần một bước xử lý riêng.
- Dataverse tự kiểm tra bản ghi được tham chiếu; plug-in này không kiểm tra Building đang active hoặc các điều kiện liên bảng khác.
- Không tự ghi ngược SQL, sửa current value, thay GUID hoặc tác động retention.

## 4. Hiểu C# theo cách quen thuộc từ Python

| Thành phần | Cách hiểu |
| --- | --- |
| File `.cs` | Source code, tương tự file `.py` |
| Class triển khai `IPlugin` | Handler để Dataverse gọi |
| `Execute(IServiceProvider)` | Hàm entry point; nền tảng cung cấp context |
| `Target` | Các cột gửi trong request, giống dictionary của request body |
| File `.csproj` | Cấu hình build, framework và dependency NuGet |
| `dotnet build` | Compile source thành assembly `.dll` |
| Assembly | Một DLL có thể chứa nhiều class plug-in |
| Step | Đăng ký class nào chạy trên table/message/stage nào |
| Solution ZIP | Gói mang assembly và steps sang môi trường khác |

Plug-in chạy trong Dataverse sandbox. Bạn không mở thêm port hoặc deploy Windows
Service cho class này. Plug-in validation mẫu không cần client secret hay token.
Tạo bảng/cột vẫn thuộc provisioner hoặc solution schema; build DLL không tạo bảng.

Microsoft hỗ trợ plug-in .NET Framework 4.6.2–4.8 và hiện khuyến nghị 4.8.
Máy này có PAC 2.11.2; template còn sinh `net462` nên bài này đổi thành `net48`.
Worker hiện dùng `net10.0` và là project riêng.
[Microsoft: supported frameworks](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/supported-customizations#support-for-net-framework-versions).

## 5. Tạo project và code

Chạy PowerShell từ `D:\metasys-poc`. Tạo project một lần:

```powershell
pac plugin init --outputDirectory .\plugins\FMCentralBms.Plugins
```

Trong `plugins/FMCentralBms.Plugins/FMCentralBms.Plugins.csproj`, sửa:

```xml
<TargetFramework>net48</TargetFramework>
```

Giữ `SignAssembly=true` và `AssemblyOriginatorKeyFile` do template sinh.
DLL đăng ký trực tiếp cần strong-name signing. Giữ cùng signing key cho các
lần cập nhật; signing key này không phải credential đăng nhập Dataverse.
Bài này chỉ dùng SDK có sẵn trong sandbox nên đăng ký DLL là đủ.
[Microsoft: build and package](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/build-and-package).

Xóa file mẫu `Plugin1.cs`, tạo `RequireEquipmentBuilding.cs` với toàn bộ nội dung sau.
`PluginBase.cs` do template sinh có thể giữ lại; class dưới đây dùng trực tiếp `IPlugin`.

```csharp
using System;
using Microsoft.Xrm.Sdk;

namespace FMCentralBms.Plugins
{
    public sealed class RequireEquipmentBuilding : IPlugin
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
                target.LogicalName != "fmc_bmsequipment")
                return;

            // Update payload chi chua cac cot duoc gui len.
            if (isUpdate && !target.Contains("fmc_buildingid"))
                return;

            if (target.GetAttributeValue<EntityReference>("fmc_buildingid") == null)
            {
                var trace = (ITracingService)serviceProvider.GetService(
                    typeof(ITracingService));
                trace?.Trace("RequireEquipmentBuilding blocked {0}; CorrelationId={1}",
                    context.MessageName, context.CorrelationId);

                throw new InvalidPluginExecutionException(
                    "BMS-EQUIPMENT-001: Equipment phai thuoc mot Building. " +
                    "Hay chon Building truoc khi luu.");
            }
        }
    }
}
```

Điểm dễ nhầm: `Target` của Update không phải toàn bộ record. Thiếu key nghĩa là
request không đụng tới cột; có key nhưng giá trị null nghĩa là yêu cầu xóa lookup.
Bản này không cần pre-image và không gọi `service.Update` bên trong plug-in.

Class không giữ state của từng request trong field, phù hợp cách Dataverse dùng
lại instance. [Microsoft: write a plug-in](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/write-plug-in).

Build:

```powershell
dotnet build .\plugins\FMCentralBms.Plugins\FMCentralBms.Plugins.csproj -c Release
```

DLL cần dùng:
`plugins\FMCentralBms.Plugins\bin\Release\net48\FMCentralBms.Plugins.dll`.

Template có thể sinh thêm `.nupkg`; hướng dẫn này đăng ký DLL.
Khi đưa project vào implementation của repo, thêm project vào solution rồi build cả solution:

```powershell
dotnet sln .\MetasysPoc.sln add .\plugins\FMCentralBms.Plugins\FMCentralBms.Plugins.csproj
dotnet build .\MetasysPoc.sln -c Release
```

## 6. Đăng ký vào Developer environment

Các bước từ đây là thao tác ghi cloud khi bạn thực hành. Target của bài:

| Thuộc tính | Giá trị |
| --- | --- |
| URL | `https://org06cbc9ec.crm5.dynamics.com/` |
| Environment ID | `5abcb0e5-99b2-e51f-aa0e-90d84405798b` |
| Organization ID | `ab191700-b99e-f111-aaa0-000d3a80bb96` |
| Solution / publisher | `FMCentralBms` / `FMCentralBmsPublisher` |

Kiểm tra danh tính rồi mở Plug-in Registration Tool:

```powershell
pac auth list
pac org who --environment https://org06cbc9ec.crm5.dynamics.com/
pac tool prt
```

Trong PRT, đăng nhập bằng tài khoản developer đã có và kiểm tra lại URL tổ chức;
PRT có phiên kết nối riêng. Tài khoản cần quyền đăng ký plug-in.

Repo hiện có command đăng ký idempotent dùng `DataverseConnection`, nên lần
triển khai này không phụ thuộc PRT interactive:

```powershell
dotnet run --project .\DataverseSyncWorker -c Release --no-launch-profile -- `
  --register-plugin `
  --plugin-path=..\plugins\FMCentralBms.Plugins\bin\Release\net48\FMCentralBms.Plugins.dll
```

Command chỉ chạy khi truyền `--register-plugin`; worker startup bình thường
không tự đăng ký hoặc thay đổi plug-in.

Chọn **Register → Register New Assembly**, chọn DLL đã build,
isolation **Sandbox**, location **Database**, rồi đăng ký class
`FMCentralBms.Plugins.RequireEquipmentBuilding`.

Chọn class, **Register New Step** hai lần:

| Cấu hình | Step Create | Step Update |
| --- | --- | --- |
| Name | `BMS: Require Building on Equipment Create` | `BMS: Require Building on Equipment Update` |
| Message | `Create` | `Update` |
| Primary Entity | `fmc_bmsequipment` | `fmc_bmsequipment` |
| Secondary Entity | Để trống | Để trống |
| Filtering Attributes | Để trống | Chỉ `fmc_buildingid` |
| Stage | `PreValidation` (10) | `PreValidation` (10) |
| Mode | `Synchronous` | `Synchronous` |
| Execution Order | `10` | `10` |
| Run in User's Context | `Calling User` | `Calling User` |
| Deployment | `Server` | `Server` |
| Images / configuration | Không cần | Không cần |

Đây là đăng ký cụ thể cho quy tắc của repo.
[Microsoft: register a plug-in](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in).

`InvalidPluginExecutionException` trong synchronous step hủy thao tác và đưa
thông báo lỗi cho bên gọi. `PreValidation` phù hợp với việc từ chối request;
trong lời gọi lồng nhau nó vẫn có thể nằm trong transaction đang có.
[Microsoft: handle exceptions](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/handle-exceptions).

Worker đang upsert **standard Equipment**, nên Dataverse thực hiện nhánh Create
hoặc Update tương ứng và gọi các steps này. Không cần thêm một step Upsert cho
đường ghi hiện tại. Elastic Upsert có hành vi khác; không sao chép cấu hình này
sang `fmc_bmsreading`.
[Microsoft: Upsert behavior](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/use-upsert-insert-update-record).

## 7. Test API: thiếu Building, có Building, xóa lookup, đổi tên

Form có thể chặn trước khi tới server nên chỉ test bằng form chưa chứng minh
plug-in chạy. Đoạn PowerShell dưới đây gọi API bằng developer token helper hiện có,
tạo một Equipment `DEMO-PLUGIN-...` riêng, và xóa đúng record demo đó sau test.
Không sử dụng code Equipment có trong SQL cho record demo.

Chạy sau khi đã đăng ký hai steps. Helper cần phiên Azure CLI còn đăng nhập.
Token được giữ trong biến, không in ra terminal.

```powershell
$pluginConfig = Get-Content .\DataverseSyncWorker\appsettings.json -Raw | ConvertFrom-Json
$pluginOrg = 'https://org06cbc9ec.crm5.dynamics.com'
if ($pluginConfig.Dataverse.Url.TrimEnd('/') -ne $pluginOrg) {
    throw 'Worker config khong tro toi Developer organization cua bai demo.'
}
$pluginToken = & $pluginConfig.Dataverse.DeveloperTokenPython $pluginConfig.Dataverse.DeveloperTokenScript
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($pluginToken)) {
    throw 'Khong lay duoc developer token.'
}
$pluginHeaders = @{
    Authorization = "Bearer $pluginToken"
    Accept = 'application/json'
    'OData-Version' = '4.0'
    'OData-MaxVersion' = '4.0'
}
function Invoke-BmsPluginDemoApi {
    param([string]$Method, [string]$Path, $Body)
    $request = @{
        Uri = "$pluginOrg/api/data/v9.2/$Path"
        Method = $Method
        Headers = $pluginHeaders.Clone()
        ErrorAction = 'Stop'
    }
    if ($Method -eq 'PATCH') { $request.Headers['If-Match'] = '*' }
    if ($null -ne $Body) {
        $request.ContentType = 'application/json'
        $request.Body = $Body | ConvertTo-Json -Depth 5 -Compress
    }
    Invoke-RestMethod @request
}
function Assert-BmsPluginRejected {
    param([scriptblock]$Operation)
    try { & $Operation | Out-Null }
    catch {
        $detail = "$($_.ErrorDetails.Message) $($_.Exception.Message)"
        if ($detail -notmatch 'BMS-EQUIPMENT-001') { throw }
        Write-Host 'PASS: server tra loi BMS-EQUIPMENT-001.'
        return
    }
    throw 'FAIL: request khong bi chan. Kiem tra assembly va hai steps.'
}

$pluginWho = Invoke-BmsPluginDemoApi GET 'WhoAmI'
if ($pluginWho.OrganizationId -ne 'ab191700-b99e-f111-aaa0-000d3a80bb96') {
    throw 'Sai organization; dung test.'
}
$pluginBuildings = Invoke-BmsPluginDemoApi GET 'fmc_bmsbuildings?$select=fmc_bmsbuildingid&$filter=fmc_buildingcode eq ''BLDG-A'''
if (@($pluginBuildings.value).Count -ne 1) { throw 'Can dung mot Building BLDG-A.' }

$pluginBuildingId = $pluginBuildings.value[0].fmc_bmsbuildingid
$pluginDemoId = [guid]::NewGuid().ToString()
$pluginDemoPath = "fmc_bmsequipments($pluginDemoId)"
$pluginDemoBody = @{
    fmc_bmsequipmentid = $pluginDemoId
    fmc_equipmentcode = "DEMO-PLUGIN-$pluginDemoId"
    fmc_name = 'Plugin demo Equipment'
    fmc_equipmenttype = 789100002
}
try {
    # 1. Create thieu Building: phai bi plug-in chan.
    Assert-BmsPluginRejected {
        Invoke-BmsPluginDemoApi POST 'fmc_bmsequipments' $pluginDemoBody
    }

    # 2. Create co Building: phai thanh cong.
    $pluginDemoBody['fmc_buildingid@odata.bind'] = "/fmc_bmsbuildings($pluginBuildingId)"
    Invoke-BmsPluginDemoApi POST 'fmc_bmsequipments' $pluginDemoBody | Out-Null
    Write-Host 'PASS: create co Building.'

    # 3. Clear lookup: phai bi plug-in chan.
    Assert-BmsPluginRejected {
        Invoke-BmsPluginDemoApi PATCH $pluginDemoPath @{ fmc_buildingid = $null }
    }

    # 4. Doi ten, khong gui lookup: phai thanh cong.
    Invoke-BmsPluginDemoApi PATCH $pluginDemoPath @{ fmc_name = 'Plugin demo renamed' } | Out-Null
    $pluginSaved = Invoke-BmsPluginDemoApi GET ($pluginDemoPath + '?$select=fmc_name,_fmc_buildingid_value')
    if ($pluginSaved.fmc_name -ne 'Plugin demo renamed' -or
        $pluginSaved._fmc_buildingid_value -ne $pluginBuildingId) {
        throw 'FAIL: ten hoac Building sau update khong dung.'
    }
    Write-Host 'PASS: doi ten thanh cong, Building duoc giu nguyen.'
}
finally {
    # Chi xoa GUID demo vua tao, ke ca khi test phat hien step chua hoat dong.
    $pluginDemoRows = Invoke-BmsPluginDemoApi GET ("fmc_bmsequipments?" +
        '$select=fmc_bmsequipmentid&$filter=fmc_bmsequipmentid eq ' + $pluginDemoId)
    if (@($pluginDemoRows.value).Count -gt 0) {
        Invoke-BmsPluginDemoApi DELETE $pluginDemoPath | Out-Null
        Write-Host "Da xoa Equipment demo $pluginDemoId."
    }
    $pluginHeaders.Clear()
    $pluginToken = $null
}
```

`789100002` là `TestRig` trong mapping hiện tại. Navigation property
`fmc_buildingid` và entity set `fmc_bmsequipments` được kiểm tra từ solution export.
Web API nhận lookup khi bind và cho yêu cầu clear bằng navigation property null;
custom step của bài này phải từ chối thao tác clear.
[Microsoft: associate/disassociate using Web API](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/associate-disassociate-entities-using-web-api).

Nếu không thấy mã lỗi riêng, kiểm tra đúng URL, hai steps đã Enabled, class,
Stage=10, Mode=Synchronous và Update filter. Có thể bật Plug-in Trace Log ở mức
Exception trong môi trường demo để xem trace và CorrelationId.
[Microsoft: debug plug-ins](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/debug-plug-in).

## 8. Test cùng flow Fake → ingestion → SQL → Dataverse

1. Bật fake và ingestion theo [demo guideline](../runbooks/demo-guideline.vi.md).
2. Để ingestion ghi ít nhất một reading mới có Equipment mapping; xác nhận có pending trước khi enqueue.
3. Giữ worker ở CommandDriven và chạy flow `FMC - Request SQL to Dataverse Sync`.
4. Hoặc dùng hai lệnh sau nếu đang thao tác bằng CLI. Worker đang chạy có thể claim request trước lệnh process; theo dõi request được enqueue để kết luận.

```powershell
dotnet run --project .\DataverseSyncWorker -c Release -- --enqueue
dotnet run --project .\DataverseSyncWorker -c Release -- --process-command-once
dotnet run --project .\DataverseSyncWorker -c Release -- --verify
```

Kỳ vọng: request hoàn tất, log có catalog synchronized, Equipment vẫn có Building,
Point vẫn có Equipment mapping, pending trong cutoff được xử lý.
`--verify` chỉ kiểm tra mẫu retained history và ledger, không chứng minh toàn bộ
112 Object ID đã được đối soát. Các lỗi/dead-letter có sẵn cần phân biệt với
lỗi do plug-in mới.

**Khoảng trống đang có trong code:** [CommandProcessor.cs](../../DataverseSyncWorker/Services/CommandProcessor.cs)
kết thúc request trước khi gọi `SyncEngine` nếu `EligiblePendingRows == 0`.
Do đó trigger chỉ có thay đổi catalog và không có reading pending có thể báo
Succeeded mà chưa ghi lại catalog. Cần sửa worker riêng để bảo đảm mỗi request
chạy catalog ít nhất một lần; bài plug-in này chưa thực hiện sửa đó.

Nếu plug-in từ chối Equipment, worker hiện có thể đánh dấu request Failed;
không tự coi lỗi này là dead-letter từng reading. Raw SQL vẫn còn, batch bị lỗi
chưa được acknowledge; các parent writes thành công trước đó có thể đã lưu trên
Dataverse. Sửa nguyên nhân và replay qua ledger hiện có, không reset history/IDs.

## 9. Đưa plug-in vào solution và triển khai

Trong maker portal, chọn đúng Developer environment → Solutions → `FMCentralBms`:

1. **Add existing → More → Developer → Plug-in assembly**: thêm `FMCentralBms.Plugins`.
2. **Add existing → More → Developer → Plug-in step**: thêm riêng cả hai steps.
3. Kiểm tra danh sách có một assembly và đúng hai steps Enabled; tăng solution version trước release.

Chỉ đăng ký DLL bằng PRT hoặc chỉ thêm assembly chưa bảo đảm steps nằm trong
solution export. [Microsoft: add assembly and steps to a solution](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in#add-your-assembly-to-a-solution).

Export vào thư mục mới để review:

```powershell
$pluginRelease = Join-Path '.artifacts' ('plugin-release-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $pluginRelease | Out-Null
pac solution export --name FMCentralBms --environment https://org06cbc9ec.crm5.dynamics.com/ --path "$pluginRelease\FMCentralBms_unmanaged.zip" --managed false
```

Sau khi export thành công, unpack:

```powershell
pac solution unpack --zipfile "$pluginRelease\FMCentralBms_unmanaged.zip" --folder "$pluginRelease\unpacked" --packagetype Unmanaged
git diff --no-index -- .\dataverse\FMCentralBms "$pluginRelease\unpacked"
```

`git diff --no-index` trả exit code 1 khi có diff. Review assembly/steps/solution
manifest; sau đó cập nhật source đã kiểm tra vào `dataverse/FMCentralBms` theo
[quy trình solution](../../.agents/skills/dv-solution/SKILL.md). Không tự sửa XML
để giả lập việc đăng ký cloud.

Khi đã chọn môi trường Test/UAT và chiến lược managed cho đích, export managed:

```powershell
pac solution export --name FMCentralBms --environment https://org06cbc9ec.crm5.dynamics.com/ --path "$pluginRelease\FMCentralBms_managed.zip" --managed true
```

Đặt URL đích thật vào biến trước khi chạy; không import lại managed ZIP này vào
Developer đang giữ unmanaged source:

```powershell
$pluginTargetUrl = 'https://YOUR-TEST-ORG.crm5.dynamics.com'
pac org who --environment $pluginTargetUrl
pac solution import --environment $pluginTargetUrl --path "$pluginRelease\FMCentralBms_managed.zip" --activate-plugins --publish-changes
```

Chờ import thành công, kiểm tra version, assembly và hai steps rồi chạy lại test
với URL/OrganizationId và auth của đích. Test script ở phần 7 cố ý khóa Developer,
nên cần điều chỉnh đầy đủ trước khi dùng môi trường khác.

Nếu đích chưa có `BLDG-A`, cần đưa dữ liệu qua pipeline đã cấu hình cho đích.
Solution ZIP chứa schema/code/configuration components; không tự copy dữ liệu
Building/Equipment/Point. Khi deploy toàn bộ solution, xử lý cả dependency,
connection references và environment variables của flow hiện có theo
[deployment reference](../reference/dataverse-deployment.md).
Không tự suy ra quyền runtime/provisioning cho một môi trường mới.

## 10. Cập nhật, rollback và tiêu chí hoàn tất

Cập nhật nhỏ: sửa source, giữ namespace/class/signing key; tăng build/revision
của assembly, ví dụ `1.0.0.1`; build rồi **Update** assembly hiện có trong PRT.
Kiểm tra steps vẫn trỏ đúng class, chạy lại test và export solution version mới.
Thay major/minor có thể dẫn tới cách xử lý assembly version khác; xem
[Microsoft: update assemblies](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in#assembly-versioning).

Rollback ở Developer: disable đúng hai steps để tạm ngừng quy tắc; hoặc build
lại logic bản tốt với version cập nhật rồi Update assembly. Với managed Test/UAT,
phát hành solution sửa lỗi; không uninstall toàn bộ `FMCentralBms` chỉ để bỏ rule này.

Một lần triển khai chỉ hoàn tất khi có:

- DLL build thành công; một class và hai steps đúng cấu hình trên đúng environment.
- Hai case bị từ chối trả `BMS-EQUIPMENT-001`; create hợp lệ và update tên đều pass.
- Record demo được dọn, catalog và quan hệ thật còn nguyên.
- Một sync request có reading mới chạy thành công; verify không phát hiện hồi quy do plug-in.
- Assembly và steps nằm trong solution; có export/source diff và kết quả test của lần triển khai.

## 11. Deployment receipt — Developer environment (2026-09-10)

- Đã thêm project `plugins/FMCentralBms.Plugins` vào `MetasysPoc.sln`; giữ
  strong-name key và build target `net48` theo supported framework.
- `dotnet build .\MetasysPoc.sln -c Release --no-restore`: pass, 0 warnings,
  0 errors. 7 local handler checks cũng pass, không gọi Dataverse.
- Đã đăng ký bằng worker command `--register-plugin` vào đúng Developer
  organization `https://org06cbc9ec.crm5.dynamics.com/`, solution
  `FMCentralBms` / publisher `FMCentralBmsPublisher`.
- Assembly live: `FMCentralBms.Plugins`, ID
  `a5233bd0-bbac-f111-aaad-00224819a344`, version `1.0.0.0`, Sandbox/Database.
  Plugin type live: `FMCentralBms.Plugins.RequireEquipmentBuilding`, ID
  `00c79fe3-bbac-f111-aaad-00224819a344`.
- Hai live steps đúng plan: Create ID
  `00029f22-bcac-f111-aaad-00224819a344` và Update ID
  `27894c29-bcac-f111-aaad-00224819a344`; cả hai `PreValidation`/synchronous/
  order 10/server/enabled. Update chỉ filter `fmc_buildingid`.
- API mutation test pass: Create thiếu Building bị từ chối với
  `BMS-EQUIPMENT-001`; Create có Building pass; clear lookup bị từ chối; đổi
  tên không gửi lookup pass và giữ nguyên Building. Record demo đã được xóa.
- Đã export/unpack unmanaged solution từ live environment và merge assembly,
  two step definitions và root components vào `dataverse/FMCentralBms`.
  Source XML và DLL đã được so khớp với export.
- Sync receipt `475ba7a0-bcac-f111-aaad-00224819a344` hoàn tất `Succeeded` tại
  cutoff `81773`: `54578` delivered, `546` batches, `0` quarantined,
  `PendingAfter=0`, `DeadLetterAfter=0`.
- `--verify` sau sync: `SourceRows=81773`, `DeliveredRows=81773`,
  `PendingRows=0`, `DeadLetterRows=0`; live Building/Equipment relationships
  pass và 25 retained-history samples pass.

Giới hạn còn lại: receipt này chỉ chứng minh Developer environment và dữ liệu
POC hiện tại. Chưa import managed solution sang Test/UAT/Production, chưa cấu
hình production application identity/capacity, và simulator không chứng minh
quyền truy cập tới Johnson Controls Metasys thật. Pending có thể phát sinh lại
nếu ingestion tiếp tục tạo reading mới sau cutoff.
