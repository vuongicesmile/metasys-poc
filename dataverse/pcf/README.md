# PCF controls

Thư mục này chứa source TypeScript của các Power Apps Component Framework (PCF) controls. Đây là frontend chạy trong model-driven app; không phải .NET service và không được kết nối trực tiếp SQL Server.

| Control | Dùng cho | Trạng thái |
| --- | --- | --- |
| [BmsPointGrid](BmsPointGrid/README.md) | Current Points view và Equipment subgrid của `fmc_bmspoint` | PCF `1.0.1` đã deploy và bind `Active BMS Points`; Equipment subgrid chưa bind |

Mỗi control có `package-lock.json`; CI phải dùng `npm ci`. `node_modules`, `out`, `bin` và `obj` là output local, không commit. Khi sửa một control, chạy build riêng của control trước; `dotnet build MetasysPoc.sln` không build TypeScript PCF.

PCF chỉ được phát hành khi control đã được thêm vào solution `FMCentralBms`, kiểm tra trên Dev, rồi export/unpack source solution. Không chỉnh tay source solution để giả vờ control đã được deploy.
