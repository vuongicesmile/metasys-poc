# Tài liệu Metasys POC

Tài liệu được chia theo mục đích; không thêm plan hoặc runbook vào root.
Các lệnh trong tài liệu chạy từ `D:\metasys-poc`, trừ khi có hướng dẫn khác.

| Nhóm | Tài liệu | Dùng khi |
| --- | --- | --- |
| Runbook | [Tracing plugin Equipment](runbooks/dataverse-plugin-tracing.vi.md) | Build bản tracing, bật logging và đọc START/SKIP/BLOCK/PASS |
| Runbook | [FMC BMS Demo — test plugin bằng UI](runbooks/bms-demo-app.vi.md) | Mở model-driven app, chọn form Plugin Test, Save thiếu Building để xem lỗi server |
| Runbook | [Migration/seed Building và Equipment](runbooks/bms-relations-migration.vi.md) | Chạy Provision, Preview, Apply, Verify và hiểu C# qua cách làm quen thuộc từ Python |
| Runbook | [Tự tạo và deploy Dataverse plug-in từng bước](runbooks/dataverse-plugin-step-by-step.vi.md) | Copy source/DLL nào, giải thích từng dòng C#, đăng ký PRT/SDK, tạo steps, test, update và tự làm rule tương tự |
| Demo guideline | [Trình bày demo BMS từng phần](runbooks/demo-guideline.vi.md) | Chuẩn bị màn hình, lời thoại, thao tác demo 20 phút và phương án dự phòng |
| Plan đề xuất | [Mở rộng Building và Equipment cho BMS](plans/plan.md) | Thiết kế 2 bảng mới, mapping dữ liệu cũ và kiểm thử quan hệ Dataverse |
| Plan/hướng dẫn | [Thiết kế Dataverse plug-in và bài đầu tiên](plans/dataverse-plugins.vi.md) | Chọn rule phù hợp dữ liệu BMS; tạo, build, đăng ký, test và đóng gói plug-in C# |
| Plan/receipt | [Power Automate → SQL-to-Dataverse](plans/power-automate-sql-to-dataverse.vi.md) | Kiến trúc, quyết định và trạng thái triển khai pilot |
| Runbook | [Power Automate trigger → SQL sync](runbooks/power-automate-sql-sync.vi.md) | Chạy flow, bật worker và theo dõi request |
| Plan lịch sử | [Plan 3.0](plans/plan-3.0-sql-to-dataverse.md) | Tra cứu thiết kế ban đầu; một số chi tiết đã được thay thế |
| Runbook | [Quy trình SQL → Dataverse](runbooks/sql-to-dataverse-runbook.vi.md) | Thiết lập và vận hành launcher hiện có |
| Reference | [Dataverse deployment và hợp đồng dữ liệu](reference/dataverse-deployment.md) | Tra cứu implementation, quyền, provisioning, kiểm thử và các sửa đổi Plan 3.0 |

## Quy tắc tổ chức

- `plans/`: mục tiêu, kiến trúc đề xuất, bước thực hiện, nghiệm thu, quyết định còn mở. Ghi rõ trạng thái và ngày cập nhật.
- `runbooks/`: thao tác có thể thực hiện với tính năng đã triển khai, điều kiện trước khi chạy và cách kiểm tra kết quả.
- `reference/`: hợp đồng kỹ thuật, cấu hình và ghi nhận triển khai; ghi nhận quá khứ không thay thế kiểm tra trạng thái live.
- Root giữ [README.md](../README.md) để bắt đầu và [AGENTS.md](../AGENTS.md) để agent đọc hướng dẫn.
- `.agents/skills/` giữ nguyên cấu trúc skill; `dataverse/` chứa source solution cùng README chỉ dẫn tại chỗ.
- Khi di chuyển tài liệu, cập nhật cả liên kết Markdown lẫn đường dẫn trong script/launcher; không duy trì nhiều bản nội dung giống nhau.

Khi tài liệu mâu thuẫn, code và hợp đồng implementation đã kiểm chứng được ưu tiên
hơn plan lịch sử. Plan Power Automate chỉ trở thành hướng dẫn vận hành sau khi
được triển khai, kiểm thử và cập nhật runbook.
