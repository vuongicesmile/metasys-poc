# Change Log bằng Dataverse plugin

## Trạng thái

Source đã chuẩn bị cho environment Developer `org06cbc9ec.crm5.dynamics.com`,
solution `FMCentralBms`, app `fmc_FMCBMSDemo`. Chưa triển khai live: checkout thiếu
khóa ký gốc `FMCentralBms.Plugins.snk`. Không dùng DLL build với SignAssembly=false
để deploy; không tạo khóa thay thế cho assembly hiện có.

Kiểm tra read-only ngày 2026-09-22: PAC WhoAmI xác nhận organization
`ab191700-b99e-f111-aaa0-000d3a80bb96`; assembly live vẫn là `1.0.0.5`, token
`e122b5e4dcc2589d`. Worker status chưa đọc được vì đường dẫn DeveloperTokenPython
trong WSL không tồn tại trên máy hiện tại. PAC auth và worker auth là hai đường
riêng; cần sửa đường dẫn helper hợp lệ hoặc cấu hình runtime identity trước deploy.
Chưa tạo bảng, đăng ký steps, đổi roles, publish app hoặc export cloud ở lần này.

Kiểm tra tiếp trên máy công ty: tìm khóa ký gốc cạnh project plugin hoặc tại
đường dẫn `FMC_PLUGIN_SIGNING_KEY`, rồi kiểm tra lại helper token bằng `--probe`
và chạy `--change-log-status` trước khi deploy. Không commit khóa vào Git.
Trên máy hiện tại, `pac auth token` trả token có audience
`https://api.powerplatform.com`, không dùng được cho Dataverse SDK (WhoAmI trả
unauthorized). Helper PAC thử nghiệm đã được bỏ; chưa thay đổi cấu hình auth.

## Hành vi

`RecordChangeLog` chạy synchronous PostOperation Create/Update/Delete trên
`fmc_bmsbuilding` và `fmc_bmsequipment`. Áp dụng cả thao tác UI và API/worker;
người khởi tạo là InitiatingUserId, execution identity là UserId. Không đoán rằng
mọi request đều là người dùng tương tác.

Log chỉ ghi thay đổi thành công trong cùng transaction. Lỗi ghi log làm thao tác
nguồn rollback; thao tác bị chặn trước đó không sinh log. Không log Read, các bảng
khác, cascade/system operations không phát Create/Update/Delete, hoặc thay đổi
ngoài danh sách cột. Không log Update gửi lại nguyên giá trị.

Các cột theo dõi:

- Building: Name, Building Code, Source Building, Description, State, Status.
- Equipment: Name, Equipment Code, Equipment Type, Building, Description, State, Status.

`Before` image dùng cho Update/Delete; `After` dùng cho Create/Update. Không dùng
All Columns. Log giữ table name + record GUID dạng text, không lookup tới record
nguồn nên xóa record không cascade xóa log. Before/After là JSON chỉ gồm cột trong
allowlist, giá trị canonical dạng string/null; lookup gồm table và GUID, Choice
là số. Changed Fields liệt kê tên logical của cột thực sự thay đổi.

`fmc_changelog` là standard organization-owned table. Form readonly hiển thị người
thay đổi, UTC timestamp (UI hiển thị theo timezone), operation, record, correlation,
before/after. App có mục Change Logs. Role Operator và Integration được thêm Read,
Create trên bảng mới; Viewer chỉ Read. Không thêm Write/Delete, không gán thêm role
cho user. Các role khác thực hiện source writes cũng cần Create log; nếu thiếu,
source write bị chặn.

Đây là log nghiệp vụ, không phải kho audit chống giả mạo: user có quyền Create có
thể tự tạo log qua API và System Administrator vẫn có thể sửa/xóa. Để audit tuân thủ,
cần thiết kế riêng bằng Dataverse auditing và chính sách bảo vệ/retention.
Không tự xóa log hoặc cấp TTL trong bản này; cần theo dõi capacity khi dùng lâu dài.

## Triển khai khi có khóa gốc

Từ root repo, dùng đường dẫn khóa gốc ngoài Git:

```powershell
dotnet build Dataverse.Plugin/FMCentralBms.Plugins -c Release -p:AssemblyOriginatorKeyFile="D:\secure\FMCentralBms.Plugins.snk"
dotnet run --project Dataverse.SyncWorker/Dataverse.SyncWorker.App --no-launch-profile -- --change-log-status
dotnet run --project Dataverse.SyncWorker/Dataverse.SyncWorker.App --no-launch-profile -- --deploy-change-log --plugin-path="D:\Coder\metasys-poc\Dataverse.Plugin\FMCentralBms.Plugins\bin\Release\net48\FMCentralBms.Plugins.dll"
```

Deploy kiểm tra version 1.0.0.7 và public key token `e122b5e4dcc2589d` trước cloud
write; WhoAmI được guard bằng organization ID. Command chỉ tạo cấu hình log, thêm
quyền của bảng mới và cập nhật app; không start worker hoặc chạy SQL sync.
Schema/UI được provision trước khi bật 6 steps. Nếu deployment lỗi giữa chừng,
kiểm tra trạng thái rồi retry cùng source; không xóa bảng để làm lại. Nếu cấp quyền
thất bại, sửa quyền triển khai theo authority hiện có trước khi bật steps.

Sau deploy, kiểm tra metadata/6 active steps/8 images, membership trong solution,
ValidateApp và kiểm thử bằng account có role Operator:

1. Tạo Building demo với code riêng: xuất hiện một log Create, Before=null.
2. Đổi Name: log Update chứa đúng giá trị trước/sau và người thực hiện.
3. Gửi lại Name cũ (không đổi giá trị): không có log mới.
4. Xóa Building demo chưa liên kết: log Delete còn tồn tại, After=null.
5. Lặp lại với Equipment demo có Building; xóa Equipment trước Building demo.
6. Thử thao tác nguồn bị validation chặn: không có log thành công giả.

Chỉ xóa record demo đã ghi lại ID; giữ các log demo để quan sát trong app.
Export/unpack FMCentralBms vào thư mục tạm, so sánh và đưa các thành phần liên quan
vào `dataverse/FMCentralBms` sau khi xác minh. Source local không chứng minh cloud
đã deploy. Không commit khóa ký hoặc token.

## Tài liệu Microsoft

- [Đăng ký steps và entity images](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in#step-registration)
- [Lỗi synchronous plugin và rollback](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/handle-exceptions)
