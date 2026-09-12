# FMC BMS Demo — hướng dẫn demo end-to-end

## Trạng thái triển khai

App đã được build và publish ngày 2026-09-12 vào môi trường Developer:

- Môi trường: `https://org06cbc9ec.crm5.dynamics.com/`
- App: `FMC BMS Demo`
- App ID: `d19f4897-d227-4df3-8361-988f97c53e89`
- Mở trực tiếp: [FMC BMS Demo](https://org06cbc9ec.crm5.dynamics.com/main.aspx?appid=d19f4897-d227-4df3-8361-988f97c53e89)
- Solution: `FMCentralBms`

Lần kiểm tra live khi triển khai có 6 tòa nhà, 9 thiết bị, 112 điểm Bronze hiện tại, 81.773 reading Bronze còn retention, 107 dòng Silver, 21 yêu cầu đồng bộ, 1 file SharePoint và 0 dòng đã parse.

## Bài toán app giải quyết

App ghép các phần đã có thành một luồng demo thống nhất:

```text
Microsoft Entra sign-in
        ↓
Nhập Building → nhập Equipment
        ↓
Fake Metasys/COV → SQL Bronze
        ↓
Power Apps/Power Automate tạo Sync Request
        ↓
.NET worker đọc request → Dataverse Bronze current + history
        ↓
Dataflow chuẩn hóa → Silver + cảnh báo chất lượng

SharePoint document library
        ↓
Power Automate copy file → Dataverse SPO File
        ↓
Parser tương lai → SPO Import Row
```

## 1. Đăng nhập và mở app

1. Mở link **FMC BMS Demo** ở trên.
2. Microsoft Entra yêu cầu đăng nhập bằng tài khoản tổ chức nếu trình duyệt chưa có phiên.
3. Nếu hiện trang chọn app, chọn `FMC BMS Demo`.
4. Trang đầu tiên là **Trung tâm vận hành BMS**. Trang hiển thị người dùng hiện tại, các KPI và dữ liệu gần nhất.

App dùng đăng nhập chuẩn của Power Apps/Dataverse. Không có bảng mật khẩu hoặc màn hình login tự viết, vì mật khẩu, MFA và phiên đăng nhập phải do Entra quản lý.

## 2. Phân quyền người dùng

Có hai security role dùng cho demo:

| Role | Quyền chính |
| --- | --- |
| `FMC BMS Demo Operator` | Xem toàn bộ dữ liệu demo; tạo/sửa Building và Equipment; tạo Sync Request |
| `FMC BMS Demo Viewer` | Chỉ đọc Building, Equipment, Bronze, Silver, Sync Request và dữ liệu SharePoint |

Admin vào **Power Platform admin center → Environment → Settings → Users + permissions → Users**, mở user và gán một trong hai role. App đã được gắn quyền mở cho hai role này. Tài khoản System Administrator vẫn mở được app để phát triển.

## 3. Đọc trang tổng quan

Trang **Trung tâm vận hành BMS** có:

- KPI cho Building, Equipment, Bronze Point, Bronze Reading, Silver, Sync Request và SharePoint.
- Sơ đồ luồng Bronze → chuẩn hóa → Silver.
- Năm Bronze Point mới nhất, năm Sync Request mới nhất và file SharePoint gần nhất.
- Bộ lọc nhanh cho phần dữ liệu xem trước.
- Nút làm mới để đọc lại Dataverse.
- Nút tạo Building/Equipment và điều hướng tới các danh sách nghiệp vụ.

Trang chỉ tải một lượng bản ghi giới hạn để không kéo toàn bộ bảng elastic có hàng chục nghìn reading. Muốn duyệt nhiều dữ liệu, mở view tương ứng ở menu trái.

## 4. Nhập Building và Equipment

### Tạo Building

1. Ở trang tổng quan chọn **Tạo tòa nhà**, hoặc menu **Nhập liệu → Tòa nhà** rồi chọn **New**.
2. Nhập tên, mã tòa nhà, tên nguồn và mô tả.
3. Chọn **Save & Close**.
4. Mở lại view **Tòa nhà BMS** và kiểm tra dòng vừa tạo.

### Tạo Equipment

1. Chọn **Tạo thiết bị**, hoặc menu **Nhập liệu → Thiết bị** rồi chọn **New**.
2. Nhập tên, mã thiết bị, loại thiết bị và chọn Building cha.
3. Chọn **Save & Close**.
4. Mở Building cha để xem thiết bị trong sub-grid.

Plug-in hiện có kiểm tra Equipment phải có Building hợp lệ. Có thể bỏ trống Building rồi Save trong một bản ghi thử để trình bày lỗi kiểm tra phía server; sau đó chọn Building đúng và Save lại.

## 5. Chạy SQL → Dataverse sync

Power Automate chỉ tạo lệnh trong `fmc_syncrequest`; .NET worker mới là thành phần kết nối SQL và ghi Bronze vào Dataverse.

1. Trên máy dev, chạy và giữ cửa sổ worker mở:

   ```powershell
   .\START-SQL-TO-DATAVERSE.cmd
   ```

2. Trong app chọn **Tổng quan → Đồng bộ dữ liệu**.
3. Nhập hoặc giữ `Client Request ID`, rồi chọn **Request BMS Sync**.
4. Custom API `fmc_RequestBmsSync` tạo hoặc tái sử dụng một request theo idempotency key.
5. Cloud flow `FMC - App Request BMS Sync Event` tiếp nhận business event.
6. Worker claim request, đồng bộ Building → Equipment → Point → Reading và cập nhật kết quả.
7. Vào **Vận hành → Yêu cầu đồng bộ**. Mở view **Yêu cầu đang xử lý** hoặc **Yêu cầu đồng bộ gần đây**.

Một lần chạy đạt yêu cầu khi status kết thúc thành công, `Pending After = 0` và `Quarantined Rows = 0`. `Delivered Rows = 0` vẫn hợp lệ nếu không có reading SQL mới trước cutoff.

Có thể kiểm tra worker bằng:

```powershell
Invoke-RestMethod http://localhost:5300/api/dataverse-sync/status
dotnet run --project .\DataverseSyncWorker -- --verify
```

## 6. Kiểm tra Bronze và pagination

1. Chọn **Dữ liệu Bronze → Điểm hiện tại** để xem mỗi BMS object ở trạng thái mới nhất.
2. Chọn **Dữ liệu Bronze → Lịch sử readings** để xem lịch sử trong elastic table.
3. View **Lịch sử Bronze gần đây** sắp xếp `Reading Time` giảm dần.
4. Dùng nút chuyển trang ở cuối grid hoặc tăng/giảm số dòng mỗi trang. Đây là phân trang phía server của model-driven app, nên app không tải toàn bộ 81.000+ reading vào trình duyệt.
5. Dùng filter theo Building, Object ID hoặc thời gian để kiểm tra một chuỗi dữ liệu cụ thể.

## 7. Kiểm tra Silver

1. Chọn **Dữ liệu Silver → Điểm đã chuẩn hóa**.
2. View **Dữ liệu Silver đã chuẩn hóa** hiển thị giá trị/unit đã đổi và cờ chất lượng.
3. Đổi view sang **Cảnh báo chất lượng Silver** để chỉ xem dòng có warning `Flagged`.
4. Nếu Bronze vừa đổi mà Silver chưa đổi, chạy hoặc chờ dataflow refresh. SQL worker không trực tiếp ghi bảng `cr3c8_silvernewbmspoint`.

App đang đọc bảng Silver cloud thực tế có prefix `cr3c8_`, vì đây là output của dataflow đã tồn tại trong môi trường.

## 8. Kiểm tra SharePoint → Dataverse

1. File nguồn nằm trong site `Powerplatform`, thư viện `Shared Documents`.
2. Chạy flow **FMC - Copy SPO Demo File (Manual)** hoặc trigger đã cấu hình.
3. Vào **SharePoint → File đã tiếp nhận**.
4. Mở dòng mới nhất và kiểm tra tên file, URL SharePoint, kích thước, trạng thái, thời điểm xử lý và File column đã archive.
5. Mở sub-grid dòng import hoặc menu **Dòng đã parse**.

Luồng copy file đã chạy được với file `employee_bronze_profiling_demo_full`. Giai đoạn hiện tại mới xác nhận tương tác SharePoint connector → Dataverse File column. Parser Excel/CSV tạo `fmc_spoimportrow` chưa được triển khai, vì vậy số dòng parse hiện là 0. Đây là phần tiếp theo nếu demo cần đọc nội dung từng hàng.

## 9. Đăng xuất

1. Chọn ảnh hoặc tên tài khoản ở góc trên bên phải của Power Apps.
2. Chọn **Sign out / Đăng xuất**.
3. Đóng các tab Power Apps còn mở nếu muốn kết thúc hoàn toàn phiên demo.

## 10. Kịch bản demo 10 phút

1. Đăng nhập và giới thiệu KPI trên **Trung tâm vận hành BMS**.
2. Tạo nhanh một Building và một Equipment có quan hệ cha-con.
3. Mở **Đồng bộ dữ liệu**, tạo request và theo dõi trạng thái ở **Yêu cầu đồng bộ**.
4. Mở Bronze Point, sau đó Bronze Reading và chuyển trang để chứng minh xử lý volume.
5. Mở Silver, so sánh giá trị/unit chuẩn hóa và view cảnh báo.
6. Mở file SharePoint đã archive trong Dataverse; nói rõ parser row là bước kế tiếp.
7. Mở menu hồ sơ và đăng xuất.

## 11. Kiểm tra kỹ thuật sau khi deploy

Build live ngày 2026-09-12 có kết quả:

- 97/97 bước build hoàn tất, 0 lỗi.
- 29 thành phần được tạo/cập nhật; 68 thành phần có sẵn được tái sử dụng.
- Verify của builder đạt 109/109.
- Đọc ngược môi trường xác nhận 8 bảng trong app, 1 generative page và không mất sitemap subarea.
- Solution unmanaged đã export lại vào `dataverse/FMCentralBms`.

Source thiết kế nằm ở `dataverse/app-source/fmc-bms-demo/app-spec.json`; source trang ở `trung-tam-van-hanh.tsx`. `app-spec.json` là nguồn để tái build app, còn solution export giữ bản sao chính xác của component đã deploy.
