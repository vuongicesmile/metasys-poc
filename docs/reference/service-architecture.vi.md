# Kiến trúc service và cách mở rộng Metasys POC

Refactor ngày 2026-09-12–13 tách phần khởi tạo ứng dụng, điều phối nghiệp vụ, giao tiếp HTTP/SSE, truy cập SQL và giao diện dashboard. Các URL, cờ CLI, bảng dữ liệu, cách tính ID và quy trình xác nhận delivery giữ nguyên. Adapter SSE kiểm tra cancellation trước khi phát event, kể cả khi `StreamReader` đã có dòng trong buffer.

## 1. Đọc source từ đâu

| Thành phần | Nơi đọc | Trách nhiệm |
| --- | --- | --- |
| Khởi động mỗi ứng dụng | `Program.cs` | Tạo host, gọi đăng ký service và map endpoint |
| Dependency injection | `Hosting/ServiceCollectionExtensions.cs` | Chọn implementation, cấu hình và lifetime |
| API HTTP | `Endpoints/` | Route, request/response, Swagger; gọi service xử lý |
| Hợp đồng service | `Abstractions/` | Interface tại các ranh giới cần thay thế hoặc kiểm thử |
| Ingestion | `BmsIngestionApp/Services/CovIngestionWorker.cs` | Đọc catalog → lưu catalog nếu bật SQL → subscribe → nhận event → lưu reading |
| Metasys transport | `BmsIngestionApp/Services/MetasysClient.cs` | HTTP, JSON, subscription, đọc SSE và giải phóng connection |
| Lưu dữ liệu nguồn | `BmsIngestionApp/Services/BmsReadingRepository.cs` | Upsert catalog và append SQL readings |
| Xử lý request sync | `DataverseSyncWorker/Services/CommandProcessor.cs` | Claim request, chốt cutoff, điều phối batch, progress, requeue và completion |
| Đồng bộ một batch | `DataverseSyncWorker/Services/SyncEngine.cs` | Lock, đọc ledger, mapping, ghi Dataverse, Ack/quarantine |
| Lệnh bảo trì | `DataverseSyncWorker/Hosting/WorkerCommandLine.cs`, `WorkerCommandDispatcher.cs` | Tách cờ CLI khỏi host và thực thi lệnh trước khi khởi động background worker |
| Trạng thái runtime | `Services/RuntimeState.cs`, `IngestionStatusTracker.cs` | Snapshot phục vụ API theo dõi |
| Dashboard | `dataverse/app-source/fmc-bms-demo/src/` | Service, hook, component và styles riêng |

Các service nghiệp vụ không tự tạo implementation của dependency. Ví dụ `CovIngestionWorker` nhận `IMetasysClient` và `IBmsReadingRepository` qua constructor. Khi muốn thay nguồn simulator bằng adapter Metasys thật, tạo implementation mới của `IMetasysClient`, rồi đổi đăng ký trong `AddIngestionServices`.

Không ép mọi class có interface. `ISyncLedger` chỉ cung cấp hai phép đọc mà `CommandProcessor` cần. `SyncEngine` vẫn dùng `SqlStore` để giữ SQL session lock và thứ tự ghi ledger hiện có. Các service provisioning, mapper và adapter Dataverse chuyên biệt vẫn được giữ riêng theo chức năng.

## 2. Luồng dữ liệu và lifetime

1. `FakeMetasysApi` phát catalog và COV qua SSE.
2. `CovIngestionWorker` gọi `IMetasysClient.ReadCatalogAsync`, lưu catalog rồi subscribe các Object ID.
3. `MetasysClient.ReadEventsAsync` chuyển từng dòng SSE thành `CovEvent`. Worker cập nhật trạng thái và gọi repository khi bật SQL. Chế độ `--no-sql` không gọi persistence.
4. SQL giữ đầy đủ lịch sử nguồn. `SyncWorker` chạy liên tục hoặc nhận yêu cầu ở chế độ CommandDriven.
5. `CommandProcessor` claim yêu cầu, giữ cutoff/baseline, gọi `ISyncEngine` đến khi hết hàng eligible hoặc phải requeue.
6. `SyncEngine` dùng semaphore và SQL application lock, ghi catalog/current/history rồi mới Ack. Bộ đếm completion lấy từ SQL ledger.
7. Dashboard đọc dữ liệu Dataverse qua `dataApi` của Power Apps, hiển thị trạng thái và điều hướng đến form/view/demo có sẵn.

`SqlStore`, `SyncEngine`, `CommandProcessor` và state tracker có lifetime singleton. Các đăng ký interface trỏ về **cùng instance** của concrete service; tạo hai instance `SyncEngine` sẽ làm mất ý nghĩa của local serialization. `MetasysClient` dùng `IHttpClientFactory`: client được tạo theo thao tác, còn handler được factory quản lý. SSE dùng `ResponseHeadersRead`, nhận cancellation và giải phóng response/stream khi kết thúc.

Dispatcher chỉ resolve các service cần cho lệnh đã chọn. `--self-test`, `--verify` và các lệnh bảo trì vẫn thoát trước `RunAsync`; không vô tình khởi động worker sync.

## 3. Cấu trúc dashboard

