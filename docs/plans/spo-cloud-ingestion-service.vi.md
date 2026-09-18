# Plan: SPO ingestion vào các table Bronze hiện có

Ngày cập nhật: 2026-09-14. Trạng thái: **Core/CLI/Azure Functions và hạ tầng Bicep đã implement local; chưa deploy cloud**.

Implementation hiện nằm tại các layer `SPO.Ingestion.Domain`,
`SPO.Ingestion.Common`, `SPO.Ingestion.Business`, `SPO.Ingestion.DataAccess`,
`SPO.Ingestion.App`, cùng `SPO.Ingestion.Cli`,
`SPO.Ingestion.Functions`, `config/spo-ingestion.json` và `infra/spo-ingestion`.
Runbook triển khai/test là [spo-cloud-ingestion-service.vi.md](../runbooks/spo-cloud-ingestion-service.vi.md).
Account Azure được kiểm tra ngày 2026-09-14 chỉ có tenant-level context, không có
subscription; vì vậy chưa tạo Storage/Function App/flow và chưa ghi SPO data vào Dataverse.

## 1. Yêu cầu đã chốt

Dùng các table và column chuẩn đang có trong Dataverse. CSV/JSON/XLSX hoặc SharePoint
List được parse, map thành typed values và ghi trực tiếp vào các column Bronze.
Không tạo thêm table/column Dataverse, không tạo generic Bronze JSON table, không
yêu cầu thiết kế lại cấu trúc dữ liệu đang dùng cho SQL ingestion.

Tài liệu này thay thế thiết kế trước về việc tạo các table version/request/row.
Queue, raw snapshot và checkpoint được đề xuất lưu trên Azure Storage. Đây là hạ
tầng mới cần triển khai, không phải hạ tầng đã có.

## 2. Baseline và table đích

[ReadingMapper](../../DataverseSyncWorker/Services/ReadingMapper.cs) và
[deployment contract](../reference/dataverse-deployment.md) xác nhận các table sau
trong repository. Metadata live phải được đối chiếu trước triển khai.

| Table hiện có | Vai trò | Identity |
| --- | --- | --- |
| fmc_bmsbuilding | Building catalog | Alternate key fmc_bmsbuilding_buildingcode |
| fmc_bmsequipment | Equipment catalog | Alternate key fmc_bmsequipment_equipmentcode |
| fmc_bmspoint | Bronze current state, standard | Alternate key fmc_bmspoint_objectid |
| fmc_bmsreading | Bronze history, elastic | Deterministic primary GUID + partitionid |

Các đích này được dùng chung cho dữ liệu SQL và SPO theo ownership/mapping quy định.
Không tự thêm column khi gặp header mới trong file.

fmc_spofile có thể tiếp tục hiển thị latest archive/status bằng các column hiện có.
Nó không phải đích của từng dòng nghiệp vụ hoặc immutable version queue.
fmc_spoimportrow giữ nguyên cho pilot Building; không mở rộng thành generic store.
fmc_syncrequest giữ contract SQL hiện tại, không đưa SPO jobs vào đó.

[Manual demo](../../scripts/provision-spo-copy-demo.py) chỉ archive một CSV cố định.
[Runbook SPO](../runbooks/spo-file-ingestion.vi.md) ghi receipt ngày 2026-09-12;
đó chưa phải multi-file ingestion hoặc xác nhận trạng thái live hôm nay.

## 3. Kiến trúc đề xuất

| Thành phần | Trách nhiệm |
| --- | --- |
| SharePoint connector trong Power Automate | Nhận event, đọc metadata/content của file và List |
| Azure Blob Storage | Raw snapshots bất biến, job manifests, row receipts, errors |
| Azure Storage Queue | Thông báo có job; message chỉ chứa Job ID/manifest reference |
| Azure-hosted .NET service | Claim, parse, validate, map và ghi typed Bronze columns |
| Dataverse tables hiện có | Records nghiệp vụ hiện trực tiếp trong Model-driven app |
| Dataflow hiện có | Đọc cùng Bronze tables, transform sang Silver phù hợp |

Luồng: upload/update SPO → capture raw vào Blob → Ready manifest + enqueue →
service parse/map → Building/Equipment/Point/Reading hiện có → Silver.

