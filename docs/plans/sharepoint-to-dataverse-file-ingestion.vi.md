# Plan triển khai SharePoint Online → Dataverse

Trạng thái: đề xuất đã review, chưa triển khai. Cập nhật: 2026-09-11.

## 1. Phạm vi và quyết định MVP

MVP dùng Power Automate SharePoint connector để đọc file, Dataverse connector để lưu
binary vào File column. Không cần viết Graph API client. Không khẳng định connector
dùng Graph hay SharePoint REST nội bộ; đây là chi tiết do Microsoft quản lý.

Chọn các mặc định sau cho demo; đây là quyết định thiết kế, chưa phải cấu hình live:

| Mục | Quyết định |
| --- | --- |
| Solution | FMCentralBms, publisher prefix fmc |
| Nguồn | Một SPO site, một document library, một inbox folder |
| File archive | CSV, JSON, XLSX; tối đa 5 MiB = 5242880 bytes |
| Lưu trữ | Copy binary vào Dataverse; giữ file nguồn ở SPO |
| Version | Latest-only, không cam kết lưu mọi version trung gian |
| Writer | Một flow writer, trigger concurrency = 1; loop tuần tự |
| Xóa/di chuyển | Không tự xóa bản Dataverse; move sang library khác là nguồn mới |
| Parse demo | JSON schema v1, tối đa 100 items, ghi table demo riêng |
| SQL/BMS | Không ghi đè Building/Equipment/Point do SQL worker quản lý |
| Retention demo | Chưa tự xóa; theo dõi capacity và quyết định retention trước production |

Archive và import là hai kết quả độc lập. `Archived` nghĩa là có bản file đã upload;
`Imported` nghĩa là parser đã hoàn thành đủ các dòng của một version.

