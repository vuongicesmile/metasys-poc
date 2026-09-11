# Plan nâng cao — BMS Maintenance Engine

Trạng thái: **đề xuất, chưa triển khai**. Ngày: 2026-09-10.

## 1. Ý tưởng: biến app BMS thành nơi xử lý sự cố thiết bị

Người vận hành phát hiện thiết bị có vấn đề, tạo phiếu bảo trì, chọn người xử lý,
giao việc và theo dõi tiến độ. Plugin kiểm soát quy trình ở server, tự ghi các
mốc thời gian và lưu lịch sử thay đổi trạng thái.

Ví dụ demo: cảm biến nhiệt độ phòng kỹ thuật cần kiểm tra. Người vận hành tạo
phiếu `Kiểm tra cảm biến phòng kỹ thuật`, chọn Equipment, giao cho kỹ thuật viên.
Kỹ thuật viên bắt đầu xử lý, rồi thử hoàn tất khi chưa có kết quả sửa chữa:
app báo lỗi. Sau khi nhập kết quả, hoàn tất thành công và xuất hiện lịch sử.

Đây là **một nghiệp vụ gồm nhiều plugin phối hợp**, nâng cấp từ các bài kiểm tra
một field. Giá trị học được: đọc record liên quan, Pre Image, state transitions,
sửa Target trong PreOperation, tạo lịch sử trong PostOperation, transaction,
phân quyền và xử lý hai người thao tác cùng lúc.

## 2. Trải nghiệm mong muốn trên UI

Thêm mục **Maintenance** vào `FMC BMS Demo`:

| Màn hình | Nội dung |
| --- | --- |
| Open Work Orders | Phiếu Draft, Assigned, In Progress |
| My Work Orders | Phiếu có Technician là người đang đăng nhập |
| Overdue Work Orders | Phiếu chưa kết thúc và Due At nhỏ hơn thời điểm hiện tại |
| Completed Work Orders | Phiếu đã hoàn tất, kết quả và thời điểm hoàn tất |
| Work Order detail | Thông tin phiếu, Equipment, người xử lý, thời gian, subgrid lịch sử |

Form chia ba tab: **Details**, **Resolution**, **History**.

- Bản đầu: thay đổi `Stage` trên form rồi Save để học các plugin chuẩn.
- Bản nâng cao: các nút **Assign**, **Start**, **Complete**, **Cancel**, **Reopen**.
  Complete mở hộp nhập kết quả; Cancel/Reopen yêu cầu lý do.
- Trường thời gian và lịch sử chỉ đọc trên UI; server cũng kiểm soát việc ghi.
- Notification dùng lời dễ hiểu, ví dụ `Chưa thể hoàn tất: cần nhập kết quả xử lý`.

Người dùng cuối thao tác trên app. Nút trong bản nâng cao tự gọi Custom API
phía sau; người dùng không cần PowerShell, Postman hay nhập request API.

## 3. Phạm vi và giả định của đề xuất

Giữ nguyên pipeline simulator → ingestion → SQL → sync worker. Phiếu bảo trì là
dữ liệu nghiệp vụ mới trong Dataverse, không phải bản sao của raw readings.

| Dữ liệu | Nguồn quản lý |
| --- | --- |
| Catalog Building/Equipment và lịch sử reading | SQL và worker hiện có |
| Phiếu bảo trì, phân công, kết quả, lịch sử phiếu | Dataverse |
| Giá trị point hiện tại | Worker cập nhật; app có thể hiển thị để tham khảo |

Không sửa SourceId, deterministic GUID, partition, TTL hay delivery ledger.
Không tự ghi ngược kết quả bảo trì vào SQL hoặc gửi lệnh tới Metasys.
Thiết bị simulator không chứng minh khả năng thao tác trên thiết bị thật.

Các lựa chọn sau là giả định thiết kế, chưa phải yêu cầu vận hành đã xác nhận:

- Phiếu được tạo thủ công; một Equipment được phép có nhiều phiếu.
- Technician là Dataverse user, chưa cần mô hình đội/ca trực.
- Không gửi email/Teams trong phạm vi bản đầu.
- Chỉ Supervisor được Reopen/Cancel; quyền này cần xác định tài khoản và role
  khi triển khai, không tự cấp quyền quản trị.
- Không kết luận thiết bị hỏng chỉ vì một reading cao: chưa có ngưỡng, đơn vị,
  thời gian duy trì và quy tắc chống nhiễu được xác nhận.

## 4. State machine

Các trạng thái nghiệp vụ nằm trong custom Choice `fmc_stage`.
Không dùng lẫn `statecode/statuscode` của Dataverse để biểu diễn cùng quy trình.
Trong bản đầu record vẫn Active; việc archive/inactivate là mở rộng riêng.

| Từ | Sang | Điều kiện |
| --- | --- | --- |
| Draft | Assigned | Có Equipment, Technician, Priority, Due At hợp lệ |
| Assigned | In Progress | Caller là Technician được giao hoặc Supervisor |
| In Progress | Completed | Có Resolution ít nhất 20 ký tự sau Trim; caller có quyền xử lý |
| Draft / Assigned / In Progress | Cancelled | Supervisor, có Reason ít nhất 10 ký tự |
| Completed | In Progress | Supervisor, có Reason ít nhất 10 ký tự; tăng Reopen Count |
| Cùng trạng thái | Cùng trạng thái | Chỉ sửa field được phép; không tạo thêm transition history |

Mọi đường khác đều bị từ chối. Ví dụ Draft → Completed không được phép dù đã
nhập Resolution. Cancelled là kết thúc; muốn làm lại thì tạo phiếu mới.

Field policy đề xuất:

- Equipment chỉ được đổi khi Draft; sau Assign phải giữ nguyên.
- Technician/Priority/Due At chỉ được điều phối ở Draft hoặc Assigned.
- Khi In Progress chỉ sửa mô tả, kết quả, lý do và thực hiện transition hợp lệ.
- Completed/Cancelled không được sửa nội dung trực tiếp; Completed chỉ mở lại
  qua transition Reopen được cấp quyền.
- Reopen giữ `Started At` đầu tiên, xóa `Completed At` hiện tại, giữ kết quả cũ
  trong history và xóa Resolution trên phiếu để yêu cầu kết quả mới.

Không coi “giữ nguyên stage” là bỏ qua tất cả validation: người dùng vẫn có thể
đang sửa Equipment hoặc giả mạo timestamp trong cùng request.

## 5. Thiết kế table

### 5.1. `fmc_bmsworkorder` — phiếu bảo trì

Standard table, user/team-owned. Prefix `fmc`, trong solution `FMCentralBms`.

| Logical name dự kiến | Kiểu | Mục đích |
| --- | --- | --- |
| `fmc_name` | Text 200, primary name | Tiêu đề phiếu |
| `fmc_workordernumber` | Autonumber, format `WO-{SEQNUM:6}` | Số dễ đọc; cho phép có khoảng trống giữa các số |
| `fmc_equipmentid` | Lookup → `fmc_bmsequipment` | Thiết bị cần xử lý |
| `fmc_priority` | Choice | Low, Normal, High, Critical |
| `fmc_stage` | Choice | Draft, Assigned, In Progress, Completed, Cancelled |
| `fmc_technicianid` | Lookup → `systemuser` | Người phụ trách; không đồng nghĩa owner |
| `fmc_description` | Multiline Text 4000 | Hiện tượng quan sát |
| `fmc_resolution` | Multiline Text 4000 | Kết quả xử lý |
| `fmc_transitionreason` | Multiline Text 1000 | Lý do Cancel/Reopen; chụp vào history rồi xóa |
| `fmc_dueat` | DateTime, User Local | Hạn xử lý; lưu UTC, hiển thị theo múi giờ người dùng |
| `fmc_assignedat` | DateTime, User Local | Lần giao việc đầu tiên |
| `fmc_startedat` | DateTime, User Local | Lần bắt đầu đầu tiên |
| `fmc_completedat` | DateTime, User Local | Hoàn tất gần nhất; xóa khi Reopen |
| `fmc_cancelledat` | DateTime, User Local | Thời điểm hủy |
| `fmc_reopencount` | Whole Number | Số lần mở lại, server quản lý |