Power Automate đọc SPO qua SharePoint connector, không cần tự viết Graph API.
Có thể dùng Azure Blob connector để upload snapshot và authenticated service endpoint
để finalize manifest/enqueue. Action, connection, license và payload limits cần được
kiểm chứng trong implementation preflight. Worker đọc raw đã capture, không đọc lại
workbook latest trong lúc parse.

Azure Storage được chọn để giữ queue/lease/replay mà không thêm Dataverse tables.
Run history đơn thuần không đủ làm delivery ledger khi service crash giữa các write.

## 4. Source registration và mapping configuration

Đề xuất thêm file config/spo-ingestion.json ở phase implement, deploy cùng service.
Cấu hình chứa namespace, site, library/list ID, path scope, extension allowlist,
parser/mapping version, source aliases, target columns, business/event keys,
ownership, null policy và resource bounds. Không tạo configuration table Dataverse.

Ví dụ mapping cho Reading, là minh họa chưa implement:

~~~json
{
  "sourceKey": "spo-bms-readings",
  "sourceKind": "DocumentLibrary",
  "pathPrefix": "/Shared Documents/FMC-Inbox/Readings/",
  "extensions": ["csv", "json", "xlsx"],
  "mapping": "bms-reading-v1",
  "targetTable": "fmc_bmsreading",
  "eventIdField": "sourceReadingId",
  "columns": {
    "objectId": "fmc_objectid",
    "objectName": "fmc_objectname",
    "readingTime": "fmc_readingtime",
    "value": "fmc_readingvalue",
    "unit": "fmc_unit"
  }
}
~~~

Filename tự do. Path/source configuration chọn mapping; nhiều rule cùng match là
lỗi cấu hình. Source column names có thể khác nhau, aliases map sang chuẩn đã có.
Không ép người dùng generate một bộ BMS schema mới.

CSV cấu hình delimiter/encoding/header; JSON có item path; XLSX có table/sheet selector.
Nhiều sheet chỉ ingest các phần đã đăng ký. Library/list mới cần bind thêm trigger;
mỗi file không cần flow riêng. Verify subfolder coverage trong cả trigger và scan.

## 5. Mapping vào column thực tế

### Building và Equipment

| Input canonical | Table.column | Quy tắc |
| --- | --- | --- |
| buildingCode | fmc_bmsbuilding.fmc_buildingcode | Business key |
| name | fmc_bmsbuilding.fmc_name | Required text |
| sourceBuilding | fmc_bmsbuilding.fmc_sourcebuilding | Source mapping |
| description | fmc_bmsbuilding.fmc_description | Optional |
| equipmentCode | fmc_bmsequipment.fmc_equipmentcode | Business key |
| name | fmc_bmsequipment.fmc_name | Required text |
| equipmentType | fmc_bmsequipment.fmc_equipmenttype | Choice đang có |
| buildingCode | fmc_bmsequipment.fmc_buildingid | Resolve GUID, gán EntityReference |
| description | fmc_bmsequipment.fmc_description | Optional |

[BmsRelations](../../DataverseSyncWorker/Models/BmsRelations.cs) hiện map:
WaterMeter=789100000, TemperatureSensor=789100001, TestRig=789100002.
Verify choices live; không tự thêm AHU/CHILLER option nếu chuẩn chưa có.

### Point và Reading

| Input canonical | Point column | Reading column |
| --- | --- | --- |
| objectId | fmc_objectid | fmc_objectid |
| objectName | fmc_name | fmc_objectname |
| objectType | fmc_objecttype | fmc_objecttype |
| building | fmc_building | fmc_building |
| readingValue | fmc_currentvalue | fmc_readingvalue |
| unit | fmc_unit | fmc_unit |
| readingTime | fmc_lastreadingtime | fmc_readingtime |
| sourceSystem | fmc_sourcesystem | fmc_sourcesystem |
| equipmentCode | fmc_equipmentid lookup | Không có trong history mapper hiện tại |

Reading.fmc_name được mapper tạo; fmc_externalkey là diagnostic reference trong
giới hạn column, không phải alternate key của elastic table.

Giữ đúng ý nghĩa các field SQL:

- fmc_lastsqlid không chứa SharePoint Item ID/ordinal.
- fmc_sqlreadingid chỉ có giá trị khi source thực sự là SQL reading có identity đã biết.
- fmc_sqlingestedat không thay bằng thời điểm file upload SPO.
- Event thuần SPO bỏ qua các field SQL khi update, để null trên record mới nếu
  metadata cho phép. Nếu rule live bắt buộc SQL field, báo contract conflict.

