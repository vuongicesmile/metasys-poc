# SharePoint Online → Dataverse: triển khai và kiểm thử

Trạng thái: đã copy file CSV thật từ SharePoint vào Dataverse bằng manual cloud flow;
hai lần chạy thành công, cùng record, SHA-256 trùng nguồn. Cập nhật: 2026-09-12.

Runbook này đi cùng
[plan kỹ thuật](../plans/sharepoint-to-dataverse-file-ingestion.vi.md). Plan giải thích các
quyết định và contract; tài liệu này ghi đúng những gì đã có trong Developer environment,
cách kiểm tra, và các bước còn lại để chạy demo end-to-end.

## 1. Deployment receipt hiện tại

| Mục | Giá trị đã xác thực |
| --- | --- |
| Environment | `5abcb0e5-99b2-e51f-aa0e-90d84405798b` |
| Organization | `ab191700-b99e-f111-aaa0-000d3a80bb96` |
| Dataverse URL | `https://org06cbc9ec.crm5.dynamics.com/` |
| Solution | `FMCentralBms` unmanaged, prefix `fmc` |
| Model-driven app | `fmc_FMCBMSDemo` |
| App ID | `d19f4897-d227-4df3-8361-988f97c53e89` |
| SPO File table | `fmc_spofile`, metadata ID `1e4a957f-04ae-f111-aaad-00224819a344` |
| SPO Import Row table | `fmc_spoimportrow`, metadata ID `30bf49ba-04ae-f111-aaad-00224819a344` |
| Runtime role | `FM Central SPO File Ingestion`, role ID `6348e20b-05ae-f111-aaad-00224819a344` |
| SharePoint connection | `shared-sharepointonl-c613b2d6`, Connected |
| Dataverse connection | `shared-commondataser-ffc44f95-e5ab-44b0-acdf-47752fa72135`, Connected |
| App validation | Success, 0 issue sau lần publish ngày 2026-09-12 |
| Manual copy flow | `FMC - Copy SPO Demo File (Manual)` — On, chỉ chạy khi bấm Run |
| Flow ID | `1c3eda7d-9bec-4a43-adc3-1426b89d2a84` |
| File receipt ID | `8bae05bc-08ae-f111-aaad-00224819a344` |

Mở app trực tiếp:

```text
https://org06cbc9ec.crm5.dynamics.com/main.aspx?appid=d19f4897-d227-4df3-8361-988f97c53e89
```

Trong nhóm Catalog đã có trang **SPO Files**. Main form có File column và subgrid
**SPO Import Rows**. Các view đã tạo gồm All SPO Files, Archived SPO Files,
Archive Failed SPO Files, Import Failed SPO Files và All SPO Import Rows.

### Demo đã chạy thật ngày 2026-09-12

Nguồn: `/Shared Documents/employee_bronze_profiling_demo_full.csv`, 293502 bytes.
Đích: File column `fmc_file` trên record SPO File. Status là **Archived**;
Import Status là **NotRequested** vì lần test này chỉ copy nguyên file CSV.

| Lần test | Flow run ID | Kết quả |
| --- | --- | --- |
| Copy đầu tiên | `08584124580898731055942956626CU23` | Succeeded, tạo receipt và upload file |
| Replay | `08584124180627970922586213572CU29` | Succeeded, Set_existing_id, không tạo receipt thứ hai |

Cả hai lần tải bản Dataverse về để so với binary mà Get_content đã đọc từ SPO:

```text
SHA256 nguồn = SHA256 Dataverse
f3445ecfe242baee75f6e15c338060fde140426052d8a731d07fcae7f1dba011
```

Source key: `bms_spo_dev01_7d9282a4-c45d-4ef8-8719-18f78e71d0d0_1`.
List item ID thực bằng 1; không dùng ItemId=0 từ folder picker.

