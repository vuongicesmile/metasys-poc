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

## Build local

Chạy từ thư mục này:

```powershell
npm ci
npm run build
npm run lint
```

`npm start` mở harness local, dùng để kiểm tra layout chứ không thay thế test trong model-driven app. Toolchain release dùng Node 24; máy local hiện cần Node tối thiểu mà dependency template yêu cầu. Nếu npm chỉ cảnh báo engine nhưng build lỗi, dùng Node 24 thay vì thay version package tự ý.

## Gắn vào Dataverse

Control đã được import vào Developer environment thuộc solution `FMCentralBms` version `1.0.3.0` ngày 2026-09-21. Chưa gắn nó vào view/subgrid, nên chưa hiển thị trong FMC BMS Demo.

1. Trong Current Points view, chọn `BmsPointGrid`, map đúng sáu cột ở bảng trên và đặt `staleAfterMinutes`.
2. Publish, kiểm tra bằng role Viewer/Operator, rồi export/unpack `FMCentralBms` vào repository.
3. Chỉ sau export mới thêm binding/custom control artifact vào source solution và bật phát hành qua tag.

Không sửa tay `dataverse/FMCentralBms` để mô phỏng bước 2–4. Xem [plan](../../../docs/plans/pcf-components-demo.vi.md) để biết checklist deployment, rollback và giới hạn của history elastic.