Date parse rõ timezone, chuyển UTC đúng một lần. Decimal theo precision bốn số và
range chuẩn hiện có. Validate length/type/Choice/lookup; không truncate âm thầm.
Field vắng không tự clear; explicit null phải có policy.

## 6. Identity, chống trùng và SQL coexistence

Phân biệt:

~~~text
JobKey = hash(source identity, observed ETag/content hash, mapping version)
Target identity = business key hoặc stable source event key
~~~

ETag/ordinal phục vụ job/checkpoint/lineage, không thay identity nghiệp vụ. Copy
cùng event sang file khác không được tạo thêm Point/Reading.

### Standard tables

Resolve Building/Equipment/Point theo alternate key, dùng GUID của record đã có.
Không tạo cùng key dưới một SourceId/GUID khác. Khi tạo mới, dùng deterministic GUID
tương thích canonical SourceId của ReadingMapper. Không đổi SourceId=FMC của SQL
worker để phân biệt transport SPO. Source provenance đầy đủ nằm ở Blob receipts.

### Elastic Reading

Partition giữ ReadingMapper.Partition(objectId). Nếu file là export SQL, yêu cầu
SourceId + SQL ID thật và reuse ReadingId hiện tại. Với event sinh riêng từ SPO,
dùng namespace event SPO + stable sourceReadingId qua helper StableGuid; không giả SQL ID.
Thiếu event ID thì cấu hình composite key đủ phân biệt theo nghiệp vụ; objectId +
timestamp không mặc nhiên unique. Không dùng row ordinal làm identity xuyên các file.

TTL vẫn tính từ event time. Expired history được acknowledge SkippedExpired trong
receipt, không replay để nhận TTL đầy đủ mới.

### Ownership và current state

Cùng table không đồng nghĩa hai nguồn được overwrite mọi record. Mapping khai báo
owner theo key/field. Đề xuất mặc định: SPO ghi các key được gán cho SPO trong cùng
table; SQL-owned keys có thể resolve để làm lookup, nhưng update cần rule rõ ràng.

Writer SQL hiện dùng unconditional Upsert. Chỉ thêm timestamp check vào SPO không
ngăn SQL ghi đè dữ liệu mới hơn. Nếu cần hai nguồn cùng update một Point, phase
implementation phải bổ sung arbitration cho cả hai writer: event time, tie-break
giữa nguồn và conditional update theo RowVersion. SQL ordering cũ vẫn reading_time
rồi SQL id; không so SQL id và SharePoint id như cùng một sequence.

Khi chưa có rule key chung, trả SourceOwnershipConflict thay vì overwrite tùy ý.
SPO-owned Point cần conditional update để backfill/replay không hạ current state.
Không thay SQL raw rows, deterministic mappings, partition/TTL hoặc delivery ledger.

## 7. Capture và durable handoff

File flow dùng When a file is created or modified (properties only):

1. Lọc folder/temp file/path và resolve dataset configuration.
2. Đọc metadata, size, ETag trước; implementation hiện giới hạn <=50 MiB.
3. Get file content; đọc metadata sau. ETag khác thì retry capture latest.
4. Lưu raw vào Blob path riêng cho capture attempt.
5. Finalize Ready manifest bằng conditional create/update theo JobKey, chọn đúng
   một attempt hoàn chỉnh; không overwrite raw đã được chọn.
6. Enqueue Job ID sau khi manifest Ready.

Dispatcher định kỳ tìm Ready jobs chưa hoàn tất để enqueue/recover. Crash sau
archive trước enqueue không làm mất công việc. Duplicate messages được xử lý bằng
manifest identity/status. Không dựa vào trigger concurrency=1 để chống trùng.

List flow dùng When an item is created or modified, capture fields thành JSON.
Version/token phải thuộc payload thực sự đọc được; không gắn event ETag cũ vào
Get item latest. Nếu thiếu version đáng tin cậy, dùng canonical content hash.
List snapshot đi qua cùng mapper để ghi column Bronze, không cần table riêng.

MVP giữ được các snapshot đã capture, không bảo đảm lấy mọi revision trung gian
khi nguồn thay đổi quá nhanh. fmc_spofile có thể mirror latest archive/status tùy
chọn bằng field cũ; worker không lấy binary version cũ từ File column mutable đó.
Giữ nguyên source key của receipt cũ, không thêm SiteId vào key đã lưu nếu chưa migration.

