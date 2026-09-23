# Build/publish Dataverse plug-in và chạy sync từ máy khác

Runbook này áp dụng cho checkout mới của `metasys-poc` trên Windows. Signing key
gốc `FMCentralBms.Plugins.snk` và cấu hình đường dẫn token helper portable đã có
trong repo; pull là có thể build plug-in cùng identity. Public key token của
assembly đã đăng ký là `e122b5e4dcc2589d`. Repository là public: ai cũng có
thể tải signing key, nhưng key này không cho phép đăng nhập Dataverse. Không
commit access token, client secret hay Azure CLI cache.

## 1. Chuẩn bị máy

1. Cài .NET SDK có thể build solution và plug-in `net48`, Python 3, Azure CLI và Power
   Platform CLI (`pac`). Cài .NET Framework 4.8 targeting pack nếu máy báo thiếu
   reference assemblies. Kiểm tra `dotnet --info`, `python --version`, `az version`,
   `pac --version`.
2. Pull `main`; xác nhận có file
   `Dataverse.Plugin/FMCentralBms.Plugins/FMCentralBms.Plugins.snk`. Đừng tạo key
   mới khi cập nhật plug-in.
3. Tài khoản cần quyền vào Developer environment và quyền đăng ký plug-in. Đăng
   nhập riêng cho Azure CLI và PAC (PRT cũng có phiên đăng nhập riêng):

```powershell
az login --tenant 31983a93-6f80-4356-a4e1-a65055e8327e --allow-no-subscriptions
pac auth create --environment https://org06cbc9ec.crm5.dynamics.com/
pac org who --environment https://org06cbc9ec.crm5.dynamics.com/
```

Đối chiếu organization ID `ab191700-b99e-f111-aaa0-000d3a80bb96`, environment ID
`5abcb0e5-99b2-e51f-aa0e-90d84405798b` và solution `FMCentralBms`. `PAC auth`
không thay cho `az login`: worker cần token có audience của Dataverse, không phải
Power Platform API.

## 2. Cấu hình riêng cho máy nhà

Sau pull, cấu hình mặc định dùng `python` trên PATH và tự tìm
`scripts/get-dataverse-token.py` trong checkout; không cần chạy setup nếu SQL
Server nằm trên `localhost`. Chạy từ root của repo:

```powershell
python .\scripts\get-dataverse-token.py --probe
```

Nếu SQL Server ở host khác hoặc Python không nằm trên PATH, chạy:

```powershell
.\scripts\Setup-HomeDataverse.ps1 -PythonPath 'C:\Python311\python.exe' -SqlServer 'TEN_SQL_SERVER'
```

Có thể thêm `-SigningKeyPath <key>` khi muốn chỉ định key khác theo chủ đích. Lệnh tạo
`Dataverse.SyncWorker/Dataverse.SyncWorker.App/appsettings.Local.json` (Git ignore).
File này chỉ chứa đường dẫn và SQL Server; không chứa token. Muốn đổi giá trị,
sửa file thủ công hoặc chạy lại setup với `-Force`. Worker và SPO CLI đều đọc
override này. `Url`, `ExpectedOrganizationId`, `SourceId` và logic mapping vẫn lấy từ config gốc;
đừng tự đổi để tránh đẩy dữ liệu vào nhầm organization hoặc thay đổi identity.

Nếu `--probe` lỗi, kiểm tra `az login`, quyền truy cập Developer environment và
Python/Azure CLI. Helper chỉ in `TOKEN_OK_<độ dài>`, không in token. Nếu máy dùng
Python environment có module `azure.cli` nhưng không có `az` trên PATH, helper có
fallback tương ứng. Giá trị `localhost` của SQL không tự nối tới database ở máy
công ty.

## 3. Kiểm tra rồi publish plug-in

```powershell
.\scripts\Publish-FmcPlugin.ps1
```

Lệnh mặc định chỉ build Release và **đọc** live assembly. Nó kiểm tra original
signing key, public key token, URL/organization, cùng version live/local; không
upload. Nếu key bị thay, dừng ở bước kiểm tra identity. Nếu cần triển khai code
mới, tăng `AssemblyVersion` build/revision trong
`Dataverse.Plugin/FMCentralBms.Plugins/FMCentralBms.Plugins.csproj` (giữ major/minor),
build/test, xem lại kết quả preflight, rồi chạy:

```powershell
.\scripts\Publish-FmcPlugin.ps1 -Publish
```

`-Publish` gọi registrar SDK hiện có để cập nhật assembly và giữ các step/Custom
API trong solution. Registrar **không upload bytes nếu AssemblyVersion không đổi**.
Sau khi publish, script đọc lại live version/token; kết quả này không chứng minh
mọi business case của plug-in đã chạy đúng. Test hành vi trong
[plug-in runbook](dataverse-plugin-step-by-step.vi.md) và kiểm tra Plugin Trace Log.

PRT (Plug-in Registration Tool) có thể dùng để xem/test đăng ký, nhưng không bắt
buộc cho đường deploy này. Nếu dùng PRT, đăng nhập đúng organization và upload
**chính DLL Release đã ký bằng original key**; đừng tạo assembly mới bằng key mới.

Sau cloud write, export source solution để lưu diff triển khai. Dùng PAC profile
đã đối chiếu, xuất vào `.artifacts` rồi unpack và so với source trước khi cập nhật
`dataverse/FMCentralBms` (không ghi đè mù các thay đổi local):

```powershell
New-Item -ItemType Directory -Force .\.artifacts\plugin-export | Out-Null
pac solution export --name FMCentralBms `
  --environment https://org06cbc9ec.crm5.dynamics.com/ `
  --path .\.artifacts\plugin-export\FMCentralBms_unmanaged.zip --managed false
pac solution unpack `
  --zipfile .\.artifacts\plugin-export\FMCentralBms_unmanaged.zip `
  --folder .\.artifacts\plugin-export\unpacked --packagetype Unmanaged
```

Đọc diff với `dataverse/FMCentralBms` rồi cập nhật nguồn solution tương ứng.
Không coi file export cũ là bằng chứng trạng thái cloud hiện tại.

## 4. Chạy data sync từ máy nhà (việc riêng với deploy plug-in)

Chỉ chạy nếu `FM_Central` trên máy này có dữ liệu/ledger đúng nguồn. Không copy
riêng `raw.bms_reading` mà bỏ delivery ledger; không đổi `SourceId` để né duplicate.
Kiểm tra read-only trước:

```powershell
.\scripts\Start-DataverseSync.ps1 -Mode Verify
```

Để worker chờ request từ Power Automate ở chế độ `CommandDriven`, chạy
`.\scripts\Start-DataverseSync.ps1` và giữ terminal mở. `-Mode Once` là **lệnh ghi**
Dataverse để xử lý một batch; chỉ chạy khi đã xác nhận đúng SQL nguồn/Dataverse
target. SharePoint watcher (cũng có thể ghi records) chạy riêng qua
`.\scripts\Start-SpoDataverseWatch.ps1`. Xem
[SQL-to-Dataverse runbook](sql-to-dataverse-runbook.vi.md) và
[SPO local ingestion](spo-local-ingestion.vi.md) để kiểm tra receipts.

Nếu máy nhà không có `FM_Central`/ledger, chỉ publish plug-in hoặc chạy SPO watcher
phù hợp; không tự tạo database mới rồi coi là lịch sử SQL của máy công ty.