Dùng `createdon/createdby/modifiedon/modifiedby` có sẵn.
Không thêm Building lookup lặp lại ở bản đầu: hiển thị qua Equipment để tránh
hai lookup sai nhau. Nếu cần Building tại thời điểm sự cố, bổ sung snapshot
có tên/ý nghĩa rõ ràng trong một mở rộng sau.

Priority → hạn mặc định đề xuất: Critical 4h, High 24h, Normal 72h, Low 168h.
Đây là giờ liên tục, không phải giờ làm việc và chưa phải SLA cam kết.
Create sẽ điền Due At nếu thiếu. Nếu người dùng nhập hạn riêng thì kiểm tra
không nằm trước thời điểm tạo; Priority đổi sau đó không tự kéo dài hạn đã có.

### 5.2. `fmc_bmsworkorderhistory` — lịch sử transition

Standard table, user/team-owned để có thể kiểm soát phạm vi đọc.

| Logical name dự kiến | Kiểu | Mục đích |
| --- | --- | --- |
| `fmc_name` | Text 200 | Ví dụ `Assigned → In Progress` |
| `fmc_workorderid` | Lookup → Work Order | Phiếu cha |
| `fmc_fromstage` | Choice, nullable | Null cho lịch sử Create |
| `fmc_tostage` | Choice | Stage sau thao tác |
| `fmc_action` | Choice | Created, Assigned, Started, Completed, Cancelled, Reopened |
| `fmc_actorid` | Lookup → systemuser | Người khởi tạo nghiệp vụ, lấy từ context server |
| `fmc_occurredat` | DateTime, User Local | Timestamp server |
| `fmc_reason` | Multiline Text 1000 | Lý do của transition |
| `fmc_resolution` | Multiline Text 4000 | Snapshot kết quả ở lần hoàn tất/mở lại |
| `fmc_correlationid` | Text 36 | Đối chiếu Plugin Trace |

History chỉ append qua logic server; app không có nút New/Edit/Delete history.
Ẩn nút không phải security: chặn thao tác trực tiếp bằng quyền và plugin guard.
History là lịch sử nghiệp vụ của ứng dụng, không tuyên bố chống sửa bởi System
Administrator hoặc thay thế Dataverse auditing cho mục đích tuân thủ.

Quan hệ xóa Equipment → Work Order và Work Order → History dùng Restrict,
không cascade xóa lịch sử. Thiết kế quyền đọc/owner history phải theo phiếu cha;
không giả định lookup tự kế thừa quyền.

## 6. Các plugin và cách chia trách nhiệm

| Class dự kiến | Message / Stage / Mode | Việc làm |
| --- | --- | --- |
| `InitializeWorkOrder` | Create / PreOperation 20 / Sync | Validate dữ liệu đầu vào, bắt đầu Draft, điền hạn và counter |
| `ValidateWorkOrderUpdate` | Update / PreValidation 10 / Sync | Ghép Target + Pre Image, kiểm tra field policy, quyền, transition |
| `ApplyWorkOrderTransition` | Update / PreOperation 20 / Sync | Kiểm tra lại invariant dựa trên image, điền timestamp/counter vào Target |
| `AppendWorkOrderHistory` | Create, Update / PostOperation 40 / Sync | Tạo một history cho Create hoặc transition thật |
| `ProtectWorkOrderHistory` | Create, Update, Delete / PreValidation 10 / Sync | Chặn sửa/xóa; chỉ nhận Create từ luồng server hợp lệ |
| `ProtectWorkOrderDelete` | Delete / PreValidation 10 / Sync | Bắt người dùng Cancel thay vì xóa phiếu |