```text
dataverse/app-source/fmc-bms-demo/
  src/
    Dashboard.tsx                 # Layout, grid, tương tác trang
    models/dashboard.ts           # Kiểu dữ liệu dashboard
    services/dashboardService.ts  # Query 8 bảng, giới hạn số dòng
    services/localization.ts      # EN/VI, status, ngày và số
    services/navigationService.ts # Tên người dùng và Xrm navigation
    services/presentation.ts      # FormattedValue và tìm kiếm
    hooks/useDashboard.ts         # Cache, request đang chạy, refresh
    hooks/useLanguage.ts          # Preference và storage event
    components/DashboardCards.tsx # KPI, pipeline step, empty state
    styles/dashboardStyles.ts     # Fluent UI styles
  build.cjs                       # Gom module thành TSX cho Power Apps
  package.json / package-lock.json
  trung-tam-van-hanh.tsx           # File sinh ra để đóng gói/deploy
```

Sửa code trong `src/`. Chạy `npm run build` để sinh lại artifact. Builder giữ TypeScript và import `RuntimeTypes`; không thêm runtime library vào app. Builder kiểm tra vòng phụ thuộc, identifier trùng và chỉ nhận named imports; không dùng default import, alias nội bộ hay re-export trong module. `npm run check` phát hiện artifact chưa cập nhật.

Đây là bước đóng gói source, không thay thế compiler của Power Apps. `RuntimeTypes.ts` hiện phụ thuộc các khai báo nền tảng do host cung cấp (`TableRow`, `BaseUxAgentDataApi`, v.v.), nên chạy `tsc` độc lập trong checkout chưa phải phép kiểm tra đầy đủ. Browser regression kiểm tra hành vi JavaScript của cả hai dạng source; khi publish vẫn cần compiler/verification của Power Apps theo runbook deployment.

Các giới hạn đọc dữ liệu, cache và cơ chế refresh giữ nguyên. Đổi ngôn ngữ không query lại Dataverse. Đăng nhập và đăng xuất vẫn thuộc phiên Power Apps/Entra; navigation service không lưu token hoặc tạo cơ chế đăng nhập riêng.

## 4. Thêm một chức năng theo từng bước

Ví dụ thêm nguồn đọc dữ liệu BMS:

1. Xác định dữ liệu đầu vào phải chuyển thành `MetasysCatalog` và `CovEvent` như thế nào; giữ Object ID và độ chính xác hiện tại.
2. Tạo `Services/RealMetasysClient.cs` implement `IMetasysClient`; authentication và HTTP thuộc adapter này.
3. Đăng ký client/configuration trong `Hosting/ServiceCollectionExtensions.cs`. Không đưa credential vào source.
4. Test lỗi HTTP, dữ liệu rỗng/sai, thứ tự event và cancellation bằng HTTP handler giả.
5. Test worker với repository giả để xác nhận catalog được xử lý trước readings và `--no-sql` không ghi SQL.
6. Chạy bộ self-test SQL trước khi thử dữ liệu thật. Không thay ID, watermark hoặc Ack để làm backlog biến mất.

Ví dụ thêm một KPI dashboard:

1. Thêm kiểu vào `models/dashboard.ts`.
2. Thêm query có giới hạn trong `services/dashboardService.ts`.
3. Thêm nhãn EN/VI vào `services/localization.ts`.
4. Dùng component KPI trong `Dashboard.tsx`; không đặt HTTP/query trực tiếp trong JSX.
5. Build, chạy browser regression rồi dùng artifact TSX theo quy trình deploy app hiện có.

## 5. Kiểm tra trước khi bàn giao

Chạy từ root repository:

```powershell
dotnet build MetasysPoc.sln -c Release
dotnet test tests/MetasysPoc.Tests/MetasysPoc.Tests.csproj -c Release
dotnet DataverseSyncWorker/bin/Release/net10.0/DataverseSyncWorker.dll --self-test
npm --prefix dataverse/app-source/fmc-bms-demo ci
npm --prefix dataverse/app-source/fmc-bms-demo run build
npm --prefix dataverse/app-source/fmc-bms-demo test
node scripts/tests/Test-BmsEventDemoLanguage.mjs
git diff --check
```

23 unit test dùng fake service/HTTP, không gọi SQL hoặc Dataverse. Các ca kiểm tra gồm cutoff/baseline, progress từ ledger, requeue, failure, cancellation, singleton dùng chung, `--no-sql`, thứ tự ingestion, lỗi HTTP/SSE và giải phóng stream. `--self-test` tạo database SQL có tên duy nhất rồi xóa sau khi chạy; sink Dataverse là giả lập. Browser regression chạy trên cả source module và artifact sinh ra, dùng dữ liệu tổng hợp, Microsoft Edge headless và xuất screenshot vào `.artifacts/language-tests`.

Checkout này thiếu `plugins/FMCentralBms.Plugins/FMCentralBms.Plugins.snk`. Build solution với signing mặc định sẽ lỗi; đã kiểm tra compilation bằng `-p:SignAssembly=false`. Cách này chỉ dùng để kiểm tra code, DLL không ký không phải artifact để deploy plugin. Cần khóa ký hiện có để build plugin dùng cho deployment; không tạo khóa thay thế làm đổi assembly identity.

NuGet restore của test project hiện báo `NU1903` cho dependency gián tiếp `System.Security.Cryptography.Xml 8.0.2` từ SDK hiện có. Refactor này không nâng phiên bản SDK; cần xử lý dependency trong một thay đổi có kiểm thử tương thích riêng.

Thay đổi này chuẩn hóa source và quy trình build/test local. Không thay schema, không chạy sync vào dữ liệu live và không triển khai lại cloud chỉ để đổi cách tổ chức file.