## 8. Worker, receipts và parser

Các component Core/Functions sau đã có code local; SharePoint event flow, initial scan
và status UI vẫn phải tạo/bind sau khi có Azure subscription và connections:

| Component | Chức năng |
| --- | --- |
| SpoCaptureFinalizer | Chọn raw attempt, Ready manifest, enqueue idempotently |
| SpoJobStore | Blob manifest, lease/ETag, counters và checkpoints |
| SpoParserRegistry | JSON/CSV/XLSX/List adapters |
| SpoBronzeMapper | Input → typed Entity của table/column chuẩn hiện có |
| SpoBronzeWriter | Identity, ownership, dependency và Dataverse writes |
| SpoDeliveryLedger | Row outcomes, target GUID/partition trong Blob receipts |
| SpoReconciler | Ready jobs, retry, scan/checkpoint recovery |
| SPO.Ingestion.Functions | Azure host, queue consumer, scheduler, telemetry |

Reuse behavior đã kiểm chứng từ
[DataverseWriter](../../DataverseSyncWorker/Services/DataverseWriter.cs) và
[CommandProcessor](../../DataverseSyncWorker/Services/CommandProcessor.cs).
Không reuse mù ReadingMapper.Point/History cho event SPO vì chúng stamp SQL fields;
tách helper/conversion phù hợp và chạy SQL regression tests nếu refactor.

Worker dùng Blob lease/conditional manifest writes và queue visibility renewal.
Heartbeat độc lập với parse. Mất ownership thì dừng; owner cũ không complete job.
Queue message chỉ xóa sau terminal state durable; transient errors backoff/Retry-After,
quá ngưỡng lưu dead-letter receipt để retry có kiểm soát.

Validate toàn file trước write trong resource bounds MVP. Ghi theo dependency:
Building → Equipment → Point → History. Thiếu parent vì file parent chưa đến thì
WaitingDependency và bounded retry, không tạo parent rỗng.

Receipt theo job/partition/ordinal, target identity theo business/event key.
Chỉ acknowledge dòng khi mọi target write cấu hình cho dòng đó thành công.
Point thành công nhưng History lỗi thì replay cùng identities. Elastic partial
failure phải được đọc/kiểm tra đầy đủ; counters tính theo receipts, không cộng đôi.

CSV parser phải hỗ trợ quoted comma/newline, BOM/encoding policy. JSON có depth/row
bounds. XLSX có sheet/table selector, decompressed-size bounds, formula cached-value
policy và không thực thi macro. Không cam kết tự hiểu schema bất kỳ khi chưa mapping.

## 9. Partial writes, profiling và UI

Ghi trực tiếp Bronze khiến một phần rows có thể hiện trước khi toàn file hoàn tất.
Không có staging/publication table nên không tuyên bố atomic transaction toàn file,
đặc biệt với standard và elastic tables trong cùng job.

Validation failure trước write không thay dữ liệu. Remote failure giữa batch giữ
job Failed/Retrying với delivered/pending count; replay phần còn lại. Không rollback
bằng cách xóa records đã có. Xóa file hoặc thiếu row trong bản mới không tự xóa Bronze.

Profiling/error reports nằm trong Blob và telemetry; không thêm quality/lineage column.
Report nối Job ID → raw → row ordinal → target table/GUID/partition.

Người dùng upload/sửa List bằng SharePoint UI rồi xem records trong app hiện có.
Có thể implement status page gọi authenticated service endpoint và Retry/Reconcile
commands; không cần Dataverse job table. Đây là UI work mới, chưa có sẵn.

Reconcile dùng SharePoint connector quét đầy đủ pagination, source version và
job outcomes. Lưu scan cursor trên Blob. 403 hoặc partial scan không phải deletion.
Initial scan nhận file có trước khi bật flow, scheduled scan phục hồi missed events.

## 10. Silver

Dataflow tiếp tục đọc các table Bronze hiện có. Verify filters/mapping để data SPO
được nhận; không thêm generic JSON parse stage cho Silver. Dataset không map được
vào chuẩn hiện có phải báo lỗi contract, không tự tạo Employee hay table khác.