Không thêm các class này vào DLL cho đến bước implementation. Có thể giữ cùng
assembly hiện tại nhưng namespace con `FMCentralBms.Plugins.Maintenance`.

### Image và filtering

- Update Pre Image alias `Before`: stage, Equipment, Technician, Priority,
  Due At, Description, Resolution, các timestamp và Reopen Count cần kiểm tra.
- Post Image alias `After` trên step history: các cột dùng làm snapshot sau lưu.
- Create history chỉ có Post Image; Create không có Pre Image.
- Filter của các Update step bao gồm **tất cả field cần bảo vệ**: stage,
  Equipment, Technician, Priority, Due At, Name, Description, Resolution,
  Transition Reason, timestamp, counter. Không chỉ filter stage.
- Khi bổ sung một field mới vào policy phải cập nhật filter và image tương ứng.
- PreOperation sửa `Target`, không gọi `service.Update` trên chính phiếu.
- Append history không Update phiếu cha; tránh vòng lặp.

Policy tách thành class thuần `WorkOrderPolicy` để test từng transition mà không
cần kết nối Dataverse. Giá trị Choice đặt trong constants dùng chung với
provisioner; không rải số nguyên trong từng plugin.

## 7. Luồng xử lý Complete

1. Người dùng nhập Resolution, chọn Completed rồi Save (bản đầu), hoặc bấm
   nút Complete và xác nhận (bản nâng cao).
2. Plugin đọc Stage cũ và Technician từ `Before`, ghép field mới từ Target.
3. Server kiểm tra người thao tác và transition. Resolution chỉ có khoảng trắng
   hoặc dưới 20 ký tự thì trả `BMS-WO-003`.
4. PreOperation đặt `Completed At = UTC now`. Caller không được tự chọn timestamp.
5. Dataverse lưu phiếu.
6. PostOperation synchronous tạo history với actor, stage cũ/mới và kết quả.
7. Nếu ghi history thất bại, exception đi ra ngoài và thao tác lưu bị rollback.
8. Sau thành công, UI refresh phiếu và subgrid History.

Reason cần cho history dù đã xóa khỏi Target: PreOperation truyền nội dung qua
`SharedVariables` cho PostOperation trong cùng pipeline. Đây là dữ liệu nội bộ
giữa các step, không nhận actor/timestamp hoặc cờ tin cậy từ field do client gửi.

Đặc tính transaction của PostOperation synchronous giúp tránh tình trạng phiếu
đã Completed nhưng mất lịch sử. Không bắt exception ghi history rồi bỏ qua.
Không gửi email/Teams trực tiếp trong transaction này.
[Microsoft: database transactions](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/scalable-customization-design/database-transactions).

## 8. Những tình huống làm bài này đáng triển khai

### 8.1. Hai người bấm cùng lúc

Bản đầu phục vụ học UI Save và pipeline, chưa tuyên bố giải quyết mọi xung đột.
Hai request cùng đọc In Progress không được coi là an toàn chỉ vì có Pre Image.

Bản nâng cao dùng Custom API `fmc_TransitionWorkOrder` dạng Action bound vào
Work Order. Input: Action, Reason, Resolution khi cần, ExpectedRowVersion,
RequestId. Với Assign bổ sung TechnicianId. Output: Stage, RowVersion,
HistoryId, Replayed.

- UI gửi row version đã đọc; không tự refresh version rồi ghi đè ý định cũ.
- Handler gọi `UpdateRequest` với `ConcurrencyBehavior.IfRowVersionMatches`.
- Request thua nhận `BMS-WO-409`, UI yêu cầu tải lại và xem trạng thái mới.
- Các field transition chỉ ghi được qua đường lệnh này trong bản nâng cao;
  Update guard chặn request trực tiếp thay stage/timestamp/counter.
