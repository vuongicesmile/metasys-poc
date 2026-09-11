# Trace cho RequireEquipmentBuilding

Source local version `1.0.0.1` thêm tracing; chưa update assembly trên Dataverse.

| Marker | Ý nghĩa |
| --- | --- |
| START | Message, Stage, Mode, Depth, CorrelationId, OperationId |
| SKIP | Sai message/stage/mode, Target không phù hợp, hoặc Update không gửi Building |
| BLOCK | `BMS-EQUIPMENT-001`, Building missing/null và lookup có trong payload hay không |
| PASS | Building được cung cấp, rule cho request tiếp tục |

Không log toàn bộ payload, token, tên thiết bị hoặc dữ liệu cá nhân. PASS chỉ nói
rule này đã cho qua, không bảo đảm các bước Dataverse phía sau đều thành công.
Update không khớp filtering attributes sẽ không gọi plugin, nên không có START/SKIP.
Form chặn required trước khi gửi request cũng không có trace của plugin.

## Build và đưa lên môi trường test

```powershell
dotnet build .\plugins\FMCentralBms.Plugins\FMCentralBms.Plugins.csproj -c Release
```

Trong PRT, kết nối đúng Developer environment, chọn assembly
`FMCentralBms.Plugins` → Update → chọn
`plugins/FMCentralBms.Plugins/bin/Release/net48/FMCentralBms.Plugins.dll`.
Giữ hai steps hiện có. Bản export solution vẫn là receipt của lần deploy cũ;
sau update cần export lại nếu muốn lưu artifact mới.

## Bật và đọc log

Trong PRT, dùng **Settings → Logging to Plug-in Trace Log**, hoặc System Settings
→ Customization → Enable logging to plug-in trace log:

- All: lưu cả lần PASS/SKIP và lỗi; phù hợp lúc test.
- Exceptions: chỉ lưu khi plugin trả exception; dùng khi chỉ cần điều tra lỗi.
- Off: không lưu PluginTraceLog.

Đây là setting cấp environment. Ghi lại mức cũ và khôi phục khi kết thúc test.
Việc thêm source tracing không tự thay đổi setting này.

Mở Plugin Trace Log bằng giao diện quản trị hoặc Plugin Trace Viewer. Lọc
Type Name `FMCentralBms.Plugins.RequireEquipmentBuilding`, sắp xếp Created On mới
nhất, đọc Message Block và Correlation ID. Dùng form Equipment - Plugin Test
của [app demo](bms-demo-app.vi.md): thiếu Building có START/BLOCK; Building hợp lệ
có START/PASS khi logging All.

Nguồn: [Microsoft — Logging and tracing](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/logging-tracing).
