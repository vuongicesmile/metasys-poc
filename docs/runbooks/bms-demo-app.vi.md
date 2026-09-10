# FMC BMS Demo — test plugin bằng UI

## Mở app

- [FMC BMS Demo](https://org06cbc9ec.crm5.dynamics.com/main.aspx?appid=d19f4897-d227-4df3-8361-988f97c53e89)
- [Tạo Equipment bằng form Plugin Test](https://org06cbc9ec.crm5.dynamics.com/main.aspx?appid=d19f4897-d227-4df3-8361-988f97c53e89&pagetype=entityrecord&etn=fmc_bmsequipment&formid=4e2cf907-5c8b-4fa5-9bfb-55faf81fd889)

Đăng nhập bằng tài khoản Developer hiện có. App dùng dữ liệu thật trong
`FMCentralBms`, không phải simulator và không tự chạy SQL sync. Không sửa/xóa
các record catalog do SQL quản lý để thử nghiệm; dùng mã `UI-TEST-...` riêng.

App có hai mục điều hướng: **BMS Buildings** và **BMS Equipment**. Point được
đưa vào như dependency của subgrid Equipment; không thêm một luồng đồng bộ mới.

## Test Create thiếu Building

1. Mở link form test ở trên; hoặc vào app → BMS Equipment → New.
2. Nếu đi từ New, chọn form **Equipment - Plugin Test** trong bộ chọn form
   gần đầu màn hình. Form **Information** là form thường.
3. Form test phải hiện cảnh báo **PLUGIN TEST (Developer)**. Script sẽ mở tab
   **BMS Details and Relationships**; Name nằm ở tab **General**.
4. Nhập Name = `UI Plugin Test 01`, Equipment Code = một mã mới như
   `UI-TEST-20260910-001`, Equipment Type = **Test Rig**.
5. Để trống **Building**, rồi bấm **Save**.
6. Kết quả mong đợi: Dataverse từ chối tạo và hiện lỗi từ plugin:

```text
BMS-EQUIPMENT-001: Equipment phai thuoc mot Building. Hay chon Building truoc khi luu.
```

7. Đóng hộp thoại lỗi, chọn một Building đang tồn tại rồi Save lại. Rule này
   cho phép lưu nếu các field và quyền khác hợp lệ. Lần này tạo record thật.

## Test Update

Trên record test đã lưu, vẫn chọn **Equipment - Plugin Test**:

| Thao tác | Kết quả mong đợi |
| --- | --- |
| Xóa Building bằng nút x của lookup → Save | Plugin chặn; giá trị đã lưu trong server vẫn được giữ |
| Chọn lại Building hợp lệ → Save | Thành công |
| Chỉ đổi Name → Save | Thành công, không làm mất Building |

Không dùng BMS Building → New để test rule này. `RequireBuildingName` trong
bài học là ví dụ source, không phải plugin được triển khai.

## Vì sao UI này gọi được plugin?

Form thường chặn field bắt buộc trước khi gửi request. Form test gọi:

```javascript
form.getAttribute("fmc_buildingid").setRequiredLevel("none");
```

Dòng này chỉ bỏ validation bắt buộc của Building trên **form test**, không
đổi metadata `ApplicationRequired` của table và không vô hiệu hóa plugin.
Name, Equipment Code và Equipment Type vẫn phải được nhập. Khi Save, form
gửi request chuẩn của model-driven app; plugin server kiểm tra Building và
trả lỗi. Người test không cần gọi API hay chạy script trong console.

Script được gắn vào OnLoad, truyền execution context, có guard table/form,
hiện cảnh báo dữ liệu thật và không tự tạo/update record.
Source: [EquipmentPluginTest.js](../../dataverse/app-source/EquipmentPluginTest.js).

## Nếu không thấy lỗi mong đợi

- Không có banner PLUGIN TEST: kiểm tra đang chọn đúng form; thử link trực tiếp
  phía trên và reload. Form Information không có script test.
- Field khác báo thiếu: nhập đủ Name, Equipment Code, Equipment Type.
- Building vẫn báo bắt buộc trên form test: kiểm tra script đã load và sự kiện
  OnLoad; không đổi toàn bộ table thành Optional để chữa tạm.
- Báo trùng Code: dùng một mã test mới.
- Báo thiếu quyền: app không cấp thêm role cho người dùng; phải xác định tài khoản
  và quyền cần thiết trước khi thay đổi. Không tự cấp System Administrator.
- Không có record test sau lần Save lỗi: đây là kết quả đúng.

## Triển khai lại và kiểm tra

Từ `D:\metasys-poc`, với phiên Developer đã đăng nhập:

```powershell
# Read-only: identity, form, requirement level và plugin steps.
.\scripts\Invoke-BmsDemoApp.ps1 -Mode Status

# Có ghi cloud: tạo/cập nhật web resource, form test, sitemap, app; validate/publish.
.\scripts\Invoke-BmsDemoApp.ps1 -Mode Deploy

# Read-only: app published, validation, script, form, plugin steps, solution membership.
.\scripts\Invoke-BmsDemoApp.ps1 -Mode Verify

# Local unit tests; không gọi Dataverse.
node --test scripts/tests/Test-EquipmentPluginTest.mjs
```

Script cố định đúng Developer organization và solution/publisher. Đây không phải
script triển khai tùy ý sang production. Không gọi sync, không đổi cột, không
thêm role hoặc chia sẻ app cho tài khoản khác. Deploy sử dụng supported Web API
để tạo cấu hình; thao tác của người dùng cuối vẫn hoàn toàn trên UI.

## Receipt 2026-09-10

- App: `fmc_FMCBMSDemo`, ID `d19f4897-d227-4df3-8361-988f97c53e89`.
- Published: `2026-09-10T07:23:36Z` (lần publish đã kiểm tra).
- `ValidateApp`: `ValidationSuccess=true`, `ValidationIssueList=[]`.
- Form test: `4e2cf907-5c8b-4fa5-9bfb-55faf81fd889`.
- Web resource: `fmc_/scripts/EquipmentPluginTest.js`.
- Metadata Building vẫn `ApplicationRequired`; form Information không gắn script test.
- 3 nhóm unit test JavaScript pass. Không có kết nối browser để xác minh
  end-to-end thao tác Save trong phiên đăng nhập; các kết quả UI ở trên là
  expected behavior, không phải screenshot nghiệm thu.
- Export gốc: `.artifacts/bms-demo-app-20260910/FMCentralBms.zip`;
  source các thành phần được lưu trong `dataverse/FMCentralBms`.
- Export có thay đổi live không thuộc tác vụ này (Silver table và flow refresh).
  Chỉ merge thành phần app/form/web resource và dependencies liên quan; giữ nguyên
  source ngoài phạm vi. Repo không được coi là bản mirror toàn bộ environment.

Receipt là bằng chứng của lần triển khai, không thay thế Verify live sau này.

## Tài liệu Microsoft

- [Create/manage/publish model-driven apps using code](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/create-manage-model-driven-apps-using-code)
- [CopySystemForm](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/reference/copysystemform?view=dataverse-latest)
- [setRequiredLevel](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/attributes/setrequiredlevel)
- [Handle plugin exceptions](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/handle-exceptions)
