# Guideline demo kết quả tuần 23–30/09/2026

Thời lượng đề xuất: **15–20 phút**. Mục tiêu của buổi demo là kể một câu chuyện
liền mạch: POC đã chuyển từ đồng bộ dữ liệu thuần túy sang một nền tảng có quản
lý thay đổi schema, dữ liệu vận hành dễ kiểm tra, quy trình FM Request và thông
báo trong Power Apps.

## 1. Thông điệp chính

> Trong tuần qua, nhóm đã ổn định cách chạy và triển khai solution, đưa schema
> Dataverse vào cơ chế migration có tracking, chuyển Reading sang Standard table
> phù hợp với phạm vi demo, xây FM Request từ schema đến API/CRUD, và hoàn thiện
> notification cho Operator. Đồng thời Operations Center đã được sửa để đọc đúng
> bảng mới và hoạt động lại trên app đã publish.

Không nên mở đầu bằng danh sách commit. Hãy trình bày theo giá trị nghiệp vụ:

1. Dữ liệu BMS có nguồn gốc và luồng đồng bộ rõ ràng.
2. Thay đổi Dataverse có version và receipt, không còn phụ thuộc hoàn toàn vào
   thao tác tạo bảng/cột bằng tay.
3. Người vận hành có thể tạo FM Request và nhận thông báo ngay trong model-driven app.
4. Dashboard đọc đúng data contract mới và có kiểm thử chống regression.

## 2. Những việc đã làm trong tuần

Khoảng thời gian đối chiếu Git: **23/09/2026–30/09/2026**. Có 19 commit, gồm
17 commit nội dung và 2 merge commit.

| Nhóm kết quả | Việc đã làm | Bằng chứng chính | Trạng thái nên báo cáo |
| --- | --- | --- | --- |
| Trải nghiệm phát triển | Thêm Visual Studio multi-project launch để debug Fake API, Ingestion, Dataverse worker và SPO CLI; làm quy trình publish plug-in dùng được trên nhiều máy | `MetasysPoc.slnLaunch`, `scripts/Publish-FmcPlugin.ps1`, commit `ca70d08`, `d0dfbcd` | Source hoàn tất; dùng được cho dev local |
| Data contract BMS | Chuẩn hóa Building Code trong mapping; chuyển Reading sang Standard `fmc_bmsreadingsnapshot`; giữ SQL là system of record; thêm Equipment Code và đồng bộ Reading sau cutover | commit `4b4ff3d`, `291205a`; deployment receipt ngày 24/09 | Đã triển khai và smoke test trên Developer environment |
| Dataverse schema migration | Tạo `fmc_schemamigration`, cột Version/Applied On, runner theo thứ tự và receipt cho từng version; migration `003` tạo schema FM Request | commits `fdf3958`, `b9310dc`, `2e8550c`, `1cc9f69`, `a88e141`, `d699b72` | Framework và schema đã có; migration idempotent |
| FM Request | Tạo table `fmc_fmrequest`; service hỗ trợ Draft, Submit, Approve, Reject, Update và Delete theo trạng thái; expose endpoint tạo Draft | commits `d699b72`, `a42bd89`, `5d852a8` | Table và service có trong source; HTTP hiện mới expose Create |
| In-app notification | Plug-in async gửi notification cho các user có role `FMC BMS Demo Operator`; bổ sung quyền nhận notification và gán role cho user demo | commit `fa17bf1`; live smoke test ngày 30/09 | Đã deploy và test server-side thành công |
| Operations Center | Sửa dashboard không còn query bảng elastic đã retire; chuyển sang `fmc_bmsreadingsnapshot`, cập nhật binding và regression test | commit `fa17bf1` | Đã publish; Web API/readback và UI automation test pass |
| SharePoint change | Bổ sung source cho phát hiện thay đổi, queue/claim, banner và luồng sync sau thay đổi | commit `8fc66bf` | Trình bày là phần mở rộng đang hoàn thiện; cần kiểm tra live flow/worker trước khi demo |
| BMS assistant | Chuẩn bị Agent source, PCF entry và logic mở host Copilot/M365 Copilot | commits `4d22293`, `bd78b53` | Chỉ demo nếu host/licensing trong app đang hoạt động ổn định |

## 3. Chuẩn bị trước buổi demo

### 3.1. Chuẩn bị kỹ thuật

- Pull `main` và xác nhận ít nhất có commit `fa17bf1`.
- Chạy `dotnet build MetasysPoc.sln --no-restore` và lưu lại ảnh/output pass.
- Nếu demo full ingestion, trong Visual Studio chọn solution launch profile
  **All Services (Debug)** rồi nhấn F5. Các service chính:
  - Fake Metasys API: `http://localhost:5100/swagger`
  - Ingestion: `http://localhost:5200/swagger`
  - Dataverse worker: `http://localhost:5300/swagger`
- Nếu chỉ demo Dataverse/FM Request, chỉ cần chạy `Dataverse.SyncWorker.App` với
  profile `http`. Notification là Dataverse plug-in cloud nên không phụ thuộc
  worker local sau khi record đã được tạo trong Dataverse.
