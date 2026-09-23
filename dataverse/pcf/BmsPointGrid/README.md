# BmsPointGrid

`BmsPointGrid` là React dataset PCF control chỉ đọc Point từ `fmc_bmspoint`. Nó được thiết kế để gắn vào **Current Points** view trước, sau đó tái sử dụng cho Points subgrid của Equipment.

## Đọc code theo thứ tự này

1. `BmsPointGrid/ControlManifest.Input.xml`: contract với Power Apps — dataset `points`, sáu cột bắt buộc/có thể trống và property `staleAfterMinutes`.
2. `Domain/`: `PointSnapshot` là dữ liệu thuần; `ReadingFreshness` chỉ tính nhãn UI.
3. `Business/`: `IPointDataSource`/`IPointNavigation` là ranh giới; `PointGridService` tạo DTO cho UI.
4. `DataAccess/`: mapper đọc đúng logical name Dataverse, adapter gọi Dataset/Navigation API.
5. `Presentation/`: React render table, loading/empty/error, phân trang và format.
6. `index.ts`: composition root; Power Apps gọi `init`, `updateView`, `destroy` ở đây.

Luồng dữ liệu là `fmc_bmspoint` view/subgrid → `PcfPointDataSetAdapter` → Domain snapshot → `PointGridService` → React. Không có SQL connection string, EF Core, token hay API backend trong control.

## Contract bảng

| Logical name | Dùng để |
| --- | --- |
| `fmc_bmspointid` | ID record để mở Point form |
| `fmc_objectid` | Source Object ID |
| `fmc_name` | Tên Point |
| `fmc_currentvalue` | Giá trị hiện tại, precision 4 decimal |
| `fmc_unit` | Đơn vị |
| `fmc_lastreadingtime` | Thời điểm reading |
| `fmc_sourcesystem` | Nguồn dữ liệu |

Giá trị `null` không thành `0`; SQL ID không được dùng làm number. `staleAfterMinutes` mặc định 30 nếu không cấu hình, chỉ đổi nhãn `Mới/Cũ/Chưa rõ`, không tạo alarm và không ghi ngược Dataverse.

## BMS Operations Assistant Web Chat

Version `1.0.2` adds a **BMS Assistant** button to this Point grid. The button opens an iframe panel from a Copilot Studio Web Chat URL supplied by the optional `agentWebChatUrl` input. Only HTTPS Microsoft-hosted URLs are accepted; the component does not accept raw HTML or scripts.

The control is deployed to the existing `Active BMS Points` view in the Developer environment. Until `agentWebChatUrl` is configured in the view's PCF control parameters, the panel shows a setup message and does not connect to the agent. Get the real URL/embed source from Copilot Studio **Channels → Web app**; do not guess or commit auth tokens. Web Chat embed code requires the agent's **No authentication** mode, which makes the link usable by anyone who has it. This agent currently uses an Invoker-mode Power Apps MCP connection, so anonymous chat must not be assumed to have live Dataverse permissions or data access.

## Build local

Chạy từ thư mục này:

```powershell
npm ci
npm run build
npm run lint
```

`npm start` mở harness local, dùng để kiểm tra layout chứ không thay thế test trong model-driven app. Toolchain release dùng Node 24; máy local hiện cần Node tối thiểu mà dependency template yêu cầu. Nếu npm chỉ cảnh báo engine nhưng build lỗi, dùng Node 24 thay vì thay version package tự ý.

## Gắn vào Dataverse

Control version `1.0.1` đã được build, push và export xác nhận trong Developer
environment thuộc solution `FMCentralBms` ngày 2026-09-22. View `Active BMS Points`
đã bind control cho web, tablet và phone; Equipment subgrid vẫn chưa bind.

1. Sau khi sửa code, tăng `control version` trong `ControlManifest.Input.xml`.
2. Build rồi push vào solution `FMCentralBms`; publish và hard refresh app.
3. Kiểm tra bằng role Viewer/Operator, rồi export/unpack `FMCentralBms` vào repository.
4. Khi tái sử dụng cho Equipment subgrid, map đúng sáu cột và kiểm tra filter quan hệ của host.

Không sửa tay `dataverse/FMCentralBms` để mô phỏng bước 2–4. Xem [plan](../../../docs/plans/pcf-components-demo.vi.md) để biết checklist deployment, rollback và giới hạn của history elastic.