Gom completed jobs theo refresh window. Flow AutoRefreshSilveronNewBronzeData trong
source cần được kiểm tra live: nếu nó refresh từng row thì có thể đọc partial job.
Điều phối refresh sau job giúp vận hành nhưng không tạo atomic visibility cho app/
consumer khác. Watermark chỉ advance sau refresh thành công.

## 11. Phases implementation

| Phase | Deliverable | Nghiệm thu |
| --- | --- | --- |
| 1. Contract | Live metadata/key/Choice/lookup read, source file mapping, ownership | Đích là column hiện có, không schema provisioning |
| 2. Cloud ingress | Azure Storage/host, file trigger, raw manifest/queue | Multi-file khác tên, raw/hash đúng, không cần local |
| 3. Parser/dry run | JSON/CSV/XLSX, mapping config, preview typed writes | Preview rõ table/column/value/key/conflicts |
| 4. Bronze writes | Identity, TTL, lease/receipts, replay | Record vào table cũ; retry không duplicate |
| 5. List/reconcile/UI | Snapshots, full scan/pagination, status và retry | List vào cùng columns, missed events phục hồi |
| 6. Coexistence/Silver | Shared arbitration nếu key chung, SQL regression, refresh grouping | Không hạ current state, Silver nhận đúng mapping |

Preflight xác định Azure subscription/resource group, Storage, Functions host/runtime
được hỗ trợ, application identity, connections và license/capacity. Không dùng token
local làm runtime credential cloud. Đây là resource đề xuất, chưa được deploy.

Deploy flows Off → bind connections → dry run → pilot live → bật event/reconcile.
Tắt manual writer nếu cùng scope gây cạnh tranh. Solution artifacts cập nhật
flows/UI/reference cần thiết; không thêm table, column hoặc alternate key Dataverse.

Rollback dừng capture/consumer mới, giữ raw/receipts và dữ liệu đã ghi để reconcile.
Không reset SQL ledger, đổi SourceId hoặc xóa hàng loạt Bronze.

## 12. Test matrix

| Test | Kết quả bắt buộc |
| --- | --- |
| Metadata trước/sau | Không thêm table/column/key; Reading vẫn elastic |
| 20 CSV/JSON/XLSX khác tên, subfolder | Route/parse đúng, đúng typed columns |
| Source aliases và invalid schema | Config mapping hoặc lỗi; không tự tạo field |
| Choice/lookup/parent đến sau | Giá trị hợp lệ, GUID đúng, WaitingDependency phục hồi |
| Duplicate event/replace/copy file | Target business/event identity không duplicate |
| Crash sau remote partial success | Receipts reconcile, replay đúng phần còn lại |
| Crash Ready trước enqueue | Dispatcher phục hồi |
| Nhiều worker, lease expired | Stale worker không complete; writes idempotent/conditional |
| Backfill và SQL/SPO trùng key | Không hạ current state; ownership/conflict rõ |
| Event thuần SPO | Không giả SQL ID hoặc SQL ingestion timestamp |
| Expired reading replay | Không kéo dài TTL |
| CSV quoted newline, XLSX nhiều sheet/>256 rows | Đủ logical rows theo config/bounds |
| List updates, reconcile nhiều trang | Snapshot/version đúng, không mất do pagination |
| Local tắt | Ingestion cloud tiếp tục |
| App/Silver | Table cũ có data, phản ánh rõ partial-write limitations |

Parser tests, queue/lease fault injection, build/tests shared code và SQL regression
khi refactor; pilot live có delivery receipts và metadata diff. Source definition
hoặc local tests không phải bằng chứng cloud deployment.

## 13. Tham chiếu

- [SPO pilot plan](sharepoint-to-dataverse-file-ingestion.vi.md).
- [SPO runbook](../runbooks/spo-file-ingestion.vi.md).
- [SQL deployment contract](../reference/dataverse-deployment.md).
- [SharePoint connector](https://learn.microsoft.com/en-us/connectors/sharepoint/).
- [Power Automate limits](https://learn.microsoft.com/en-us/power-automate/guidance/coding-guidelines/understand-limits).
- [Azure Functions idempotency](https://learn.microsoft.com/en-us/azure/azure-functions/functions-idempotent).

Azure layout/queue, ownership và mapping là thiết kế đề xuất của dự án. Action,
runtime và khả năng triển khai cụ thể cần verify trong phase implementation.