- Đăng nhập đúng Developer environment và mở app:
  [FMC BMS Demo](https://org06cbc9ec.crm5.dynamics.com/main.aspx?appid=d19f4897-d227-4df3-8361-988f97c53e89).
- Chuẩn bị sẵn các tab: Operations Center, BMS Readings, FM Requests, notification
  bell và Swagger của worker.
- Dùng tên request duy nhất, ví dụ `DEMO-FM-20260930-01`, để dễ tìm và tránh nhầm
  với dữ liệu cũ.
- Xác nhận tài khoản demo có role `FMC BMS Demo Operator`.

### 3.2. Dữ liệu và phương án dự phòng

- Giữ sẵn một FM Request đã tạo thành công và notification smoke-test cũ để trình
  bày nếu plug-in async mất thêm thời gian.
- Giữ output build/test và deployment receipt mở sẵn.
- Không xóa dữ liệu live trong lúc demo. Nếu cần dọn record demo, thực hiện sau
  buổi trình bày với đúng record ID.
- Không phụ thuộc vào Agent chat hoặc SharePoint cloud flow trong luồng demo chính.

## 4. Kịch bản demo 18 phút

### Phần A — Bối cảnh và kiến trúc, 2 phút

Mở Operations Center hoặc một slide kiến trúc đơn giản và nói:

> Fake Metasys phát COV qua SSE, Ingestion ghi dữ liệu gốc vào SQL Server.
> DataverseSyncWorker đọc SQL theo delivery ledger và đưa current state cùng
> Reading thuộc phạm vi demo lên Dataverse. Power Apps là lớp vận hành; Power
> Automate và plug-in xử lý trigger, notification và orchestration.

Luồng trình bày:

```text
Fake Metasys -> SSE Ingestion -> SQL Server (system of record)
                                  |
                                  v
                         DataverseSyncWorker
                                  |
                                  v
        Dataverse tables -> FMC BMS Demo -> Operator notification
```

Nhấn mạnh SQL giữ full history. `fmc_bmspoint` là current state;
`fmc_bmsreadingsnapshot` là Standard table cho ngày 10/09 và Reading sau cutover.

### Phần B — Migration ledger, 3 phút

Mở code `DataverseProvisioner.SchemaMigrationHistory.cs`, sau đó mở table
`fmc_schemamigration` trong Dataverse.

Nói:

> Thay đổi schema giờ được định nghĩa bằng code và ghi receipt theo version.
> Runner kiểm tra từng migration độc lập, chạy theo thứ tự 001, 002, 003. Nếu
> desired state đã tồn tại thì migration vẫn được coi là thành công; exception
> sẽ dừng và không ghi receipt.

Có thể chạy profile `migration-ledger` trước buổi demo. Trong buổi demo chỉ cần
cho thấy log `SKIP: already applied` và các receipt để chứng minh tính idempotent.
Migration `003` là ví dụ thực tế tạo FM Request table và các cột Name,
Description, Status.

### Phần C — Reading contract mới, 3 phút

Trong app mở **BMS Readings** và chọn một record mới.

Chỉ ra các trường:

- `Equipment Code`
- `SQL Reading ID`
- `Object ID` / `Object Name`
- `Reading Time`, `Reading Value`, `Unit`

Nói:

> Bảng elastic cũ đã retire. Dashboard và menu hiện đọc Standard
> `fmc_bmsreadingsnapshot`. ID được map deterministic từ SQL để retry không tạo
> dòng trùng. SQL vẫn là nguồn lịch sử đầy đủ; Dataverse phục vụ phạm vi vận hành
> và demo đã thống nhất.

Nếu demo sync trực tiếp, tạo tối đa 1–2 dòng test và chỉ chạy request có kiểm soát.
Không migrate lại toàn bộ lịch sử trong buổi demo.

### Phần D — FM Request đến notification, 6 phút

Đây là phần demo chính.

1. Mở **FM Requests** và tạo một record mới, hoặc gọi endpoint local:

   ```http
   POST http://localhost:5300/api/fm-requests
   Content-Type: application/json

   {
     "name": "DEMO-FM-20260930-01",
     "description": "Demo request created during weekly review"
   }
   ```

2. Mở record vừa tạo, chỉ Status mặc định là `Draft`.
3. Giải thích service đã có rule chuyển trạng thái:
   `Draft -> Submitted -> Approved/Rejected`; chỉ Draft được sửa hoặc xóa.
4. Chờ plug-in async xử lý. Điều hướng sang một page khác rồi quay lại hoặc chờ
   polling của Power Apps, sau đó mở notification bell.
5. Mở notification **New FM Request** và kiểm tra link đưa về đúng record.

Talk track:

> Sau khi `fmc_fmrequest` được tạo, plug-in PostOperation chạy async nên lỗi gửi
> notification không rollback request nghiệp vụ. Plug-in tìm các active user có
> role `FMC BMS Demo Operator`, kể cả role qua team, rồi gửi native
> `SendAppNotification` riêng cho từng user.

Kết quả mong đợi:

- Record FM Request được tạo đúng một lần với Status = Draft.
- Notification xuất hiện cho Operator.
- Click notification mở đúng FM Request.

Lưu ý trung thực: API local hiện chỉ expose Create; các thao tác CRUD/chuyển trạng
thái còn lại đang có ở service nhưng chưa được expose thành đầy đủ HTTP API hoặc
custom page. Không gọi endpoint này là Dataverse Custom API.

### Phần E — Operations Center và regression fix, 2 phút

Quay về **Operations Center**, nhấn Refresh và chỉ các KPI/data section tải được.

Nói:

> Sau khi retire elastic table, page cũ vẫn query `fmc_bmsreading`, làm toàn bộ
> `Promise.all` fail và hiện “Unable to load data”. Tuần này binding và query đã
> chuyển sang `fmc_bmsreadingsnapshot`; source và generated page đều có test bảo
> đảm không gọi lại bảng đã retire.

Không dùng số KPI live làm cam kết cố định vì dữ liệu có thể thay đổi theo thời
điểm. Giá trị cần chứng minh là page tải thành công và đọc đúng table contract.

### Phần F — Tổng kết và bước tiếp theo, 2 phút

Kết luận bằng ba ý:

1. **Quản trị được thay đổi:** schema migration có version và receipt.
2. **Có use case nghiệp vụ:** tạo FM Request và báo cho đúng nhóm Operator.
3. **App vận hành ổn định hơn:** Reading contract mới và Operations Center đã
   đồng bộ với schema thực tế.

Bước tiếp theo đề xuất:

- Hoàn thiện custom page tạo/sửa FM Request trong model-driven app.
- Expose đầy đủ transition Submit/Approve/Reject theo quyền và audit trail.
- Bổ sung notification read/unread hoặc acknowledgment theo từng user nếu nghiệp
  vụ yêu cầu, thay vì chỉ dựa vào native notification center.
- Hoàn tất kiểm thử live cho SharePoint debounce/auto-sync và Agent host trước khi
  đưa hai phần này vào luồng demo chính.
- Chuẩn hóa service identity và quy trình deploy cho môi trường ngoài Developer.

## 5. Câu hỏi thường gặp và câu trả lời ngắn

**Vì sao không tạo table thủ công?**  Maker portal vẫn hữu ích để kiểm tra UI,
nhưng schema quan trọng được định nghĩa bằng SDK và migration receipt để có thể
review, chạy lại idempotent và biết environment đã áp dụng version nào.

**Vì sao SQL vẫn là system of record?**  SQL giữ full raw history và source ID.
Dataverse cung cấp current operational state, workflow và phạm vi Reading cần cho
app; không thay thế toàn bộ raw store.

**Notification có gửi cho tất cả mọi người không?**  Không. Plug-in chỉ gửi cho
active user có role `FMC BMS Demo Operator`, trực tiếp hoặc qua team.

**Notification có làm hỏng thao tác tạo request khi gửi lỗi không?**  Không rollback
record vì step chạy async PostOperation; lỗi vẫn cần theo dõi trong system jobs/
plug-in trace.

**Custom page FM Request đã hoàn tất chưa?**  Chưa nên báo là hoàn tất. Table,
service, create endpoint, app navigation và notification đã có; custom page canvas
chuyên biệt vẫn là phần tiếp theo.

**Agent đã production-ready chưa?**  Chưa. Source và host integration đã được
chuẩn bị, nhưng còn phụ thuộc feature availability, license, identity và tool
verification trong Power Apps runtime.

## 6. Checklist nghiệm thu buổi demo

- [ ] App mở đúng Developer environment và đúng `FMC BMS Demo`.
- [ ] Operations Center tải được, không còn lỗi retired `fmc_bmsreading`.
- [ ] Mở được BMS Reading có Equipment Code và SQL Reading ID.
- [ ] Migration ledger có receipt 001, 002, 003 hoặc log thể hiện trạng thái đúng.
- [ ] Tạo được một FM Request mới ở Status Draft.
- [ ] Operator nhận được native in-app notification và mở đúng record.
- [ ] Không trình bày custom page, Agent hoặc SPO cloud deployment như đã hoàn tất
  nếu chưa kiểm tra live ngay trước buổi demo.
- [ ] Có ảnh/output dự phòng cho build, smoke test và notification receipt.

## 7. Bằng chứng tham chiếu

- Git range: `ca70d08` đến `fa17bf1`.
- Dataverse contract và deployment receipts:
  [`docs/reference/dataverse-deployment.md`](../reference/dataverse-deployment.md).
- App demo runbook:
  [`docs/runbooks/model-driven-app-demo.vi.md`](model-driven-app-demo.vi.md).
- FM Request custom page plan:
  [`docs/plans/fm-request-custom-page.vi.md`](../plans/fm-request-custom-page.vi.md).
- Source migration:
  `Dataverse.SyncWorker/Dataverse.SyncWorker.DataAccess/Services/DataverseProvisioner.SchemaMigrationHistory.cs`.
- Source FM Request:
  `Dataverse.SyncWorker/Dataverse.SyncWorker.DataAccess/Services/FmRequestService.cs`.
- Source notification:
  `Dataverse.Plugin/FMCentralBms.Plugins/Plugins/NotifyNewFmRequest.cs`.
