# FM Request custom page trong FMC BMS Demo

Trạng thái (2026-09-29): **đã kiểm tra schema và app live; custom page chưa được tạo/publish**. Đây là kế hoạch và cấu hình Power Fx để hoàn tất trong Power Apps Studio, không phải biên nhận triển khai.

## Đích và hợp đồng dữ liệu

- Developer environment: `https://org06cbc9ec.crm5.dynamics.com/` (`ab191700-b99e-f111-aaa0-000d3a80bb96`).
- Solution: `FMCentralBms`; model-driven app: `FMC BMS Demo` (`fmc_FMCBMSDemo`).
- Bảng live: `fmc_fmrequest` (UserOwned), entity set `fmc_fmrequests`. Solution export ngày 2026-09-29 có `fmc_name` (required), `fmc_description` và `fmc_status` (required; Draft/Submitted/Approved/Rejected). App đã chứa bảng và navigation “FM Requests”. Role `FMC BMS Demo Operator` trong export đã có Create/Read/Write trên bảng; role Viewer chỉ có Read.
- `FmRequestService.CreateFmRequest(name, description)` trong .NET tạo bản ghi với Status = Draft. Custom page sẽ ghi trực tiếp cùng bảng qua Dataverse connector; nó **không gọi method C#**. Nếu phải đi qua C#, cần một Dataverse Custom API/plug-in riêng và hợp đồng bảo mật tương ứng.

## Kế hoạch triển khai

1. Xác nhận bảng/columns còn đúng như trên và người dùng tạo phiếu đã được gán role có Create/Read trên `fmc_fmrequest`. Không tự nâng quyền hoặc gán role nếu chưa thống nhất phạm vi.
2. Trong solution `FMCentralBms`, mở `FMC BMS Demo` bằng modern app designer, chọn **Add page → Custom page → Create custom page**. Tên đề xuất: `FM Request — New`; bật **Show in navigation** và đặt cạnh `FM Requests` ở nhóm vận hành. Custom page là dạng canvas được tích hợp vào model-driven app, phù hợp hơn việc nhúng standalone canvas app vào một form.
3. Trong Power Apps Studio, thêm Dataverse data source `FM Requests`. Dùng responsive vertical container với tiêu đề “New FM request”, phần mô tả ngắn, Edit form, nút `Save request` và `Cancel`. Giới hạn chiều rộng form khoảng 720 px; trên màn hình hẹp dùng chiều rộng container. Không thêm workflow phê duyệt hay upload ở bước này.
4. Cấu hình form chỉ tạo bản ghi mới, Name và Description hiển thị; Status được đặt Draft cố định khi lưu. Hiển thị thông báo thành công/lỗi, tránh gửi lặp trong lúc form đang submit.
5. Save và Publish custom page, quay lại app designer để Save/Publish FMC BMS Demo. Xác nhận page nằm trong navigation và solution.
6. Tạo một phiếu thử với tên duy nhất, kiểm tra ở view `FM Requests`: Name, Description và Status = Draft; sau đó xóa riêng bản ghi thử nếu không cần giữ. Export/unpack solution sang thư mục tạm và chỉ merge phần custom page/app/sitemap vào `dataverse/FMCentralBms` sau khi xem diff.

## Cấu hình Power Fx trong Studio

| Control | Property | Value |
| --- | --- | --- |
| `frmFmRequest` (Edit form) | `DataSource` | `'FM Requests'` |
| `frmFmRequest` | `DefaultMode` | `FormMode.New` |
| `frmFmRequest` | `Item` | `Defaults('FM Requests')` |
| `frmFmRequest` | `OnSuccess` | `Set(varSaving, false); Notify("FM request saved as Draft", NotificationType.Success); ResetForm(frmFmRequest); NewForm(frmFmRequest)` |
| `frmFmRequest` | `OnFailure` | `Set(varSaving, false); Notify(frmFmRequest.Error, NotificationType.Error)` |
| `btnSave` | `OnSelect` | `Set(varSaving, true); SubmitForm(frmFmRequest)` |
| `btnSave` | `DisplayMode` | `If(frmFmRequest.Valid && !varSaving, DisplayMode.Edit, DisplayMode.Disabled)` |
| `btnCancel` | `OnSelect` | `ResetForm(frmFmRequest); NewForm(frmFmRequest)` |

Trong `frmFmRequest`, thêm cards cho Name, Description và Status. Name giữ validation required của Dataverse; Description dùng multiline. Unlock Status card, đặt `Visible = false` và `Update = 'Status (FM Requests)'.Draft`. Tên enum Power Fx phải được Power Apps Studio xác nhận bằng IntelliSense vì display name có thể thay đổi theo môi trường; không lưu/publish nếu công thức báo lỗi. Nếu Studio không cho hidden Status card cập nhật, dùng `Patch` với enum Draft đã được IntelliSense xác nhận thay cho `SubmitForm`, rồi kiểm tra kết quả trước khi publish.

## Nghiệm thu và giới hạn

- Navigation mở đúng custom page từ FMC BMS Demo; không phải tab/app độc lập.
- Save một lần tạo đúng một bản ghi `fmc_fmrequest` với Status Draft; form báo lỗi khi Name trống hoặc Dataverse từ chối ghi.
- Người dùng mục tiêu có quyền dùng trang và quyền Create/Read bảng; thử bằng role dự kiến, không chỉ tài khoản maker.
- Kiểm tra solution export có custom page, app module và sitemap mới. Không coi source Power Fx trong tài liệu này là bằng chứng đã publish.

Nguồn: [Microsoft Learn — custom pages](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/model-app-page-overview), [add custom page](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/add-page-to-model-app), [SubmitForm](https://learn.microsoft.com/en-us/power-apps/maker/canvas-apps/add-form), [Patch Dataverse choice](https://learn.microsoft.com/en-us/power-platform/power-fx/reference/function-patch).
