export type Block = { title: string; text?: string; items?: string[]; tone?: 'warning' | 'tip'; table?: string[][]; code?: string };
export type Lesson = { id: string; title: string; subtitle: string; tag: string; goal: string; blocks: Block[]; samples?: string[]; checkpoint: string };
export const lessons: Lesson[] = [
  {
    id: 'overview', title: 'Chọn một tính năng có ích', subtitle: 'Point Issue Tracker · từ nhu cầu đến CRUD', tag: 'BẮT ĐẦU',
    goal: 'Từ Current Point, ghi nhận một sự cố, xem lại, cập nhật và xóa bản nháp ngay trong FMC BMS Demo.',
    blocks: [
      { title: 'Bài tập bạn sẽ tự làm', text: 'Ví dụ: TEMP-001 có giá trị bất thường. Người vận hành chọn “Ghi nhận sự cố”, nhập “Nhiệt độ phòng cao”, lưu Draft, mở lại để bổ sung mô tả và chuyển Open. Nếu tạo nhầm, chỉ bản Draft được phép xóa qua API.', table: [
        ['CRUD', 'Người dùng làm gì?', 'Custom API'],
        ['Create', 'Tạo issue cho point đang chọn', 'fmc_CreatePointIssue'],
        ['Read', 'Mở chi tiết và phiên bản mới nhất', 'fmc_GetPointIssue'],
        ['Update', 'Sửa nội dung, ưu tiên, trạng thái', 'fmc_UpdatePointIssue'],
        ['Delete', 'Xóa bản nháp, có xác nhận', 'fmc_DeleteDraftPointIssue'],
      ] },
      { title: 'Phạm vi chính xác', tone: 'tip', text: 'Đây là tài liệu React và code mẫu để bạn tự implement. Chưa tạo table/API Point Issue trên Dataverse; chưa sửa app demo. Playground chỉ mô phỏng trong trình duyệt. Không có email, Flow hoặc SQL sync nào được gọi từ trang này.' },
      { title: 'Đi theo kiến trúc đang có', items: ['React component hiển thị form; hook quản lý state/loading/error; service giao tiếp Dataverse.', 'Custom API định nghĩa contract; IPlugin nhận input; PointIssueStore kiểm tra nghiệp vụ và gọi IOrganizationService.', 'Issue là dữ liệu nghiệp vụ Dataverse. SQL raw.bms_reading vẫn là lịch sử ingestion; không thêm issue vào SQL ledger hoặc thay worker.', 'Không tạo Code App mới. Chèn panel vào generative page React của model-driven app hiện tại.'] },
      { title: 'Vì sao dùng Custom API?', text: 'CRUD đơn giản có thể gọi Web API trực tiếp. Ở bài này, Custom API gom validation, rule Delete Draft và concurrency thành contract server dùng lại được. UI không phải nơi duy nhất quyết định dữ liệu hợp lệ.' },
    ], checkpoint: 'Bạn mô tả được hành trình tạo → đọc → sửa → xóa, và phân biệt docs local với app thật.',
  },
  {
    id: 'repo', title: 'Đọc đúng file, sửa đúng lớp', subtitle: 'Bản đồ source của app demo hiện tại', tag: 'NỀN TẢNG',
    goal: 'Xác định đâu là source cần sửa và đâu chỉ là artifact được sinh ra.',
    blocks: [
      { title: 'Các điểm neo trong repository', table: [
        ['Source hiện có', 'Dùng để tham khảo'],
        ['dataverse/app-source/fmc-bms-demo/src/Dashboard.tsx', 'Entry component, dataApi, bố cục Home'],
        ['…/src/services/spoChangeService.ts', 'Cách gọi Custom API từ Xrm host bằng fetch'],
        ['…/src/hooks/useDashboard.ts', 'Mẫu hook loading, refresh, cache'],
        ['Dataverse.Plugin/FMCentralBms.Plugins/Plugins/RequestBmsSync.cs', 'IPlugin → PluginServices → service'],
        ['Dataverse.SyncWorker/Dataverse.SyncWorker.DataAccess/Services/DataversePluginProvisioner.cs', 'Register assembly, plugin types, Custom API'],
        ['…/Services/DataverseProvisioner.cs', 'Schema provisioning phải được cập nhật theo metadata'],
      ] },
      { title: 'Prerequisite đang còn lệch trong source', tone: 'warning', text: 'dashboardService.ts còn query fmc_bmsreading, trong khi implementation hiện tại chuyển sang Standard fmc_bmsreadingsnapshot. Trước khi test panel trong Home, tự cập nhật query/primary ID, app-spec data sources, models và generate lại RuntimeTypes. Đừng tái tạo bảng elastic cũ để làm UI hết lỗi.', items: ['Thay query table bằng fmc_bmsreadingsnapshot và ID bằng fmc_bmsreadingsnapshotid.', 'Rà RuntimeTypes.ts, src/models/dashboard.ts và app-spec.json; regenerate types theo schema mới.', 'Đây là phát hiện khi đọc source, không phải kết quả kiểm tra live hôm nay. Tài liệu này không tự sửa các file đó.'] },
      { title: 'Quy tắc build của generative page', items: ['Thêm code dưới src/models, src/services, src/hooks, src/components; dùng named imports/exports.', 'build.cjs flatten source thành trung-tam-van-hanh.tsx. Không chỉnh file output để rồi bị mất thay đổi.', 'Tránh trùng tên type/helper giữa các module khi flatten. Các helper mẫu dùng prefix PointIssue.', 'Tạo table + khai báo data source trước khi generate RuntimeTypes cho queryTable. API fetch không tự thêm table vào app data sources.'] },
    ], checkpoint: 'Home tải được dữ liệu hiện tại; bạn biết 7 file mẫu sẽ nằm ở đâu.',
  },
  {
    id: 'schema', title: 'Tạo table Point Issue', subtitle: 'Standard table · lookup thật · choice rõ ràng', tag: 'DATAVERSE',
    goal: 'Tạo schema trong solution FMCentralBms, giữ tên và kiểu dữ liệu khớp C# và TypeScript.',
    blocks: [
      { title: 'Thao tác trên Maker portal', items: ['Mở make.powerapps.com, chọn đúng Developer environment của dự án. Đối chiếu org URL với profile trước khi ghi.', 'Solutions → FMCentralBms → New → Table. Display name: Point Issue; plural: Point Issues; logical name phải là fmc_pointissue.', 'Chọn Standard, ownership User or team. Primary name column: fmc_name, độ dài 200. Không tạo Elastic.', 'Tạo các column dưới đây. Lookup Point trỏ tới fmc_bmspoint, chọn Required; quan hệ không cascade-delete issue khi xóa point (dùng Restrict).', 'Xác minh IsOptimisticConcurrencyEnabled=true bằng metadata. Bật audit theo nhu cầu và chính sách môi trường, không bật rộng toàn org chỉ để học.', 'Lưu/publish schema. Thêm form + view cho Point Issues và đưa table vào app nếu cần mở từ sitemap.'] },
      { title: 'Schema đề xuất', table: [
        ['Logical name', 'Type', 'Quy tắc'],
        ['fmc_pointissueid', 'Primary GUID', 'Dataverse sinh ID'],
        ['fmc_name', 'Text · 200', 'Bắt buộc; Title ở API'],
        ['fmc_pointid', 'Lookup → fmc_bmspoint', 'Bắt buộc; PointId ở API'],
        ['fmc_description', 'Multiline text · 4000', 'Cho phép chuỗi rỗng'],
        ['fmc_priority', 'Choice', '100000000 Low / 100000001 Normal / 100000002 High'],
        ['fmc_workstatus', 'Choice', '100000000 Draft / 100000001 Open / 100000002 Resolved'],
        ['versionnumber', 'System BigInt', 'SDK trả Entity.RowVersion; không tự tạo column'],
      ] },
      { title: 'Choice value là một phần contract', tone: 'warning', text: 'Các số 100000000–100000002 ở đây là giá trị đề xuất cho local Choices. Nếu Maker sinh giá trị khác, phải cập nhật đồng thời schema, C#, React và test. Không đổi statecode/statuscode để thay thế fmc_workstatus.' },
      { title: 'Giữ schema có thể dựng lại', text: 'Sau khi học thao tác Maker, đưa định nghĩa tương đương vào partial DataverseProvisioner.PointIssues.cs cạnh DataverseProvisioner.cs: Ensure Standard table, text columns, choice values, lookup + Restrict relationship. Đăng ký lời gọi trong luồng provisioning hiện có. Chỉ chạy khi đã review phạm vi vì --provision hiện tại không phải lệnh riêng cho Point Issue.' },
    ], checkpoint: 'Metadata có đúng table, column, lookup, Choices và optimistic concurrency; không thay schema reading/point cũ.',
  },
  {
    id: 'contract', title: 'Thiết kế 4 Custom API', subtitle: 'Input / output là hợp đồng, không phải chi tiết UI', tag: 'CONTRACT',
    goal: 'Định nghĩa API trước khi copy code. Tất cả là Global (unbound).',
    blocks: [
      { title: 'Bảng contract để tạo request/response properties', table: [
        ['Unique name / HTTP', 'Input (tên : type)', 'Output (tên : type)'],
        ['fmc_CreatePointIssue · POST', 'PointId: Guid; Title: String; Description: String; Priority: Integer', 'IssueId: Guid'],
        ['fmc_GetPointIssue · GET', 'IssueId: Guid', 'IssueId: Guid; PointId: Guid; Title: String; Description: String; Priority: Integer; WorkStatus: Integer; RowVersion: String'],
        ['fmc_UpdatePointIssue · POST', 'IssueId: Guid; Title: String; Description: String; Priority: Integer; WorkStatus: Integer; RowVersion: String', 'Updated: Boolean'],
        ['fmc_DeleteDraftPointIssue · POST', 'IssueId: Guid; RowVersion: String', 'Deleted: Boolean'],
      ] },
      { title: 'Quy ước request', items: ['Is Optional = No cho toàn bộ input trong bài. Description vẫn được gửi, giá trị có thể là "".', 'Không thêm prefix fmc_ vào tên input Title/IssueId. Phân biệt hoa thường đúng như code.', 'WorkStatus lúc Create do server ép Draft. PointId bất biến khi Update.', 'RowVersion lấy từ Get, gửi lại khi Update/Delete. Sau Save phải Get lại trước thao tác tiếp theo.'] },
      { title: 'Action và Function', text: 'C/U/D: Is Function = No (POST). Get: Is Function = Yes (GET), có response properties và Enabled for Workflow = No. Để bài tập đơn giản, cả bốn API để Enabled for Workflow = No; chưa tích hợp Power Automate. Đừng đổi Action ↔ Function sau khi tạo; đó là thuộc tính immutable.' },
      { title: 'Đường đi một request', code: 'PointIssuePanel → usePointIssue → pointIssueService\n  → POST /api/data/v9.2/fmc_CreatePointIssue\n  → PointIssueApi.Execute → PointIssueStore.Create\n  → IOrganizationService (caller) → fmc_pointissue\n  ← IssueId → GET chi tiết → UI cập nhật' },
    ], checkpoint: 'Đối chiếu được mỗi InputParameters/OutputParameters trong plugin với một property của API.',
  },
  {
    id: 'business', title: 'Viết C# service nghiệp vụ', subtitle: 'Validation, CRUD và chống ghi đè', tag: 'BACKEND · 1',
    goal: 'Tạo PointIssueStore.cs. Dữ liệu sai bị chặn tại server, không phụ thuộc button có disable hay không.',
    blocks: [
      { title: 'Làm theo thứ tự này', items: ['Tạo file tại Services/PointIssueStore.cs trong project FMCentralBms.Plugins.', 'Đọc Create và Read trước: Entity là row Dataverse; EntityReference là lookup, không phải text.', 'Thêm Update chỉ ghi các field cho phép. Không nhận arbitrary JSON rồi copy toàn bộ attributes.', 'Thêm DeleteDraft. Tách expected RowVersion khỏi nội dung form và kiểm tra tại SDK request.', 'Build plugin trước khi sang UI. Giữ signing key hiện có trên máy; không tạo key mới để update cùng assembly.'] },
      { title: 'Rule đang bảo vệ phạm vi nào?', tone: 'warning', text: 'Store bảo vệ lời gọi đi qua Custom API. Người có table Write/Delete vẫn có thể gọi CRUD chuẩn để bypass rule. Trước khi coi Delete Draft là chính sách toàn hệ thống, bổ sung plugin Delete đồng bộ (PreOperation + Pre Image chứa fmc_workstatus), và guard Create/Update cho validation/state transition. Test cả direct Web API. Ẩn nút hoặc IsPrivate không phải bảo mật.' },
      { title: 'Concurrency', text: 'Mở cùng một issue ở hai tab: A lưu trước, B lưu bằng RowVersion cũ phải lỗi. Compare trong C# giúp báo rõ; IfRowVersionMatches mới chặn được race giữa bước đọc và ghi. Không bật silent overwrite khi gặp lỗi này.' },
    ], samples: ['store'], checkpoint: 'Service build được và test được title rỗng, priority sai, thiếu point, version cũ, Delete Open bị chặn.',
  },
  {
    id: 'plugin', title: 'Nối Custom API vào plugin', subtitle: 'Handler mỏng · dùng quyền caller · trace có ý nghĩa', tag: 'BACKEND · 2',
    goal: 'Tạo PointIssueApi.cs để chuyển contract API thành lời gọi business service.',
    blocks: [
      { title: 'Không thêm một ASP.NET endpoint', text: 'API này chạy trong Dataverse qua plugin assembly net48, không nằm trong worker port 5300. Worker có thể dừng mà API vẫn chạy, miễn plugin và metadata được đăng ký đúng.' },
      { title: 'Tái sử dụng framework hiện có', items: ['PluginServices.ResolveWithFactory lấy context, tracing và factory.', 'PluginServices.CreateOrgService tạo service dưới context.UserId. Không impersonate SYSTEM.', 'Switch theo context.MessageName để một Plugin Type phục vụ bốn API.', 'Main operation của Custom API trỏ tới Plugin Type. Không đăng ký thêm một processing step cho chính main operation này.'] },
    ], samples: ['plugin'], checkpoint: 'Build assembly thành công; plugin class là public và implement IPlugin; tên message khớp contract.',
  },
  {
    id: 'register', title: 'Đăng ký và cấp quyền', subtitle: 'Làm trong solution, kiểm tra metadata rồi mới gọi', tag: 'DEPLOY · BACKEND',
    goal: 'Bạn thực hiện bước này sau khi code đã build. Trang docs không thực hiện cloud mutation.',
    blocks: [
      { title: '01 · Register assembly / Plugin Type', items: ['Dùng Plugin Registration Tool (PRT) với đúng Developer org. Update assembly FMCentralBms.Plugins hiện có bằng DLL Release và signing key gốc; giữ Sandbox / Database.', 'Xác minh type FMCentralBms.Plugins.PointIssueApi xuất hiện. Nếu dùng provisioner thay PRT, thêm EnsurePluginType cho class mới vào Register; hiện tại CLI chưa biết type mới này.', 'Không đổi assembly identity, không xóa assembly cũ để “update”, không đụng các step sync/notification đã có.'] },
      { title: '02 · Tạo từng API trong Maker solution', items: ['Trong FMCentralBms → New → More → Other → Custom API (nhãn menu có thể khác theo UI). Nhập Unique Name theo bài 04; Binding Type = Global.', 'Plugin Type = FMCentralBms.Plugins.PointIssueApi; Is Private = No trong môi trường học; Allowed Custom Processing Step Type = None.', 'Chọn Is Function theo contract; Enabled for Workflow = No. Nhập Execute Privilege Name tương ứng quyền Create/Read/Write/Delete của fmc_pointissue, lấy tên từ metadata privilege đã được tạo, không tự đặt một privilege tùy ý.', 'Tạo Custom API Request Parameter và Custom API Response Property trong solution. Gắn đúng Custom API, Unique Name, type và Is Optional = No cho request.', 'Save/publish. Kiểm tra API trong /api/data/v9.2/$metadata, gồm tên input/output và type Guid/String/Int32/Boolean.'] },
      { title: '03 · Provisioner phải phản ánh metadata', tone: 'warning', text: 'EnsureUnboundCustomApi hiện có hard-code isfunction=false, workflowsdkstepenabled=true và Async Only. Không dùng nguyên helper đó cho GetPointIssue. Thêm helper riêng hoặc tham số contract, giữ nguyên behavior API cũ. Guard immutable attributes; không tự delete/recreate API khi khác contract. EnsureApiProperty hỗ trợ property, nhưng cần đối chiếu đúng type code trước khi dùng.' },
      { title: '04 · Security role', table: [
        ['Persona', 'Quyền cần có'],
        ['Viewer', 'Read fmc_pointissue và Read point trong scope được giao'],
        ['Operator', 'Create/Read/Write/Delete fmc_pointissue trong scope; Append issue + Append To point; Read point'],
        ['Provisioning identity', 'Quyền customization/deployment, tách biệt runtime user'],
      ] },
      { title: '05 · Đóng gói', text: 'Bảo đảm table, columns, relationship, form/view, plugin assembly/type, 4 Custom APIs + properties và role thay đổi đều thuộc FMCentralBms. Export/unpack bằng quy trình repo và review diff. Deployment receipt là bằng chứng một lần chạy, không thay thế kiểm tra live sau đó.' },
    ], checkpoint: 'Cả 4 API xuất hiện đúng metadata, có PluginTypeId, properties và security scope; Viewer không gọi được Create.',
  },
  {
    id: 'react', title: 'Viết React và đặt vào Home', subtitle: 'Model → service → hook → component → Dashboard', tag: 'FRONTEND',
    goal: 'Tự tạo năm file React mẫu rồi chèn panel vào source Home hiện tại.',
    blocks: [
      { title: 'Thứ tự code dễ debug nhất', items: ['models/pointIssue.ts: type dữ liệu và Choice constants.', 'services/pointIssueService.ts: chỉ biết HTTP contract, không setState.', 'hooks/usePointIssue.ts: loading, chống double click, read-after-write, thông báo lỗi.', 'components/PointIssuePanel.tsx: form tối thiểu đủ 4 thao tác; sau đó dùng Fluent UI để đồng bộ app.', 'Dashboard.tsx: từ row point đã chọn, truyền fmc_bmspointid vào panel. Không lấy EquipmentCode làm GUID.'] },
      { title: 'Read list khác Read detail', text: 'Form mẫu nhập Issue ID để test Get độc lập. Khi hoàn thiện, thêm list vào panel: dataApi.queryTable lấy một trang issue, click row gọi GetPointIssue để nhận đầy đủ RowVersion. Thêm fmc_pointissue vào app data sources và regenerate RuntimeTypes trước. Implement paging/filter đúng host; không tải mọi record về browser.' },
      { title: 'Tách lỗi mutation và lỗi refresh', tone: 'tip', text: 'Nếu Create đã thành công nhưng Get sau đó lỗi, UI vẫn phải báo “Đã tạo” và giữ IssueId. Không đề nghị bấm Tạo lại một cách mù quáng. Với timeout chưa rõ kết quả, kiểm tra issue đã tồn tại trước khi retry.' },
    ], samples: ['model', 'api', 'hook', 'component', 'integrate'], checkpoint: 'Có thể chọn point trên Home, tạo Draft, đọc lại, sửa, xóa Draft; lỗi server được hiển thị.',
  },
  {
    id: 'release', title: 'Build, publish và giữ đường lui', subtitle: 'Local build không đồng nghĩa đã có trên Dataverse', tag: 'DEPLOY · UI',
    goal: 'Tự triển khai có kiểm soát; không chạy startup mặc định của worker chỉ để học Custom API.',
    blocks: [
      { title: 'Build local trước', code: 'cd D:\\metasys-poc\ndotnet build MetasysPoc.sln -c Release\n\ncd dataverse/app-source/fmc-bms-demo\nnpm ci\nnpm run build\nnpm test', text: 'Test hiện có không tự kiểm chứng Point Issue mới. Thêm test service/plugin và test UI riêng theo bài 10. Nếu thiếu signing key, dùng hướng dẫn home-dataverse-plugin-sync; tuyệt đối không đưa key/token vào docs.' },
      { title: 'Publish theo thứ tự phụ thuộc', items: ['1. Review target org và backup/export solution trước thay đổi. Không dùng profile production.', '2. Provision schema và metadata như bài 03; update plugin assembly/type.', '3. Đăng ký APIs/properties/roles như bài 07; gọi Get thử trước từ Dataverse host.', '4. Cập nhật data sources và generate RuntimeTypes khi schema thay đổi; chạy build/test React.', '5. Upload generative page vào đúng app/page ID đã kiểm tra; refresh app và test dưới Operator/Viewer.', '6. Export/unpack FMCentralBms; review source diff và ghi receipt các test.'] },
      { title: 'Đọc help đúng CLI đang cài', code: 'pac auth list\npac model genpage list --help\npac model genpage generate-types --help\npac model genpage upload --help', text: 'CLI local được kiểm tra là PAC 2.11.2. Dùng genpage list để tìm đúng app/page; giữ toàn bộ data sources cũ khi thêm table mới. Không copy đường dẫn D:/Coder từ receipt cũ. Upload là thao tác publish thật, không chạy từ trang docs.' },
      { title: 'Giới hạn command hiện có', tone: 'warning', text: 'Worker --register-plugin chỉ đăng ký những type/API đã được khai báo trong provisioner. Thêm hai file C# rồi chạy lệnh này chưa đủ nếu chưa nối registration. Không có command --provision-point-issue sẵn trong repo. Dùng PRT + Maker cho bài đầu, rồi codify lại để deployment lặp lại được.' },
      { title: 'Rollback', text: 'Giữ version page/assembly trước thay đổi. Nếu UI mới lỗi, rollback page và ẩn entry Point Issue trước; không xóa table đã có dữ liệu. Nếu thay đổi API contract, dùng version mới thay vì sửa phá client cũ. Không chạm bảng reading, raw SQL hay delivery ledger.' },
    ], checkpoint: 'Có bằng chứng build, metadata, permission và UI live; source export khớp thay đổi thực tế.',
  },
  {
    id: 'test', title: 'Test đủ, không chỉ happy path', subtitle: 'Checklist nghiệm thu và cách tìm lỗi', tag: 'KIỂM CHỨNG',
    goal: 'Chứng minh CRUD chạy đúng, đồng thời chỉ rõ giới hạn của bài tập.',
    blocks: [
      { title: 'Test matrix', table: [
        ['Ca kiểm thử', 'Kết quả mong đợi'],
        ['Create point hợp lệ + title', '1 row Draft; lookup đúng point; trả IssueId'],
        ['Title rỗng / quá 200 / Choice lạ', 'Server reject, không tạo row'],
        ['Get ID có thật / ID không tồn tại', 'Output đủ + RowVersion / báo not found'],
        ['Update sau Get', 'Đúng nội dung, cùng IssueId, RowVersion mới'],
        ['Hai tab sửa cùng phiên bản', 'Tab lưu sau bị conflict, phải refresh'],
        ['Delete Draft / Delete Open', 'Draft xóa / Open bị chặn qua API'],
        ['Viewer gọi Create từ console', 'Access denied, không chỉ ẩn button'],
        ['Direct standard CRUD bypass', 'Ghi nhận giới hạn; thêm table guard trước production'],
        ['Mutation thành công nhưng refresh lỗi', 'Báo đã ghi + ID, không tự retry Create'],
      ] },
      { title: 'Troubleshooting', table: [
        ['Triệu chứng', 'Kiểm tra trước'],
        ['404 tên API', 'Unique Name, publish, $metadata; Function phải có output'],
        ['400 invalid parameter', 'Tên/type, Guid format, GET vs POST, input bắt buộc'],
        ['200 nhưng output rỗng', 'Plugin Type đã gắn chưa? DLL có đúng class không?'],
        ['403 / privilege missing', 'ExecutePrivilegeName, role scope, Append/Append To lookup'],
        ['Open inside Dataverse app', 'Đang chạy localhost thay vì host có Xrm'],
        ['Concurrency lỗi ngay lần đầu', 'Metadata optimistic concurrency; RowVersion từ Get'],
        ['Home lỗi bảng reading', 'Prerequisite bài 02: source còn tên table cũ'],
      ] },
      { title: 'Trước khi đưa ra ngoài demo', items: ['Server guard cho direct CRUD; state machine rõ ràng nếu không cho quay về Draft.', 'Idempotency Create bằng ClientRequestId + alternate key; policy retry/throttle, không duplicate khi timeout.', 'List/filter/paging, quyền theo owner/team, confirmation và bản dịch Fluent UI đầy đủ.', 'Automated unit tests, integration tests trên environment test, ALM và rollback đã thực hành.', 'Chỉ gọi “đã deploy” sau khi có bằng chứng live. Playground không kiểm chứng SDK, role hoặc metadata thật.'] },
    ], checkpoint: 'Happy path, permission và concurrency đều có kết quả; mọi giới hạn còn lại được ghi rõ.',
  },
];

export const sources = [
  ['Custom API: Action / Function / Plugin Type', 'https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api'],
  ['Tạo Custom API bằng Maker portal', 'https://learn.microsoft.com/en-us/power-apps/developer/data-platform/create-custom-api-maker-portal'],
  ['Tạo Custom API bằng code', 'https://learn.microsoft.com/en-us/power-apps/developer/data-platform/create-custom-api-with-code'],
  ['Optimistic concurrency', 'https://learn.microsoft.com/en-us/power-apps/developer/data-platform/optimistic-concurrency'],
  ['Register plug-in', 'https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in'],
  ['Custom API tables / properties', 'https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api-tables'],
];