- Vẫn giữ policy bên dưới để mọi caller không đi vòng qua quy tắc nghiệp vụ.
- Tiêu chí riêng: xác minh `context.IsInTransaction` và rollback xuyên chuỗi
  API → Update → History trong môi trường thực; không suy từ tên Action.

[Microsoft: optimistic concurrency](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/optimistic-concurrency).

### 8.2. Người dùng bấm lại sau khi mất mạng

Bổ sung table `fmc_workordercommand` ở bản nâng cao: RequestId (Text GUID,
alternate key), WorkOrder, Actor, PayloadHash, ResultStage, HistoryId.

- Một ý định thao tác có một RequestId; retry phải dùng lại ID đó.
- Server kiểm tra actor, phiếu và hash canonical của payload trước khi replay.
- Cùng ID/cùng nội dung đã hoàn tất: trả kết quả cũ, không tạo history lần hai.
- Cùng ID/khác nội dung: từ chối.
- Command receipt, Update và History phải commit/rollback cùng nhau.
- Nếu hai request cùng ID đụng unique key, để request thua rollback; retry từ
  client đọc receipt đã commit. Không catch lỗi transaction rồi tiếp tục ghi.
- Retention receipt phải dài hơn cửa sổ retry đã thống nhất; xóa receipt sớm
  làm mất khả năng nhận biết retry cũ.

Không dùng mô hình “query không thấy → create” làm bảo đảm chống trùng.
Alternate key cần Active trước khi bật command API.

### 8.3. Thông báo và quá hạn

Để sau bản lõi. Flow chạy theo lịch tìm phiếu quá hạn hoặc xử lý outbox sau commit.
Một cột tính tuổi không tự phát sinh Update khi đồng hồ chạy qua hạn; plugin không
phải bộ lập lịch. Chưa cấu hình người nhận hoặc gửi thông báo trong plan này.

## 9. Quyền và bảo vệ phía server

| Vai trò đề xuất | Khả năng |
| --- | --- |
| Requestor | Tạo/xem phiếu trong phạm vi được cấp, sửa Draft theo policy |
| Dispatcher/Supervisor | Giao việc, đổi người ở Assigned, Cancel/Reopen |
| Technician | Start/Complete phiếu được giao, nhập kết quả |

Lookup Technician không tự cấp quyền đọc/ghi phiếu. Khi triển khai phải chọn
cơ chế owner team/access team hoặc sharing phù hợp; test bằng tài khoản thật
có quyền tối thiểu, không chỉ bằng tài khoản admin.

Caller được xác định từ context; actor lịch sử lấy `InitiatingUserId`. Phân biệt
`UserId` khi có impersonation. Không cho client truyền actor đáng tin cậy.

History Create cần quyền phù hợp. Phương án ưu tiên cho POC: cùng calling user
có quyền Create history cần thiết; guard Create chỉ chấp nhận lời gọi server
đã xác minh originating step/API và liên kết phiếu. Update/Delete bị chặn.
Không dựa riêng vào `Depth > 1` hoặc một tên message bất kỳ làm bằng chứng tin cậy.
Guard phải có test Create history trực tiếp để chứng minh không vượt qua được.

Với Custom API, `ExecutePrivilegeName` chỉ tham chiếu privilege có thật trong
environment; không tự đặt tên privilege mới rồi giả định Dataverse hiểu.
Kiểm tra record access và quyền nghiệp vụ vẫn cần trong handler.
[Microsoft: Custom API](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api).

## 10. Mã lỗi dự kiến

