# Email khi Excel trong SharePoint thay đổi

## Phạm vi đã triển khai

Ngày 2026-09-24, flow solution-aware `FMC - Email SPO Excel File Change` được tạo và bật trong Developer environment `5abcb0e5-99b2-e51f-aa0e-90d84405798b`, solution `FMCentralBms`. Flow ID: `e11dfffc-c2f0-4a48-bc72-f977e07e0e4a`.

Flow dùng SharePoint trigger **When a file is created or modified (properties only)** ở library `Shared Documents` của site `https://titancorpvncom.sharepoint.com/sites/Powerplatform`. Chỉ file `.xlsx` trong các nguồn `FMC-Inbox` sau được gửi email:

- `01-Master/Building`, `Equipment`, `ElectricMeter`, `WaterMeter`;
- `02-Telemetry/Electricity`, `Water`.

File tạm bắt đầu bằng `~$` và các đường dẫn khác bị bỏ qua. Email được gửi qua Office 365 Outlook tới environment variable `fmc_SpoExcelChangeEmailRecipient`, hiện mặc định là `vuong.nguyenq@titancorpvn.com`. Nội dung gồm tên, path, link, người sửa (nếu trigger trả về), thời điểm sửa/phát hiện và ETag. Flow không đọc workbook nên **không xác định hàng/ô hoặc giá trị trước → sau**. Việc phát hiện này cũng không đồng nghĩa SPO data đã import xong; flow sync/worker hiện có hoạt động độc lập.

## Kiểm tra

1. Trong Power Automate, chọn Developer environment, mở `Solutions` → `FMCentralBms` → flow `FMC - Email SPO Excel File Change`; xác nhận trạng thái **On** và các SharePoint/Outlook connections còn **Connected**.
2. Sửa một file Excel thử nghiệm thuộc một trong các folder ở trên, Save, rồi xem `Run history`. Trigger polling được cấu hình mỗi phút; thời gian nhận mail thực tế còn phụ thuộc SharePoint và Outlook. Không chỉnh file dữ liệu nghiệp vụ chỉ để test nếu chưa chuẩn bị bản test.
3. Kiểm tra email đến `vuong.nguyenq@titancorpvn.com` và ETag/link trong nội dung. Nếu không có mail, xem lần chạy: nhánh `Skip_other_files`, `Get_file_metadata` và `Send_email`.
4. Xem `FMC - Detect SPO File Change` / `fmc_spochangerequest` nếu cần đối chiếu yêu cầu sync của cùng file. Flow email không tự tạo một bảng audit row-level.

Lần triển khai 2026-09-24 xác nhận flow active, definition readback khớp, thuộc solution và connections ở trạng thái Connected. Chưa sửa file SharePoint để kích hoạt, nên **chưa có bằng chứng email đầu-cuối**.

## Triển khai lại hoặc đổi người nhận

Source triển khai: [`scripts/provision-spo-excel-change-email.py`](../../scripts/provision-spo-excel-change-email.py). Script kiểm tra `WhoAmI`, solution, site/library/inbox environment variables và connection references trước khi ghi cloud. Chạy bằng Python environment có Azure CLI và đã đăng nhập đúng tenant:

```powershell
python scripts/provision-spo-excel-change-email.py render
python scripts/provision-spo-excel-change-email.py deploy
python scripts/provision-spo-excel-change-email.py verify
```

Để đổi người nhận, cập nhật `fmc_SpoExcelChangeEmailRecipient` trong solution và tắt/bật lại flow để runtime nhận giá trị mới; không cần thay SharePoint trigger. Script `deploy` hiện guard giá trị Developer đã duyệt, nên không dùng nó để ghi đè recipient khác. [Microsoft lưu ý](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/environmentvariables-power-automate) thay environment variable trực tiếp có thể chưa có hiệu lực cho flow cho tới khi Save hoặc tắt/bật lại.

Nếu cần báo **hàng/ô cũ → mới**, phải chốt Excel Table và cột khóa ổn định cho từng file, lưu snapshot từng phiên bản và tính diff; SharePoint trigger này chỉ nhận biết file/version đổi.