Microsoft hỗ trợ [Get file content trong SharePoint connector](https://learn.microsoft.com/en-us/connectors/sharepointonline/)
và [upload vào Dataverse File column](https://learn.microsoft.com/en-us/power-automate/dataverse/upload-download-file).
Flow chạy cloud, không cần download xuống local hoặc bật worker SQL để copy file.

## 2. Thông tin triển khai và điều kiện đầu vào

Điền các giá trị thực trước khi bật flow:

| Cấu hình | Nguồn lấy / giá trị |
| --- | --- |
| SPO Site URL | URL site được phép đọc, chưa được cung cấp |
| SPO Library ID | GUID library lấy từ Library settings / connection picker; lưu không có braces |
| Inbox folder | Chọn bằng folder picker sau khi chọn library |
| Source namespace | Alias ổn định như `bms_spo_dev01`; chỉ a-z, 0-9, underscore; duy nhất trong environment |
| Dataverse URL | https://org06cbc9ec.crm5.dynamics.com/ — target Developer dự kiến |
| Organization ID | ab191700-b99e-f111-aaa0-000d3a80bb96 — phải verify trước deployment |
| Connection owner | Account có quyền SPO read và Dataverse runtime table access |

Trong Power Automate → Connections, tạo SharePoint và Microsoft Dataverse connection,
test đăng nhập. Xác nhận license cho flow dùng Dataverse premium connector; không suy
ra quyền production chỉ từ Developer environment. Kiểm tra DLP cho phép hai connector
và File capacity đủ cho demo. [Licensing FAQ](https://learn.microsoft.com/en-us/power-platform/admin/power-automate-licensing/faqs).

SPO connection cần đọc library. Runtime Dataverse role cần Create/Read/Write table
mới, Append/Append To cho lookup. User xem app cần Read record/file. File copy dùng
Dataverse security; việc thu hồi quyền SPO không tự thu hồi bản copy Dataverse.

## 3. Schema đủ để build

Tạo standard table `fmc_spofile`, organization-owned cho demo. Tên ở đây là logical
names; chọn table bằng display name trong designer, không tự đoán entity-set plural.

| Column | Type / cấu hình | Ý nghĩa |
| --- | --- | --- |
| fmc_spofileid | Primary GUID tự sinh | Row ID dùng upload |
| fmc_name | Text 255, required, primary name | Tên file |
| fmc_sourcekey | Text 150, required | Stable source key, xem mục 5 |
| fmc_filename | Text 255, required | Tên kèm extension |
| fmc_sharepointidentifier | Text 4000 | Identifier để lấy content, không làm key |
| fmc_sharepointpath | Text 4000 | Path hiện tại |
| fmc_sharepointurl | Text 4000 | Link nguồn |
| fmc_filesize | Whole Number 0..5242880 | Giới hạn demo |
| fmc_file | File, MaxSizeInKB = 5120 | Bản binary latest |
| fmc_status | Choice, default Received | Trạng thái archive |
| fmc_etag | Text 200 | ETag đã archive thành công |
| fmc_candidateetag | Text 200 | ETag đang xử lý |
| fmc_receivedat | DateTime User Local | Lần đầu nhận, không reset khi retry |
| fmc_processedat | DateTime User Local | Lần archive thành công gần nhất |
| fmc_runid | Text 100 | Flow run đang/đã xử lý |
| fmc_errormessage | Multiline Text 4000 | Lỗi rút gọn, không kèm binary |
| fmc_importstatus | Choice, default NotRequested | Trạng thái parse |
| fmc_importedetag | Text 200 | Version parse thành công |
| fmc_rowcount | Whole Number, default 0 | Dòng import thành công trong version |

Choice archive: Received=789110000, Processing=789110001, Archived=789110002,
Failed=789110003, Ignored=789110004. Choice import: NotRequested=789111000,
Processing=789111001, Imported=789111002, Failed=789111003. Kiểm tra không trùng
contract hiện có khi provision; đây là local choices mới.

Key `fmc_spofile_sourcekey` trên `fmc_sourcekey`; đợi status **Active** rồi mới chạy.
Key dài 150 ký tự không chứa URL hoặc path. Dataverse hạn chế một số ký tự khi dùng
alternate key để GET/PATCH, gồm `/`, `:`, `%`.
[Alternate key constraints](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/define-alternate-keys-portal).

Trong repo, thêm định nghĩa table/columns/key và form/view vào `DataverseProvisioner`
hoặc partial class cùng service, theo pattern schema hiện có. File metadata dùng
`FileAttributeMetadata`, `MaxSizeInKB=5120`; tham chiếu
[File column documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/file-column-data).
Build solution trước khi chạy provisioning đã xác nhận target. Không tạo schema
chỉ trong maker rồi bỏ sót định nghĩa source.

## 4. Tạo flow và cấu hình trong solution

1. Power Automate → chọn Developer → Solutions → FMCentralBms.
2. Tạo connection reference cho SharePoint; reuse Dataverse reference nếu cùng identity/quyền.
3. Tạo Text environment variables `fmc_SpoSiteUrl`, `fmc_SpoLibraryId`,
   `fmc_SpoInboxPath`, `fmc_SpoSourceNamespace`. Max file size demo là constant 5242880.
4. New → Automation → Cloud flow → Automated. Tên `FMC - Archive SPO File`.
5. Trigger **When a file is created or modified (properties only)**.
6. Site Address và Library Name: chọn Enter custom value rồi dùng environment variables;
   Library Name dùng Library GUID. Folder chọn đúng inbox.
7. Trigger Settings → Concurrency Control On → Degree of Parallelism **1**.
8. Filter folder objects và file tạm `~$`; chỉ nhận csv/json/xlsx. Đặt Condition ngay
   sau trigger để có thể nhìn lý do skip trong run history.
9. Đổi tên action theo mục 5 trước khi paste expression. Tên hiển thị có space thường
   được đổi thành underscore trong expression; dùng Peek code để kiểm tra.

Các field động của SPO phải được đối chiếu với một run mẫu. Không dùng `Identifier`
thay cho list item `ID`: hai field có mục đích khác nhau.

## 5. Flow archive — thứ tự và expression cụ thể

### 5.1. Chuẩn hóa identity

Initialize String variable `SourceNamespace` từ environment variable; `LibraryId`
từ GUID library; `RowId` là chuỗi rỗng. Compose `SourceKey`:

```text
concat(variables('SourceNamespace'), '_', toLower(variables('LibraryId')), '_', string(triggerBody()?['ID']))
```

Ví dụ `bms_spo_dev01_11111111-2222-3333-4444-555555555555_27`.
Item ID ổn định khi rename trong cùng library. Identifier/path chỉ là địa chỉ truy
cập. Copy/move sang library khác tạo key khác; chưa tự migrate identity.

### 5.2. Resolve Row ID và ghi receipt trước download

Action Dataverse **List rows**, đặt tên `Find_file`:

| Field | Điền |
| --- | --- |
| Table name | SPO File |
| Select columns | fmc_spofileid,fmc_status,fmc_etag,fmc_receivedat |
| Filter rows | expression bên dưới |
| Row count | 2 để phát hiện duplicate bất thường |

```text
concat('fmc_sourcekey eq ', decodeUriComponent('%27'), outputs('SourceKey'), decodeUriComponent('%27'))
```

Condition `length(body('Find_file')?['value'])`:

- 1: Set `RowId = first(body('Find_file')?['value'])?['fmc_spofileid']`.
- 0: **Add a new row**, tên `Create_file`, điền name, filename, sourcekey,
  Received và `utcNow()` vào receivedat. Set `RowId = body('Create_file')?['fmc_spofileid']`.
- Lớn hơn 1: fail với `SPO-001 Duplicate source key`, kiểm tra key status.

Nếu Create timeout hoặc duplicate-key fault, query lại `Find_file` theo source key;
có đúng một row thì reuse GUID. Nếu không có, giữ lỗi. Không coi mọi create fault là
duplicate. Cách này dùng GUID cho mọi Update/Upload, không phụ thuộc hỗ trợ alternate
key trong Row ID của từng connector action. Unique key vẫn bảo vệ create trùng.

### 5.3. Đọc metadata mới nhất, rồi lấy binary

1. SharePoint **Get file properties** (`Read_properties`): site/library, item ID từ trigger.
   Lấy Identifier và Filename with extension mới nhất, không dùng path cũ của event.
2. **Get file metadata** (`Metadata_before`), File Identifier vừa nhận: lấy ETag và Size.
3. Nếu ETag trống: fail `SPO-002 Missing version token`; phải verify output trong run mẫu.
4. Nếu Size > 5242880 hoặc extension sai: Update Ignored, lý do, kết thúc trước download.
5. Nếu row Archived và ETag hiện tại bằng `fmc_etag`: skip upload, giữ receipt thành công.
6. **Update a row** theo `variables('RowId')`: Processing, candidateetag,
   runid=`workflow()?['run']?['name']`, metadata hiện tại. Không ghi đè successful etag.
7. SharePoint **Get file content** (`Get_content`): File Identifier từ bước 1.
8. **Get file metadata** lần nữa (`Metadata_after`): cùng Identifier.
9. So sánh `body('Metadata_before')?['ETag']` với `body('Metadata_after')?['ETag']`.
   Nếu khác, bỏ binary vừa đọc và kết thúc Failed `SPO-003 Source changed during read`;
   lần replay/event tiếp theo sẽ đọc latest. Không upload bytes đó với ETag cũ.

MVP coi source ổn định khi ETag trước/sau download bằng nhau. Nếu file thay đổi ngay
sau đó, event/reconciliation tiếp theo cập nhật lại. Không hứa file luôn đồng bộ tức thời.

### 5.4. Upload và acknowledge

Dataverse **Upload a file or an image** (`Upload_file`):

| Field | Giá trị |
| --- | --- |
| Table name | SPO File |
| Row ID | variables('RowId') |
| Column name | fmc_file |
| Content | Dynamic content File Content của Get_content |
| Content name | Filename with extension mới nhất |

Content lấy nguyên binary token; không gọi string() hoặc base64() cho bước upload.
Sau khi upload thành công, Update row: Archived, `fmc_etag=ETag trước download`,
processedat=`utcNow()`, errormessage=`null` bằng Expression để xóa lỗi cũ.
Chỉ acknowledge sau upload. Nếu upload thành công nhưng update status lỗi, retry
ghi lại cùng file trên cùng row là chấp nhận được; không skip theo candidateetag.

## 6. Concurrency, retry và Catch

Flow duy nhất có concurrency=1 nên hai run không cùng ghi File column. Event cũ vẫn
đọc metadata hiện tại, do đó không chủ động tải lại version cũ. Alternate key chỉ
ngăn duplicate row; nó không khóa binary upload. Không bật thêm flow writer/backfill
song song nếu chưa bổ sung lease/serialization dùng chung.

Scope `Try` bao toàn bộ Resolve row → metadata → content → upload → acknowledge.
Scope `Catch` Configure run after: failed hoặc timed out của Try:

1. Nếu RowId khác rỗng, cố Update Failed với run ID, mã lỗi và tóm tắt lỗi action.
2. Nếu RowId rỗng hoặc Dataverse đang unavailable, không gọi Update bằng GUID rỗng;
   giữ lỗi trong run history và Terminate Failed. Replay sau khi connection phục hồi.
3. Không copy toàn bộ `result('Try')` vào error field vì có thể gồm binary.
4. Notifications là tùy chọn, chỉ thêm khi đã có người nhận và yêu cầu gửi.

Đề xuất retry exponential tối đa 4 lần cho transient 429/5xx; dùng Retry policy của
action và tôn trọng Retry-After. 400/403/schema sai cần sửa trước replay.
Failed flow không tự trở thành Succeeded chỉ vì Catch ghi receipt thành công.

Concurrency waiting queue có giới hạn; không dùng event trigger làm bằng chứng đủ
mọi file. Cần reconciliation tại mục 9.
[Power Automate limits](https://learn.microsoft.com/en-us/power-automate/limits-and-config).

## 7. Ví dụ import JSON thành rows — phase riêng

Fixture UTF-8 `buildings-v1.json`:

```json
{
  "schemaVersion": 1,
  "items": [
    { "code": "BLD001", "name": "Building A", "floorCount": 5 },
    { "code": "BLD002", "name": "Building B", "floorCount": 3 }
  ]
}
```

Tạo standard table `fmc_spoimportrow` chứa các dòng của version đã parse. Đây là bảng
demo để kiểm chứng ETL, chưa publish vào `fmc_bmsbuilding` do SQL worker quản lý.

| Field | Type |
| --- | --- |
| fmc_name | Text 255 |
| fmc_fileid | Lookup fmc_spofile, required |
| fmc_sourceetag | Text 200, required |
| fmc_ordinal | Whole number 1..100, required |
| fmc_code | Text 50 |
| fmc_buildingname | Text 255 |
| fmc_floorcount | Whole number 0..200 |

Composite unique key: file lookup + sourceetag + ordinal. ETag chứa ký tự đặc biệt
nên key chỉ dùng enforce uniqueness; tìm bằng List rows filter rồi update bằng GUID,
không dùng ETag trực tiếp trong alternate-key URL. Escape dấu nháy đơn trong OData
string bằng cách nhân đôi. Đợi key Active trước test.

Parse action chỉ chạy ngay sau archive thành công của JSON, với cùng buffer vừa upload.
Không fetch lại SPO cho parse vì có thể lấy version khác. JSON schema cho **Parse JSON**:

```json
{
  "type": "object",
  "required": ["schemaVersion", "items"],
  "properties": {
    "schemaVersion": { "type": "integer", "enum": [1] },
    "items": {
      "type": "array", "maxItems": 100,
      "items": {
        "type": "object",
        "required": ["code", "name", "floorCount"],
        "properties": {
          "code": { "type": "string", "minLength": 1, "maxLength": 50 },
          "name": { "type": "string", "minLength": 1, "maxLength": 255 },
          "floorCount": { "type": "integer", "minimum": 0, "maximum": 200 }
        }
      }
    }
  }
}
```

Nếu Get_content output là binary envelope có `$content`, Content expression:

```text
json(base64ToString(body('Get_content')?['$content']))
```

Kiểm tra output thực trước khi dùng: nếu connector đã trả JSON object, truyền object
thẳng; không base64 decode hai lần. Demo chấp nhận UTF-8 không BOM, chưa hỗ trợ UTF-16.

Thứ tự implementation:

1. Update importstatus=Processing; giữ archive status=Archived.
2. Parse JSON; thêm Condition `schemaVersion=1`, item count <=100 để lỗi rõ ràng.
3. Validate toàn bộ items trước write: trim code/name không rỗng, code duy nhất trong
   file sau uppercase; không tự chuyển số thập phân floorCount sang integer.
4. Apply to each tuần tự, counter ordinal bắt đầu 1. Mapping code=`toUpper(trim(item()?['code']))`,
   buildingname=`trim(item()?['name'])`, floorcount=`item()?['floorCount']`.
5. List rows theo file GUID, sourceetag, ordinal; không có thì Add, có thì Update bằng GUID.
   Lookup dùng entity-set name lấy từ metadata/picker, không đoán pluralization.
6. Ghi rowcount khi đủ tất cả rows. Set Imported và importedetag cuối cùng.
7. Khi lỗi, giữ rows partial để replay cùng version, importstatus=Failed; archive vẫn Archived.
   Consumer chỉ đọc rows có sourceetag bằng importedetag và parent importstatus=Imported.
8. Nếu version mới đã thay raw file trước replay, parse latest; lịch sử partial cũ giữ lại
   nhưng không coi là dataset đã publish. Immutable audit cần bảng version + binary riêng.

Fixture lỗi: schemaVersion=2; thiếu name; name toàn spaces; floorCount=-1; duplicate
code; trên 100 items. Không fixture nào được đánh dấu Imported. CSV/Excel chưa là parser
MVP: CSV cần parser xử lý quoted delimiter/newline; Excel dùng List rows present in a
table, cần pagination (mặc định 256 rows) và giới hạn workbook 25 MB.
[Excel connector](https://learn.microsoft.com/en-us/connectors/excelonlinebusiness/).

## 8. UI để test upload/download

1. Maker → Solutions → FMCentralBms → SPO File → Forms → Main form.
2. Thêm name, filename, status, etag, processedat, errormessage, importstatus và File column.
3. Đặt field quản lý bởi flow read-only trên form; quyền table vẫn là kiểm soát server.
4. Tạo views All SPO Files, Archive Failed, Import Failed, Archived.
5. Edit model-driven app FMC BMS Demo → Add page → Dataverse table → SPO File.
6. Save, Publish. Mở app, chọn row → File control → download.
7. Upload fixture từ SharePoint UI, đợi flow run, refresh grid và so tên, size, binary.
   Tải cả bản SPO và Dataverse xuống local để so `Get-FileHash -Algorithm SHA256`.

## 9. File có sẵn, replay và reconciliation

Trigger mới không phải một initial scan. Chuẩn bị một flow batch riêng, mặc định Off:
`FMC - Reconcile SPO Files`, manual trigger, Get files (properties only), filter inbox,
pagination On và threshold phù hợp số file kiểm kê. Nếu vượt threshold phải chia theo
folder/ID range; không coi một trang kết quả là toàn bộ thư viện.

MVP vận hành batch trong maintenance window: tắt event flow, đợi active runs hoàn tất,
chạy batch tuần tự dùng cùng steps mục 5, rồi bật event flow lại. Scan lại để bắt file
thay đổi trong khoảng chuyển chế độ. Có thể đóng gói steps chung vào child flow khi
triển khai; parent/child phải cùng solution và bind connections đúng.

Run history → Resubmit cho replay lỗi; resubmit vẫn đọc latest metadata. Cần lưu
source key, etag, run ID, archive/import status để đối chiếu. Rename cùng library phải
giữ row ID; move sang library khác tạo row mới. Xóa nguồn không tự xóa Dataverse.

## 10. Artifacts, triển khai và rollback

| Artifact cần bàn giao khi implement | Vai trò |
| --- | --- |
| DataverseProvisioner / partial schema mới | Table, columns, choices, key và dependency |
| dataverse/FMCentralBms/Entities | Export schema, forms, views |
| dataverse/FMCentralBms/Workflows | Archive và reconciliation flows |
| Connection references + environment variables | Bind theo environment |
| App module / sitemap export | Trang SPO File |
| docs/examples/spo-import | Fixtures JSON tốt/xấu và kỳ vọng |
| docs/runbooks/spo-file-ingestion.vi.md | Runbook sau deployment thực tế |
| Deployment receipt | Target, component IDs, run IDs, kết quả so file/rows |

Các artifact trên là deliverables tương lai, chưa được tạo chỉ vì plan liệt kê.
Implementation: schema → key Active → connections/variables → flows Off → UI → fixtures
→ enable archive → test → export/unpack. Với C# thay đổi chạy build MetasysPoc.sln.

```powershell
pac solution export --name FMCentralBms --path .\dataverse\FMCentralBms.zip
pac solution unpack --zipfile .\dataverse\FMCentralBms.zip --folder .\dataverse\FMCentralBms
```

Khi có Test environment được cấp: import solution, bind connection references và
environment values, kiểm tra key/form/File column, Save/enable flows rồi test lại.
Không đưa current values của Dev hoặc connection credentials vào release artifact.
[Solution-aware flows](https://learn.microsoft.com/en-us/power-automate/create-flow-solution),
[connection references](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/create-connection-reference).

Rollback: tắt flows và đợi runs đang ghi kết thúc; giữ receipts/binary, sửa flow rồi
replay. Không uninstall solution để rollback data import. Với target BMS thật, cần
chốt source authority và migration trước khi thêm publish step.

## 11. Test matrix và definition of done

| Test | Bằng chứng cần có |
| --- | --- |
| File CSV/JSON/XLSX <=5 MiB | Archived; download SHA256 giống source ổn định |
| File >5 MiB, folder, file tạm | Skip/Ignored trước download/upload |
| Event trùng và Resubmit | Một source row, GUID không đổi |
| Rename cùng library | Cùng GUID, metadata mới |
| File đổi khi download | Không acknowledge ETag cũ cho bytes mới |
| Event cũ chạy sau version mới | Đọc latest, không overwrite bằng stale event payload |
| Download SPO bị 403 | Receipt Failed có run ID |
| Create Dataverse lỗi trước RowId | Failed run rõ ràng, Catch không Update GUID rỗng |
| Upload thành công, acknowledge lỗi | Replay cùng row, không duplicate |
| JSON 2 items hợp lệ | Archived + Imported; 2 demo rows đúng mapping |
| JSON sai schema/rule | Archive giữ nguyên; import Failed, không publish dataset lỗi |
| Import lỗi giữa vòng lặp | Retry không duplicate theo file/version/ordinal |
| Backfill hơn một trang | Tổng scan đối chiếu kiểm kê SPO, không bỏ trang |
| Bật lại sau outage | Reconciliation tìm file chưa archive hoặc ETag khác |

Done archive: table/key/UI/flow và fixtures có export; run thực chứng minh copy và
retry; tải file từ app đúng binary. Done JSON import: thêm parser, rows và failed/replay
tests đạt. Hai mốc được nghiệm thu riêng.

## 12. Giới hạn còn cần dữ liệu thật để xác nhận

Đã chốt cách implement MVP trong plan. Còn thiếu Site URL, Library GUID, inbox folder
và connection identity thực để bind và chạy test. Cần kiểm tra dynamic output ETag,
Identifier, file-content shape trên tenant ở lần chạy đầu. Chưa có live verification,
capacity/load test hoặc production SLA.

Không cần Custom API/plugin mới để archive. Nếu mở rộng processing dài, API chỉ nhận
File Row ID và enqueue job; không truyền binary lớn qua synchronous plug-in.