[Mở record đã copy](https://org06cbc9ec.crm5.dynamics.com/main.aspx?appid=d19f4897-d227-4df3-8361-988f97c53e89&pagetype=entityrecord&etn=fmc_spofile&id=8bae05bc-08ae-f111-aaad-00224819a344)
→ tìm field **File** để download CSV.

[Mở manual flow](https://make.powerautomate.com/environments/5abcb0e5-99b2-e51f-aa0e-90d84405798b/flows/1c3eda7d-9bec-4a43-adc3-1426b89d2a84/details)
→ **Run** để copy lại đúng file demo. Flow đọc latest metadata/content khi chạy, kiểm
tra giới hạn 5 MiB và ETag trước/sau download, dùng lại receipt theo source key.
Manual demo cố ý upload lại trên replay để kiểm chứng tương tác. Chưa có event trigger
tự chạy khi upload, parser CSV/JSON, initial scan hoặc reconciliation đa trang.

Nếu file nguồn đã đổi sau lần test, hash mới sẽ khác receipt trên; so hai bản thuộc
cùng lần chạy, không so bản nguồn mới với bản archive cũ.

## 2. Code và artifact đã tạo

- `DataverseSyncWorker/Services/DataverseProvisioner.SpoIngestion.cs`: schema, choices,
  keys, relationship, forms, views, environment variables, role, sitemap và verification.
- `DataverseSyncWorker/Program.cs`: ba command provision/status/verify.
- `dataverse/FMCentralBms`: source solution được export lại từ Developer environment.
- `docs/examples/spo-import`: một JSON hợp lệ và sáu JSON lỗi có expected result.
- `scripts/Test-SpoImportFixtures.ps1`: kiểm tra schemaVersion, số item, kiểu/range,
  trim và duplicate code trước khi dùng fixture trên flow.
- `scripts/provision-spo-copy-demo.py`: render, deploy Off, enable/disable manual flow;
  dùng Web API giống lifecycle workflow của provisioner hiện có. Không tải CSV về máy.
- `scripts/verify-spo-copy-demo.py`: đọc run output của SharePoint và File column
  Dataverse vào bộ nhớ, so byte count/ETag/SHA-256; chỉ lưu receipt metadata/hash trong
  `.artifacts/spo-copy`, không in nội dung CSV hoặc token/signed URL.

`fmc_rowcount` là Whole Number. Dataverse SDK không có schema-level `DefaultValue` cho
`IntegerAttributeMetadata`, vì vậy flow phải ghi `0` khi tạo receipt mới.

## 3. Kiểm tra PAC và đúng target

Chạy từ root repo:

```powershell
pac auth list
pac org who
pac solution list
```

Chỉ tiếp tục khi output khớp Environment, Organization và Dataverse URL ở mục 1, đồng
thời solution `FMCentralBms` tồn tại. Không dựa riêng vào tên profile PAC vì profile có
thể được đổi target.

## 4. Build, provision và verify Dataverse

Token helper mặc định trong repo có thể mang đường dẫn của máy khác. Trên checkout này,
đặt override cho process PowerShell hiện tại:

```powershell
$env:Dataverse__DeveloperTokenPython = 'D:\Coder\AI-AGENT-PLATFORM\.tools\azure-cli\Scripts\python.exe'
$env:Dataverse__DeveloperTokenScript = 'D:\Coder\metasys-poc\scripts\get-dataverse-token.py'

dotnet build .\DataverseSyncWorker\DataverseSyncWorker.csproj --configuration Release
dotnet run --project .\DataverseSyncWorker\DataverseSyncWorker.csproj `
  --configuration Release --no-build -- --spo-ingestion-status
dotnet run --project .\DataverseSyncWorker\DataverseSyncWorker.csproj `
  --configuration Release --no-build -- --verify-spo-ingestion
```

Khi triển khai vào environment mới, dùng command mutation sau một lần. Command
idempotent nên có thể chạy lại sau lỗi giữa chừng:

```powershell
dotnet run --project .\DataverseSyncWorker\DataverseSyncWorker.csproj `
  --configuration Release --no-build -- --provision-spo-ingestion
```

Provisioner đợi hai alternate key chuyển sang `Active`, publish table/app, rồi tự verify.
Nó tạo role nhưng không tự gán role cho user hay application user.

## 5. Nguồn SharePoint đã xác thực

| Environment variable | Giá trị cần nhập |
| --- | --- |
| `fmc_SpoSiteUrl` | `https://titancorpvncom.sharepoint.com/sites/Powerplatform` |
| `fmc_SpoLibraryId` | `7d9282a4-c45d-4ef8-8719-18f78e71d0d0` — Tài liệu / Shared Documents |
| `fmc_SpoInboxPath` | File mẫu ở root `/Shared Documents`; chưa tạo inbox con |
| `fmc_SpoSourceNamespace` | Giữ `bms_spo_dev01` cho Developer nếu chưa có quy ước khác |

Kiểm tra read-only ngày 2026-09-12 qua SharePoint connector:

- `test_connection`: Connected, account `vuong.nguyenq@titancorpvn.com`.
- `GetTablesForLibraries`: tìm được library trên, type 101 (document library).
- `ListFolder`: tìm thấy `/Shared Documents/employee_bronze_profiling_demo_full.csv`,
  293502 bytes, `IsFolder=false`, ETag `"{9A4A48A9-15CB-4A2C-AA6D-BE4295EF1471},1"`.
- Sau lượt kiểm tra read-only, manual flow đã download/archive và verifier đã so SHA256
  thành công; xem receipt ở mục 1.
- File CSV dùng để test archive. Parser JSON buildings-v1 trong plan không parse CSV
  employee; cần contract/mapping riêng nếu mở rộng import nội dung CSV.

Lưu ý tooling: `list_tables` dùng GetTables chỉ trả các list thường; dùng
`GetTablesForLibraries` để tìm document library. Với FlowAgent 3.0.5, ListFolder cần
URL-encode trước dataset và folder id một lần, sau đó wrapper encode thêm một lần theo
route của connector. Truyền chuỗi chưa encode gây `Route did not match`; không phải lỗi
đăng nhập. `ItemId=0` từ folder picker không dùng làm list item ID cho source key.

Các giá trị trên đã bind thành current values trong Developer environment cho manual
flow. SharePoint connection reference là `fmc_sharedsharepointonline`; Dataverse dùng lại
`fmc_sharedcommondataserviceforapps`. Khi chuyển environment cần bind lại bằng deployment
settings; không đưa current-value records của Dev vào source solution release.

Khi triển khai tiếp event flow đầy đủ theo plan, tạo **FMC - Archive SPO File** trong
`FMCentralBms`, để flow ở trạng thái Off. Bind SharePoint connection
`shared-sharepointonl-c613b2d6` và Dataverse connection
`shared-commondataser-ffc44f95-e5ab-44b0-acdf-47752fa72135`. Cấu hình trigger concurrency
bằng 1 và đi đúng thứ tự action tại mục 5–7 của plan.

Tắt manual demo trước khi bật event writer để tránh hai flow đồng thời ghi cùng file.

Lệnh tái tạo manual demo từ root repo (Python có Azure CLI và requests):

```powershell
& 'D:\Coder\AI-AGENT-PLATFORM\.tools\azure-cli\Scripts\python.exe' scripts/provision-spo-copy-demo.py render
# Validate .artifacts/spo-copy/validate_flow.args.json và preflight_flow.args.json
# bằng FlowAgent validate_flow/preflight_flow trước khi deploy.
& 'D:\Coder\AI-AGENT-PLATFORM\.tools\azure-cli\Scripts\python.exe' scripts/provision-spo-copy-demo.py deploy
& 'D:\Coder\AI-AGENT-PLATFORM\.tools\azure-cli\Scripts\python.exe' scripts/provision-spo-copy-demo.py enable
```

`deploy` từ chối sửa flow đang On; dùng mode `disable` trước khi cập nhật. Sau khi bấm
Run trong Power Automate, lấy Run ID mới và kiểm tra:

```powershell
& 'D:\Coder\AI-AGENT-PLATFORM\.tools\azure-cli\Scripts\python.exe' scripts/verify-spo-copy-demo.py --run-id '<Run ID mới>'
```

Bật flow trong đợt test có kiểm soát, upload một file JSON mẫu và kiểm tra run output để map chính xác bốn
giá trị connector: list item `ID`, `Identifier`, `ETag` và binary envelope của File
Content. Không thay `ID` bằng `Identifier`.

## 6. Test fixture local

```powershell
.\scripts\Test-SpoImportFixtures.ps1
```

Kỳ vọng:

```text
ValidFixtures   1
InvalidFixtures 6
ValidRows       2
Result          PASS
```

Fixture hợp lệ tạo hai import rows: `BLD001 / Building A / 5` và
`BLD002 / Building B / 3`. Sáu fixture lỗi phải dừng ở validation và không được set
parent thành Imported.

## 7. Demo end-to-end sau khi flow được bind

1. Mở inbox SharePoint đã chốt và upload `docs/examples/spo-import/buildings-v1.json`.
2. Mở run history của **FMC - Archive SPO File**. Ghi lại Flow run ID.
3. Mở FMC BMS Demo → **SPO Files**. Chỉ có một receipt theo source key; trạng thái archive
   phải là Archived.
4. Mở receipt, tải File column xuống. Tải file nguồn từ SharePoint xuống cùng thư mục.
5. So SHA-256:

   ```powershell
   Get-FileHash -Algorithm SHA256 .\buildings-v1-from-spo.json
   Get-FileHash -Algorithm SHA256 .\buildings-v1-from-dataverse.json
   ```

6. Khi phase parser đã bật, parent phải là Imported, row count bằng 2 và subgrid có đúng
   hai rows của cùng `fmc_importedetag`.
7. Upload lại hoặc Resubmit. GUID receipt không đổi và composite key không cho tạo duplicate
   import row theo file/version/ordinal.
8. Chạy lần lượt các fixture lỗi, file >5 MiB, `~$` temp file và file sai extension; đối
   chiếu status/error với test matrix trong plan.

Flow Off không nhận sự kiện upload; cần bật flow để chạy happy-path thực tế. Archive và JSON import là hai
mốc độc lập: archive có thể Archived trong khi import Failed.

## 8. Export source sau thay đổi live

Sau mỗi lần sửa metadata/flow đã verify:

```powershell
pac solution export --name FMCentralBms `
  --path .\dataverse\FMCentralBms.zip --managed false `
  --environment https://org06cbc9ec.crm5.dynamics.com/
pac solution unpack --zipfile .\dataverse\FMCentralBms.zip `
  --folder .\dataverse\FMCentralBms --packagetype Unmanaged --allowDelete true
```

Review `git diff` trước khi commit vì solution live có thể chứa thay đổi song song của maker
khác. Không commit file ZIP hoặc connection credential/current-value secret.

## 9. Rollback vận hành

Tắt event flow, đợi các run đang ghi kết thúc, rồi sửa và Resubmit/reconcile. Không uninstall
solution để rollback vì uninstall có thể ảnh hưởng receipt và binary đã lưu. Schema mới chưa
có bước tự xóa; provisioner cũng không tự gán hoặc thu hồi quyền người dùng.