| Code | Thông báo / hoàn cảnh |
| --- | --- |
| `BMS-WO-001` | Không được chuyển từ trạng thái hiện tại sang trạng thái yêu cầu |
| `BMS-WO-002` | Cần chọn người xử lý và hạn hợp lệ trước khi giao việc |
| `BMS-WO-003` | Cần nhập kết quả xử lý tối thiểu 20 ký tự để hoàn tất |
| `BMS-WO-004` | Bạn không được thực hiện thao tác này trên phiếu |
| `BMS-WO-005` | Không được đổi Equipment sau khi giao việc |
| `BMS-WO-006` | Cần lý do tối thiểu 10 ký tự để hủy/mở lại |
| `BMS-WO-007` | Không được sửa field do hệ thống quản lý hoặc phiếu đã kết thúc |
| `BMS-WO-409` | Phiếu đã thay đổi; tải lại trước khi thao tác |
| `BMS-WO-CONFIG-001` | Thiếu image/cấu hình server; cần người quản trị kiểm tra |

UI hiển thị lỗi nghiệp vụ; trace ghi correlation ID và mã bước. Không trả stack
trace hoặc thông tin nhạy cảm của record không thuộc quyền đọc ra UI.

## 11. Plan thực hiện theo từng mốc

### Mốc 1 — Table và form

1. Kiểm tra live environment, publisher và logical names chưa bị dùng.
2. Thêm metadata vào partial `DataverseProvisioner.Maintenance.cs` cùng command
   provisioning riêng; chỉ tạo schema khi gọi command đó.
3. Tạo hai standard table, Choice constants, lookup và deletion behavior.
4. Tạo main form, views và subgrid History; thêm vào app `FMC BMS Demo`.
5. Chuẩn bị danh sách quyền theo vai trò. Chưa cấp thêm role cho ai nếu chưa xác
   định principal và quyền được chấp thuận.
6. Export metadata vào thư mục mới và giữ diff liên quan trong source solution.

**Đầu ra:** tạo được Draft trên UI, xem được Equipment và lịch sử rỗng.

### Mốc 2 — Rule và tính thời gian

1. Viết `WorkOrderPolicy` theo ma trận ở mục 4.
2. Viết Initialize, Validate Update và Apply Transition.
3. Đăng ký steps/images/filters như mục 6, kiểm tra cấu hình thiếu image.
4. Kiểm thử các giá trị rỗng, khoảng trắng, field omitted và explicit null.
5. Kiểm tra caller không tự tạo phiếu Completed hoặc tự điền timestamp/counter.

**Đầu ra:** UI chặn transition sai, tự ghi mốc giờ khi transition đúng.

### Mốc 3 — History và transaction

1. Viết Append History, Protect History và Protect Delete.
2. Chụp reason/resolution đúng từng lần, actor từ context server.
3. Test chỉ sửa Description không tạo thêm transition history.
4. Dùng fixture integration có kiểm soát để làm history write thất bại và xác
   minh phiếu rollback; không phá role đang dùng để mô phỏng lỗi.

**Đầu ra:** mỗi transition có một history, Complete thất bại không để lại nửa dữ liệu.

### Mốc 4 — UI demo và security

1. Hoàn thiện views, nhãn, hiển thị field theo stage và lỗi dễ hiểu.
2. Test người tạo, kỹ thuật viên được giao và người không được giao.
3. Kiểm tra quyền đọc lịch sử theo phiếu; không lộ lịch sử chỉ vì subgrid có lookup.
4. Chạy kịch bản demo mục 12, ghi lại kết quả thực tế.

**Đầu ra:** bản học hoàn chỉnh qua UI Save, có giới hạn concurrency được ghi rõ.

### Mốc 5 — Command API và chống xung đột

1. Tạo Custom API và plugin handler riêng; main handler gắn bằng Plugin Type
   của Custom API, không đăng ký nó như Update step thông thường.
2. Thêm row-version checking và command receipt idempotency.
3. Thêm nút app gọi API, refresh sau success, giữ RequestId khi retry.
4. Khóa đường Update trực tiếp cho transition fields, test bypass.
5. Test hai phiên người dùng và tình huống mất response.

**Đầu ra:** bản nâng cao có bằng chứng concurrency/retry; không chỉ thêm nút cho đẹp.

