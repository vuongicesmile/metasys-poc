# Epic 3 — FM Request workflow

Environment: Developer `org06cbc9ec`; solution `FMCentralBms`; app **FMC BMS Demo**.

## Mở và demo

[Mở FMC BMS Demo](https://org06cbc9ec.crm5.dynamics.com/main.aspx?appid=d19f4897-d227-4df3-8361-988f97c53e89).
Refresh app sau khi publish. Trong nhóm menu **FM Workflow** bên trái:

1. **FM Requests → New**: nhập Name. Trong tab **Workflow details**, nhập Description, Request Type, Department và Estimated Value. Trong panel **Reading bằng chứng**, lọc và chọn reading ngay trên form New, rồi bấm **Lưu & gửi phê duyệt** để lưu Draft, chụp evidence và Submit liên tiếp.
2. Demo dùng Department **EPIC3-DEMO**. Đã có 2 bước cho mỗi loại CIWG/Risk/Project, cùng tài khoản demo để dễ trình diễn. Đây không phải quy tắc phân tách người tạo/người duyệt dùng cho production.
3. Nếu chỉ bấm **Lưu Draft**, vào **My Requests & Approvals → Yêu cầu của tôi** để mở lại; nếu đã bấm **Lưu & gửi phê duyệt**, request đi thẳng tới bước chờ duyệt. Với Draft đã lưu, panel vẫn cho phép lọc/thay reading trước khi bấm **Gửi phê duyệt**.
   Nút **Save** chuẩn trên command bar tự Submit sau PostSave; dùng nút **Lưu Draft** trong panel nếu muốn lưu mà chưa gửi. Evidence vẫn là tùy chọn của workflow, nhưng nếu đã chọn thì server sẽ chụp snapshot trong lần Save đó.
4. Request sang **In Approval**, hiện bước 1, người duyệt và hạn. Flow gửi email kèm link mở request.
5. **Chờ tôi duyệt**: nhập nhận xét → **Duyệt bước này**. Bước 1 chuyển sang bước 2; bước cuối chuyển Approved. **Từ chối** kết thúc ở Rejected.
6. Người tạo nhập nhận xét → **Đóng request** khi Approved/Rejected. Lịch sử giữ actor, UTC timestamp, bước và comment.
7. Risk: thêm Likelihood/Impact 1–5, Risk Severity, Mitigation, Review Date, Owner là user. Mở **Risk register** để xem heatmap. Số liệu chỉ tính các trang đã tải; dùng **Xem thêm** để tải tiếp.

Native form vẫn làm CRUD; không cần chạy worker local cho workflow này. Chỉ Draft của người sở hữu được sửa/xóa. Không sửa trực tiếp Status để duyệt — plugin chặn cả form, API và import. Canvas **New FM Request** hiện có vẫn là điểm tạo Draft; bổ sung các thông tin cần duyệt trong native form.

## Reading bằng chứng

Trong **FM Requests → New**, panel **Reading bằng chứng** hoạt động ngay cả khi request chưa có ID. Đây là luồng khuyến nghị để không phải Save rồi mở lại:

Nếu app đã mở trước lần publish ngày 01/10/2026, đóng record và refresh toàn bộ
app trước khi kiểm tra. Main form phải là component của `FMC BMS Demo`; script
deployment hiện gọi `AddAppComponents`, publish app rồi đọc lại membership để
không tái diễn trường hợp form có tab trong metadata nhưng app chưa sử dụng form.

1. Nhập Point, Equipment hoặc SQL reading ID; có thể chọn Unit (ví dụ `C`) và khoảng thời gian theo giờ máy.
2. Dùng **Điều kiện bất thường** để chỉ lấy giá trị `Cao hơn ngưỡng`, `Thấp hơn ngưỡng` hoặc `Ngoài khoảng`. Ví dụ: Point `TEMP-001`, Unit `C`, `Cao hơn ngưỡng`, `40` chỉ lấy nhiệt độ trên 40°C. Bấm **Tìm readings** → kiểm tra thời gian/giá trị → **Chọn reading**. Dùng **Xem thêm readings** để phân trang. Trên mobile, mỗi kết quả hiển thị dạng card.
3. Bấm **Lưu & gửi phê duyệt**. Client lưu các trường form và `fmc_evidencereadingid`; sau khi Create thành công, gọi Submit ngay. Plugin server đọc `fmc_bmsreadingsnapshot` và tạo snapshot trong Create transaction, sau đó lifecycle API ghi Submit/history/outbox.
4. Nếu chỉ bấm **Lưu Draft**, có thể thay/gỡ reading trong Draft rồi mới Submit. Sau Submit, người duyệt thấy cùng snapshot và không thể thay/gỡ bằng chứng; reading mới không cập nhật lại snapshot.

Mỗi request có một reading bằng chứng chính; bằng chứng là tùy chọn để giữ luồng CIWG/Risk/Project không liên quan BMS. Draft có thể thay hoặc gỡ bằng chứng. Chọn lại cùng reading giữ bản chụp cũ; muốn chụp lại nguồn đã được sửa, gỡ rồi đính kèm lại khi còn Draft. Chỉ đọc readings đã đồng bộ vào Standard table; không truy vấn trực tiếp toàn bộ lịch sử SQL hoặc tự chạy sync.

Schema migration **006** thêm `fmc_evidencereadingid` (String 36, GUID nguồn được server xác thực) và `fmc_evidencesnapshot` (Memo 12000, server quản lý). Source reference là ID dạng text, không phải lookup cần quyền Append To vào telemetry. Quyền hiện có Read readings và Write Draft được sử dụng; không cấp thêm role. Xóa/sửa nguồn sau này không xóa bản chụp đã lưu. Người có quyền đọc request cũng đọc được bằng chứng trong request.

Guard synchronous Create/Update chặn snapshot tự nhập; chỉ chấp nhận nguồn đầy đủ và người đính kèm đọc được. UI dùng ETag/If-Match khi thay bằng chứng để phát hiện request đã thay đổi; Submit tăng revision và khóa cả request. Hướng dẫn nền tảng: [conditional Web API operations](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/perform-conditional-operations-using-web-api).

Triển khai theo thứ tự migration → signed plug-in → `python scripts/provision-fm-workflow.py deploy-evidence-ui`. Mode UI này chỉ cập nhật form request và workflow webresources. Dùng đường dẫn tuyệt đối cho `--plugin-path` vì `dotnet run --project` đổi working directory.

Kiểm tra riêng bằng chứng:

```powershell
python scripts/verify-fm-evidence.py inspect
python scripts/verify-fm-evidence.py rollback-smoke
```

`inspect` chỉ đọc metadata và một reading. `rollback-smoke` thử create/attach và chặn sửa snapshot, sau đó kiểm tra update bị chặn trên một request không còn Draft trong các [atomic change sets](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/execute-batch-operations-using-web-api). Mỗi change set có bước lỗi bắt buộc cuối cùng: mọi thay đổi bị rollback, không commit thông báo. Request có sẵn được đọc lại để xác nhận không đổi. Receipt nằm `.artifacts/fm-reading-evidence`. Đây không phải kiểm chứng live toàn bộ Submit → Approve: thử Submit bên trong change set đã bị lifecycle guard hiện có từ chối ở cập nhật status, nên không dùng cách đó để kết luận luồng Submit thông thường. Unit/browser tests kiểm tra luồng gửi/duyệt với bằng chứng; browser dùng Dataverse giả lập, không thay thế kiểm tra phiên Power Apps thật.

## Full flow

`Draft → Submit → In Approval (step 1 … n) → Approved hoặc Rejected → Close`

- Custom API `fmc_TransitionFmRequest` kiểm tra trạng thái, người gọi, revision và operation ID; update request + append history + tạo email outbox trong cùng transaction.
- Flow **FMC - Send User Email Notification** tiêu thụ `fmc_notification` Pending → gửi Outlook → gửi Microsoft Teams bằng Flow bot vào chat của người nhận → ghi Sent khi cả hai kênh thành công. Nếu Outlook hoặc Teams lỗi, flow ghi Failed với mã `EMAIL-001` hoặc `TEAMS-001`. Body là HTML card responsive gồm brand header, status badge, step/approver/due, workflow note và CTA mở request. Đây là flow dùng lại, không phải một flow mới cho từng request type.
- Flow **FMC - Process FM Request Deadlines** chạy mỗi 5 phút → đọc request đang duyệt → gọi `fmc_ProcessFmRequestDeadline` → plugin kiểm tra giờ UTC → reminder/escalation + history + email outbox.
- Nhắc 1 lần mỗi bước trước hạn. Khi vừa quá hạn nhưng chưa tới giờ escalation, gửi **Overdue** một lần. Đến `due + escalation hours`, quyền duyệt chuyển sang escalation user, vẫn In Approval. Nếu escalation hours = 0, một thông báo escalation đồng thời báo quá hạn. Không tự động approve hoặc reject.
- Bell khi **tạo** FM Request giữ nguyên plugin gửi cho Operator đã có; email workflow và bell là hai kênh khác nhau.

Email chứa link về app để quyết định sau khi đăng nhập; chưa phải nút Approve/Reject trực tiếp trong Outlook hoặc Microsoft Approvals connector.

## Cấu hình Approval Routes

Mở **Approval Routes** bằng tài khoản System Administrator hiện có. Không tự cấp thêm quyền admin cho Operator.

| Trường | Ý nghĩa |
|---|---|
| Request Type | CIWG, Risk hoặc Project |
| Department | Rỗng = mọi department; giá trị cụ thể ưu tiên hơn mặc định |
| Step No | Thứ tự bước, số dương; không bắt buộc liên tiếp |
| Min Value / Min Severity | Điều kiện tối thiểu để bước khớp |
| Approver Email | Phải khớp đúng 1 active Dataverse user |
| Approver Role | Nếu điền, cả approver/escalation user phải có role trực tiếp hoặc qua team |
| Escalation Email | User nhận quyền duyệt khi quá hạn |
| SLA Days | 1–365 ngày, tính từ lúc bước được giao |
| Reminder Hours | Số giờ trước hạn; 0 = không gửi nhắc trước hạn |
| Escalation Hours | Số giờ sau hạn; 0 = escalation khi tới hạn |
| Enabled | Tắt để route không được chọn khi submit mới |

Tại mỗi Step No: ưu tiên department cụ thể, rồi threshold value cao nhất, rồi severity cao nhất trong các rule khớp. Hai rule ngang nhau bị báo ambiguous, không đoán người duyệt. Tối đa 20 bước. Snapshot route vào request tại Submit; sửa route không đổi luồng đã chạy. Approver cần còn active và còn role được cấu hình khi quyết định. Risk Severity là giá trị được khai báo, chưa có ma trận phân loại Low/Medium/High do nghiệp vụ duyệt; heatmap dùng Likelihood × Impact.

Route demo SLA 1 ngày và Reminder Hours 24: nhắc sẽ đủ điều kiện ngay, được scheduler xử lý ở lần quét tiếp theo. Escalation vẫn chờ đúng hạn, không có chế độ giả giờ trên cloud.

## Code ở đâu, debug thế nào

| Thành phần | File |
|---|---|
| Contract tên cột/API/choice | `DataverseSchema.FmWorkflow.cs` |
| Migration 004/005 | `DataverseProvisioner.FmWorkflow.cs` |
| Đăng ký Custom API/guard | `DataversePluginProvisioner.FmWorkflow.cs` |
| State transitions + concurrency | `Plugins/TransitionFmRequest.cs` |
| Chặn cập nhật/xóa trái lifecycle | `Plugins/GuardFmWorkflow.cs` |
| Chọn route, validate, snapshot | `Services/FmWorkflowPolicy.cs` |
| User/role, history, email outbox | `Services/FmWorkflowStore.cs` |
| .NET integration adapter | `Services/FmRequestService.cs` |
| UI solution webresource | `dataverse/webresources/fmc_/pages/FmWorkflow.html` + `scripts/FmWorkflow.js` |
| Flow/forms/navigation deployment | `scripts/provision-fm-workflow.py` |

Plugin chạy ở Dataverse sandbox, không vào breakpoint của local worker khi bấm nút trong Power Apps. Xem Plugin Trace Log: `FM guard scope` hoặc `FM workflow`. Request chứa `fmc_workflowrevision`; refresh trước quyết định nếu báo Request changed. Không catch lỗi SDK rồi tiếp tục ghi receipt.

`OperationId` là GUID do client tạo cho mỗi quyết định. Retry cùng ID, cùng request/actor/action/comment trả receipt cũ, không ghi mail lần hai. ID dùng cho lệnh khác bị từ chối. `ExpectedRevision` cộng với Dataverse row version chống hai người đồng thời quyết định. Lịch sử được dùng làm receipt và không cho sửa/xóa qua CRUD.

## Deploy và test

Đảm bảo Azure CLI trên PATH, login đúng tenant; helper token không được in ra console. Dùng Debug nếu Release đang bị Visual Studio/worker giữ DLL.

```powershell
dotnet build MetasysPoc.sln
dotnet test tests/MetasysPoc.Tests
node --test scripts/tests/fm-workflow-ui.test.cjs
node scripts/tests/fm-workflow-browser.cjs
python -m unittest scripts/tests/test_fm_workflow_deployment.py

dotnet run --project Dataverse.SyncWorker/Dataverse.SyncWorker.App --no-launch-profile -- --ensure-migration-ledger
dotnet build Dataverse.Plugin/FMCentralBms.Plugins -c Release
# Tăng assembly revision trước khi publish code mới; registrar bỏ qua cùng version.
dotnet run --project Dataverse.SyncWorker/Dataverse.SyncWorker.App --no-launch-profile -- --register-plugin --plugin-path=Dataverse.Plugin/FMCentralBms.Plugins/bin/Release/net48/FMCentralBms.Plugins.dll
python scripts/provision-fm-workflow.py deploy-ui
python scripts/provision-fm-workflow.py deploy-flow
python scripts/provision-fm-workflow.py verify
```

Chỉ dùng `seed-demo` và `test-fm-workflow-live.py run` khi muốn tạo dữ liệu demo và gửi mail thật về tài khoản demo. Test giữ records/history làm bằng chứng; không xóa business data. `receipts` chỉ đọc kết quả. Các test trình duyệt chạy offline với Dataverse giả lập, không thay thế kiểm tra trực quan phiên Power Apps thật.

## Vận hành và giới hạn

- Request cũ Submitted nhưng không có workflow snapshot không được tự nhập vào approval. Không sửa hàng loạt trạng thái/record cũ; tạo Draft mới hoặc thiết kế chuyển đổi riêng.
- `fmc_requeststatus` là trạng thái chính; `fmc_status` legacy được mirror để tương thích.
- `fmc_FmAppUrl` cấu hình link email; URL phải HTTPS, chỉ một current value. Default trỏ app demo này; triển khai environment khác phải đổi cấu hình/target rõ ràng.
- Deadline flow đang dùng connection demo có System Administrator; chưa phải service principal production least-privilege. Không cấp thêm role cho người dùng trong lần triển khai này.
- UI My Requests/Pending là bộ lọc tiện dụng, không thay quyền đọc Dataverse hiện có của Operator.
- Notification outbox deduplicate việc tạo event; không cam kết exactly-once delivery của Outlook hoặc Teams. Teams chạy sau Outlook, nên retry thủ công một receipt lỗi Teams có thể gửi lại email. Khi Failed hoặc Pending lâu: xem `fmc_flowrunid`/flow run, không tự tạo lại notification hàng loạt.
- Scheduler phân trang đến 100.000 request; chưa phải kiểm thử tải/capacity. SLA là thời gian lịch UTC, chưa có business calendar/ngày nghỉ.
- Có thể tắt riêng deadline flow để dừng reminder/escalation. Đừng tắt guard/rollback DLL khi còn request đang duyệt. Backup form/sitemap trước deploy nằm `.artifacts/epic3-workflow`; migration không drop table.

## Nguồn thiết kế

Epic 3, FR-3.1–FR-3.7: `docs/reference/FM_Requirement_and_Techincal_Solution.pdf`, trang 9. Đối chiếu [Custom API](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api), [optimistic concurrency](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/optimistic-concurrency), [webresource trên form](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/use-iframe-and-web-resource-controls-on-a-form), [publish model-driven app](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/create-manage-model-driven-apps-using-code), [scheduled Power Automate](https://learn.microsoft.com/en-us/power-automate/run-scheduled-tasks).

## Receipt Developer — 30/09/2026

- Signed plugin `FMCentralBms.Plugins` **1.0.0.12**. Migration 004 receipt `98ec6dcd-a7bc-f111-aaaf-00224819a344`; migration 005 receipt `06e29d5a-abbc-f111-aaaf-00224819a344`.
- Hai Custom API đã bind vào plugin; 10 synchronous PreOperation guards active. Live app validation: `ValidationSuccess=true`, không có issue. Webresource hash khớp source sau publish.
- Flow deadline `59414d43-f813-4a86-a331-96851e43a77f` đang On. Scheduled run `08584108493637747043961708176CU02` **Succeeded**, từ `2026-09-30T08:38:41Z` đến `08:38:42Z`.
- CIWG `36bcf7b2-04ea-4b41-8e43-df79b8446105`: Submit → reminder API → Approve bước 1 → Approve bước 2 → Close. 5 email receipt **Sent**, mỗi email 1 attempt.
- Risk `d8361ab9-1888-43db-af36-75cadcc04bca`: Submit → Reject → Close. 3 email receipt **Sent**.
- Project `f9417974-5ade-441b-89a3-401a17636058`: Submit, không gọi deadline API thủ công. Scheduled flow tự tạo Remind history lúc `08:38:42Z`; email Sent lúc `08:38:44Z` (15:38:44 giờ Việt Nam). Giữ request ở In Approval để demo.
- Request kiểm thử `4bf1ce60-cb34-49d6-9ba6-53362b75db03`: gửi 2 quyết định đồng thời cùng revision; **1 thành công**, **1 HTTP 412 RowVersion mismatch**. Đã Reject/Close sau test, không phải yêu cầu vận hành thật.
- Retry cùng OperationId không ghi thêm history; stale revision bị từ chối; direct status edit/edit sau Submit/history overwrite và xóa request đã có history đều bị chặn. Khi thiếu app URL, Submit thất bại và request vẫn Draft, không ghi receipt: kiểm chứng rollback thật.
- Local: solution build **0 warning/0 error**, **94 .NET tests**, **3 JS policy tests**, **2 flow-definition tests**, browser regression desktop/mobile (submit/approve, escape comments, không lỗi JavaScript) đều pass.
- Flow/export artifacts nằm `.artifacts/epic3-workflow`; các component Epic 3 được merge vào `dataverse/FMCentralBms`. Không pull đè các table/layout Gold/Silver không thuộc task.

Giới hạn bằng chứng: test end-to-end approve/reject/reminder và concurrency chạy với tài khoản demo hiện có (cũng có System Administrator), không phải kiểm thử một danh tính chỉ có Operator. Authorization negative tests dùng fake host; không cấp quyền mới để tạo user thử. Overdue/escalation được unit-test với thời gian quá hạn; chưa chờ một ngày để kiểm chứng end-to-end cloud. Browser regression dùng data giả lập; phiên này không có browser connection để kiểm tra trực quan app đang đăng nhập. Các test lifecycle live được thực hiện ở v1.0.0.11; v1.0.0.12 thêm nhánh Overdue và đã build/test/provision lại. Không khẳng định production readiness.