## 12. Kịch bản demo 5 phút

1. Tạo phiếu `Kiểm tra cảm biến phòng kỹ thuật`, chọn Equipment, Priority High.
   Save → Draft, thấy số WO và hạn do plugin điền.
2. Thử chuyển Draft → Completed → lỗi `BMS-WO-001`.
3. Chọn Technician, chuyển Assigned → thành công, thấy Assigned At và history.
4. Chuyển In Progress bằng người có quyền → Started At và history mới.
5. Nhập Resolution `OK`, chuyển Completed → lỗi `BMS-WO-003`.
6. Nhập `Đã kiểm tra đầu nối, hiệu chỉnh và xác nhận thiết bị hoạt động ổn định.`
   rồi Complete → thành công, thấy timestamp và snapshot kết quả.
7. Supervisor Reopen với lý do → tăng counter, history giữ kết quả cũ.
8. Khi đã làm mốc 5: mở cùng phiếu ở hai tab, transition ở tab A trước; tab B
   dùng version cũ bị báo cần tải lại.

## 13. Điều kiện nghiệm thu

| Nhóm | Bằng chứng cần có |
| --- | --- |
| Quy tắc | Mọi đường hợp lệ/không hợp lệ trong state matrix có test |
| Partial Update | Omitted field giữ giá trị cũ; explicit null được kiểm tra đúng |
| Dữ liệu liên quan | Equipment tồn tại, caller có quyền; không thay identity catalog |
| Security | User không được giao không Complete; non-Supervisor không Reopen |
| History | Một transition → một history; edit thường → không thêm transition |
| Atomicity | History lỗi → phiếu không thay stage hoặc timestamp |
| Chống giả mạo | Ghi thẳng stage/system field/history bị từ chối theo policy |
| Concurrency, mốc 5 | Hai version cạnh tranh: chỉ một thành công, không mất cập nhật |
| Retry, mốc 5 | Cùng RequestId được replay, không nhân đôi history |
| Packaging | Tables, forms, views, steps, images, API và app cùng solution |
| Hiệu năng | Đo thời gian plugin/request trên dữ liệu demo; chưa đặt SLA production |

Thời gian triển khai phụ thuộc quyền, tooling và mức hoàn thiện UI. Làm lần lượt
theo các mốc có đầu ra kiểm chứng được thay vì coi toàn bộ plan là một bài copy DLL.

## 14. Triển khai, rollback và giới hạn

Provision metadata → build DLL → register types/steps/images → validate app →
publish → test → export/unpack. Kiểm tra target và giữ export trước/sau.
Không dùng helper `--register-plugin` hiện có cho các class mới: helper đó chỉ
hỗ trợ rule Equipment → Building, cần extension riêng khi implementation.

Rollback cần đi cùng UI và quyền ghi: tạm khóa thao tác Work Order của app rồi
phục hồi phiên bản plugin tương thích. Không chỉ disable validator để người dùng
tiếp tục ghi dữ liệu không hợp lệ. Giữ tables/records/history để điều tra;
không xóa dữ liệu mới để “quay lại như cũ”.

Khi mốc 5 thay đổi đường ghi, phải chuyển UI/handler/guards theo thứ tự triển khai
có kiểm soát. Nếu bản cũ không hiểu receipt hoặc schema mới, không hạ DLL đơn lẻ.

Đây là plan sản phẩm và implementation, chưa phải source C# có thể copy build.
Trước khi triển khai, cần hiện thực policy và guard, chốt quyền với principal
thật, kiểm tra metadata/API support rồi ghi receipt nghiệm thu. Plan không tự
tạo bảng, app, role, Custom API hoặc gửi thông báo.

Tham chiếu baseline: [Dataverse deployment](../reference/dataverse-deployment.md),
[app hiện tại](../runbooks/bms-demo-app.vi.md),
[bài plugin đầu tiên](../runbooks/dataverse-plugin-step-by-step.vi.md).
