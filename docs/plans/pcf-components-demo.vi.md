# Plan PCF components cho FMC BMS Demo

Ngày: 2026-09-22. Trạng thái: **P1/P2/P4 đã implement; `BmsPointGrid` 1.0.1 đã deploy và bind vào view `Active BMS Points`; Equipment subgrid chưa bind**.

Tài liệu này chọn component đầu tiên, thiết kế cấu trúc source tương ứng với các tầng .NET trong repo và chia công việc thành các bước có thể nghiệm thu. Cây thư mục bên dưới phản ánh source PCF hiện có; các bước gắn host/deploy vẫn chưa thực hiện.

## 1. Quyết định đề xuất

Bắt đầu bằng **`BmsPointGrid`**, một PCF dataset control chỉ đọc, dùng cho view **Current Points** của `fmc_bmspoint`. Sau khi chạy ổn trên view, dùng lại trên subgrid Points của form Equipment, với điều kiện kiểm tra đúng quan hệ/filter của subgrid.

PCF là Power Apps component framework: viết control bằng TypeScript/React và đóng gói vào Dataverse solution. Nó không phải một ASP.NET Core service. “Theo chuẩn .NET của repo” ở đây nghĩa là tách trách nhiệm, DTO, interface và adapter tương ứng; không tạo các project C# để chạy trong trình duyệt. Microsoft hỗ trợ code component trên các vị trí như column, view và subgrid của model-driven app. [Tổng quan PCF](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/custom-controls-overview).

Phạm vi MVP:

- Hiển thị Point, Object ID, giá trị hiện tại, đơn vị, thời điểm reading và nguồn dữ liệu.
- Có loading, empty, error, refresh và phân trang; chọn dòng để mở Point form.
- Hiển thị “chưa có dữ liệu” hoặc “dữ liệu cũ” theo cấu hình; không gọi đó là cảnh báo thiết bị.
- Hỗ trợ EN/VI, bàn phím và kích thước vùng hiển thị.
- Không sửa reading, không ghi SQL, không chạy sync và không tạo database/API mới.

Giữ Operations Center và trang Sync Data hiện có. Không thay toàn bộ app bằng PCF trong giai đoạn đầu.

## 2. Căn cứ từ repository

| Căn cứ | Ý nghĩa đối với thiết kế |
| --- | --- |
| [Service architecture](../reference/service-architecture.vi.md) | Backend đã chia App, Business, Common, DataAccess, Domain; không cần thêm service chỉ để phục vụ grid |
| [App demo runbook](../runbooks/model-driven-app-demo.vi.md) | App hiện là model-driven app, có Operations Center, Current Points, Reading History và các form nhập liệu |
| [Dashboard source](../../dataverse/app-source/fmc-bms-demo/src/Dashboard.tsx) | React hiện tại thuộc generative page, không phải PCF |
| [Dashboard data service](../../dataverse/app-source/fmc-bms-demo/src/services/dashboardService.ts) | Đang đọc Dataverse bằng `dataApi` do host cấp; không mang nguyên adapter này sang PCF |
| [Dataverse contract](../reference/dataverse-deployment.md) | SQL giữ full history; `fmc_bmspoint` giữ current state; `fmc_bmsreading` là elastic history có retention |
| [Release script](../../scripts/release/release.py) | Hiện pack source solution đã export; chưa có nhánh build/detect thay đổi PCF |

Các căn cứ trên là source/tài liệu checked-in, không phải xác nhận trạng thái live ngày viết plan. Trước deploy phải kiểm tra lại environment, app, solution, form và view thực tế.

## 3. Component nào đặt ở đâu?

| Ưu tiên | Component đề xuất | Host | Dữ liệu và hành vi |
| --- | --- | --- | --- |
| P1 — MVP | `BmsPointGrid` | Current Points view | Dataset `fmc_bmspoint`; chỉ đọc, refresh, phân trang, mở form |
| P1 — tái sử dụng | `BmsPointGrid` | Equipment form / Points subgrid | Cùng control; giữ phạm vi quan hệ Equipment của host |
| P2 — tùy chọn | `BmsPointValue` | Point form | Field control cho giá trị hiện tại; hiển thị đơn vị và trạng thái cập nhật |
| P3 — tùy chọn | `BmsReadingTrend` | Point form hoặc custom page được chọn sau | Đọc lịch sử có giới hạn thời gian/số dòng; kiểm tra contract elastic trước |

Không làm cả ba component ngay. Grid chứng minh được cách scaffold, mapping, lifecycle, đóng gói, phân quyền và reuse trước khi thêm biểu đồ.

Operations Center hiện là **generative page**; không đồng nhất nó với **custom page**. MVP điều hướng từ dashboard đến view/form đã gắn PCF. Không mặc định có thể nhúng PCF trực tiếp vào source TSX của generative page. Nếu cần custom page về sau, phải xác minh host riêng: WebAPI/Navigation của PCF trên custom page cần kiểm tra trong app đã publish, không chỉ trong Studio. [PCF trên custom page](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/page-code-components), [Generative pages](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/generative-pages).

## 4. Cấu trúc source đề xuất

```text
dataverse/
  app-source/fmc-bms-demo/          # Dashboard hiện có, giữ nguyên host riêng
  pcf/
    README.md                      # Hướng dẫn chung: tạo, build, dùng component
    BmsPointGrid/
      BmsPointGrid.pcfproj          # Project build PCF, không phải backend .NET
      package.json
      package-lock.json
      tsconfig.json
      pcfconfig.json
      BmsPointGrid/
        ControlManifest.Input.xml  # Dataset, property-set, resources, version
        index.ts                   # Lifecycle và nơi ghép các dependency
        generated/
          ManifestTypes.d.ts       # Tool sinh từ manifest, không sửa tay
        Domain/
          Models/PointSnapshot.ts
          Policies/ReadingFreshness.ts
        Business/
          Contracts/PointGridDto.ts
          Abstractions/IPointDataSource.ts
          Abstractions/IPointNavigation.ts
          Services/PointGridService.ts
        DataAccess/
          Adapters/PcfPointDataSetAdapter.ts
          Adapters/PcfPointNavigationAdapter.ts
          Mappers/PointRecordMapper.ts
        Presentation/
          Components/PointGrid.tsx
          Components/ReadingValue.tsx
          Components/GridState.tsx
          Styles/PointGrid.css
        Common/
          Localization/messages.ts
        strings/
          BmsPointGrid.1033.resx
          BmsPointGrid.1066.resx
      tests/                       # Dành cho P3/P5; chưa có host test fixture
        unit/
        component/
        fixtures/
      README.md                    # Contract, cách đọc code và cách cấu hình host
  FMCentralBms/                    # Source solution chuẩn sau export/unpack

docs/
  plans/pcf-components-demo.vi.md
  runbooks/pcf-components.vi.md     # Chỉ tạo khi có quy trình đã triển khai
```

Tên file cấu hình thực tế theo template PAC được chọn; không ép tạo file mà template không dùng. `node_modules`, `out`, `bin`, `obj`, bundle tạm và log build không đưa vào Git. Quy tắc với file generated theo template; tuyệt đối không chỉnh tay để thay manifest.

Chưa tạo shared package hoặc generic repository. Chỉ tách thư viện dùng chung khi có ít nhất hai component cần cùng logic. `Common` chỉ chứa localization/utility nhỏ, không trở thành nơi gom mọi thứ.

### Ánh xạ với các tầng .NET

| Backend hiện tại | PCF tương ứng | Trách nhiệm |
| --- | --- | --- |
| Domain | `Domain` | Model thuần và quy tắc xác định độ mới dữ liệu; không biết PCF/React |
| Business | `Business` | DTO, interface cần thay thế và service chuẩn bị dữ liệu cho UI |
| DataAccess | `DataAccess` | Chuyển record/dataset từ Power Apps thành model; navigation adapter |
| App / composition root | `index.ts` | Ghép adapter với service, xử lý lifecycle |
| Presentation / endpoint boundary | `Presentation` | Render React, nhận thao tác người dùng, không gọi trực tiếp database |
| Common | `Common` | Nhãn EN/VI hoặc helper thật sự dùng chung |

Dependency: Business dùng Domain; DataAccess implement interface của Business; Presentation dùng DTO và callback; `index.ts` nối các phần. Domain/Business không import `ComponentFramework.Context`, `ManifestTypes`, `Xrm` hoặc `RuntimeTypes` của dashboard.

Không cần DI container như ASP.NET Core: truyền dependency qua constructor/hàm là đủ. Không bắt mỗi class phải có interface. Không thêm `DbContext`, EF Core hay `appsettings.json` vào PCF: cấu hình control nằm ở manifest/property; cấu hình backend và bí mật vẫn ở phía server.

## 5. Contract dữ liệu và luồng đọc code

### Từ entity đến màn hình

Ở backend, Entity → EF Core mapping/DbContext → repository → service → endpoint. Với PCF này, bảng Dataverse đã tồn tại; model-driven host cung cấp dataset → adapter chuyển record → service tạo DTO → React hiển thị.

PCF không query `FM_Central` trực tiếp. Luồng nguồn vẫn là BMS → ingestion → SQL → SyncWorker → Dataverse → app. Giá trị trên grid là trạng thái **đã đồng bộ**, không phải kết nối realtime đến thiết bị.

Contract DTO dự kiến:

| Thuộc tính | Ý nghĩa/quy tắc |
| --- | --- |
| `id` | GUID của Point, dùng mở record |
| `objectId` | Source Object ID, giữ nguyên định danh |
| `name` | Nhãn Point; có fallback khi thiếu |
| `currentValue` | `number` hoặc `null`; thiếu dữ liệu không đổi thành số 0 |
| `unit` | Đơn vị, có thể thiếu |
| `lastReadingTimeUtc` | Timestamp chuẩn để tính toán; chỉ đổi timezone khi hiển thị |
| `sourceSystem` | Nguồn dữ liệu, không suy đoán từ tên Point |
| `freshness` | `unknown`, `fresh`, `stale`; không phải trạng thái alarm |

Mapping khởi điểm kiểm tra từ schema: `fmc_bmspointid`, `fmc_objectid`, `fmc_name`, `fmc_currentvalue`, `fmc_unit`, `fmc_lastreadingtime`, `fmc_sourcesystem`. P0 phải kiểm chứng logical name, kiểu dữ liệu và cấu hình view/property-set; không coi tên đề xuất là metadata live. Không tự đoán entity set bằng cách thêm chữ `s`.

Hai interface đủ cho MVP:

- `IPointDataSource`: đọc snapshot hiện có, yêu cầu refresh và chuyển trang. Snapshot gồm rows, loading, error, hasNextPage/hasPreviousPage; tổng dòng chỉ có khi host cung cấp đáng tin cậy.
- `IPointNavigation`: mở Point form bằng ID; UI không tự dựng URL môi trường.

Paging của PCF không phải một hàm HTTP trả ngay trang mới: adapter gửi yêu cầu, host đưa dữ liệu mới vào `updateView`. Không gửi song song nhiều yêu cầu chuyển trang. [Paging API](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/reference/paging).

### Thứ tự đọc code cho người mới

1. `Domain/Models/PointSnapshot.ts`: một Point trong chương trình gồm những gì?
2. `Business/Contracts/PointGridDto.ts`: UI thực sự cần những trường nào?
3. `Business/Abstractions`: service cần đọc dữ liệu và điều hướng qua những thao tác nào?
4. `DataAccess/Mappers` và `Adapters`: record Power Apps được chuyển thành dữ liệu thuần như thế nào?
5. `Business/Services`: xử lý thiếu dữ liệu, độ mới và trạng thái màn hình ở đâu?
6. `Presentation/Components`: DTO được render và callback được gọi thế nào?
7. `index.ts`: ai tạo các object và khi nào Power Apps gọi chúng?
8. `ControlManifest.Input.xml`: Power Apps biết dataset, cấu hình, resource và version của control bằng cách nào?

Khi implement, chú thích tiếng Việt cho từng dòng xử lý quan trọng: input/output, chuyển kiểu, null, timestamp, phân trang và cleanup. Giải thích “vì sao” bên cạnh “làm gì”; không sửa file generated hoặc thêm chú thích máy móc vào từng dấu ngoặc. README có một luồng mẫu từ record đến DTO đến màn hình.

## 6. Lifecycle, UI và bảo toàn dữ liệu

- `init`: tạo adapter/service, đăng ký theo dõi kích thước nếu cần. Không mở kết nối SQL hoặc tự đăng nhập Entra.
- `updateView`: cập nhật context/snapshot và trả React element. Không gọi refresh vô điều kiện gây vòng lặp render → query → render.
- `getOutputs`: MVP không ghi dữ liệu; không dùng output để kích hoạt sync ngầm.
- `destroy`: dọn timer/subscription do control tạo, ngăn callback bất đồng bộ cập nhật instance đã hủy.

Dùng template React virtual control và platform libraries theo manifest được hỗ trợ. Không copy nguyên version React/Fluent của generative page sang PCF; đây là hai host/build contract khác nhau. [React controls và platform libraries](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/react-controls-platform-libraries).

Quy tắc MVP:

- Chỉ đọc các cột cần thiết. Đề xuất page size ban đầu 25; không tự tải mọi trang để đếm hoặc tìm kiếm.
- Dùng filter/sort của host ở MVP; giữ điều kiện quan hệ Equipment. Search riêng chỉ thêm khi có server-side query và kiểm thử việc kết hợp filter, không tìm vài dòng local rồi tuyên bố đã tìm toàn bộ.
- Phân biệt “số dòng đã tải” với “tổng kết quả”. Khi không biết tổng, hiển thị không xác định, không giả định bằng số dòng hiện có.
- Giữ độ chính xác bốn chữ số thập phân theo contract; format chỉ phục vụ hiển thị, không ghi ngược dữ liệu đã làm tròn. SQL bigint/source ID nếu xuất hiện ở bước sau phải giữ dạng text.
- Ngưỡng stale là property được mô tả rõ; chưa có yêu cầu nghiệp vụ thì không tự đặt làm ngưỡng cảnh báo vận hành.
- EN/VI theo trải nghiệm app hiện có nhưng có fallback độc lập khi mở view trực tiếp. Đổi ngôn ngữ không tải lại dữ liệu.
- Chỉ đọc dưới quyền user Power Apps hiện tại. Không nhúng token, connection string, tài khoản dịch vụ hoặc dùng quyền cao hơn để che lỗi thiếu quyền.
- Loading/empty/error có nội dung rõ ràng; giá trị số 0 hiển thị là 0. Màu không phải tín hiệu duy nhất; có nhãn và thao tác bàn phím.

Nếu làm `BmsReadingTrend`, chỉ đọc lịch sử còn retention với khoảng thời gian/số dòng giới hạn. Không suy diễn “không còn history trong Dataverse” thành “SQL không có dữ liệu”; không đổi GUID, partition, TTL, ledger hoặc current-state ordering để phục vụ chart.

## 7. Các bước implement và điều kiện hoàn thành

| Bước | Công việc | Điều kiện hoàn thành |
| --- | --- | --- |
| P0 — chốt host/contract | Kiểm tra Current Points view, Point form, Equipment relation/subgrid, metadata và quyền; chốt version Node/PAC/platform libraries | Đã xác nhận local source schema và PAC template; host IDs/quyền live còn chờ deployment scope |
| P1 — scaffold | Tạo project React dataset, thư mục các tầng, DTO/interfaces, fixture và README tiếng Việt | Đã scaffold, có README tiếng Việt và build local thành công; chưa có host fixture |
| P2 — grid | Implement dataset adapter, render, navigation, paging và refresh | Đã implement/build; kiểm tra host thật, filter subgrid và role còn chờ P3 |
| P3 — tích hợp Dev | Đóng gói, thêm control vào solution chính và cấu hình view/subgrid | Control đã import vào `FMCentralBms` version `1.0.3.0` và được export xác nhận; binding view/subgrid, app publish và role check còn chờ |
| P4 — release | Thêm PCF change detection/build/pack vào pipeline và quy trình rollback | Đã có detection/build và chặn source-only; pack/import chỉ hoàn tất sau P3 export solution |
| P5 — mở rộng tùy chọn | Value control hoặc trend sau khi MVP được chấp nhận | Có yêu cầu và contract riêng; không mặc định thuộc MVP |

P0 kiểm tra local được làm độc lập. P3 và kiểm tra live cần đúng environment và phạm vi triển khai được cho phép; việc có plan không phải lệnh deploy.

### Lệnh scaffold đã chạy local

PAC launcher hiện báo version `2.11.2`; usage đã xác nhận có template `dataset` và framework `react`. Trong lần kiểm tra này `--help` không được subcommand nhận và CLI in usage; không có project nào được tạo.

Chạy từ root repository khi bắt đầu P1, với thư mục đích chưa tồn tại hoặc rỗng:

```powershell
pac pcf init --namespace FMC.Bms --name BmsPointGrid --template dataset --framework react --outputDirectory dataverse/pcf/BmsPointGrid
Set-Location dataverse/pcf/BmsPointGrid
npm install
npm run build
npm start
```

Commit lockfile sau lần install đầu; CI dùng `npm ci`. Xác nhận Node/npm tương thích template tại thời điểm triển khai. Lệnh trên chỉ tạo/build control và mở test harness; không chứng minh control hoạt động trong app thật. Cú pháp PAC tham khảo tại [PCF CLI](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/pcf).

`dotnet build MetasysPoc.sln` không thay thế PCF build. Không cần thêm project PCF vào solution backend hoặc đổi các service .NET hiện tại.

## 8. Đóng gói vào solution và pipeline hiện có

Solution đích vẫn là `FMCentralBms`, publisher `FMCentralBmsPublisher`, prefix `fmc`. Namespace code `FMC.Bms` không thay publisher prefix. Không tạo một solution rỗng trùng tên rồi ghi đè source đã export.

Phương án triển khai:

1. P0/P1 làm thử luồng packaging bằng solution project `.cdsproj` riêng cho build, tham chiếu `.pcfproj` qua `pac solution add-reference --path ...`. Vị trí build project phải tách khỏi `dataverse/FMCentralBms` để không phá canonical source. Cú pháp chạy trong thư mục solution project, không phải tùy ý ở repo root. [Solution CLI](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/solution).
2. Nếu dùng solution vận chuyển riêng để đưa control vào Dev, ghi rõ nó chỉ là phương tiện phát triển. Thêm control và form/view binding vào `FMCentralBms`; artifact bàn giao cuối là solution chính đầy đủ, không phải chỉ ZIP chứa PCF.
3. Sau deployment được cho phép, xác minh membership/control version/binding, publish, rồi export/unpack `FMCentralBms` vào thư mục chuẩn. Không chỉnh manifest solution bằng tay để giả lập việc đã deploy.
4. Release planner hiện phát hiện `dataverse/pcf/**`, chạy `npm ci`/`npm run build` khi thay đổi PCF đi cùng `dataverse/FMCentralBms`, và chặn tag chỉ có PCF source. Nó chưa tự thêm control vào solution: bước đó chỉ đúng sau P3 import, cấu hình host và export canonical solution.
5. Bổ sung pipeline: nhận biết thay đổi PCF → `npm ci` → production build theo toolchain đã chốt → đưa control output vào staged solution theo cơ chế packaging đã xác minh → kiểm tra manifest/version → pack. Phải dùng bundle vừa build, không dùng nhầm bundle cũ trong export.
6. Kiểm tra ZIP cuối chứa control, resources và cấu hình host; lưu version, commit và receipt. Thử trường hợp chỉ sửa PCF nhưng không sửa dashboard/.NET để tránh release bỏ sót.

Điểm cần chứng minh trong packaging spike: cách ghép output `.pcfproj` với toàn bộ exported solution hiện tại, khả năng build trên runner Windows, và bảo toàn plugin/web resources/app. Chưa chốt spike thì chưa ghi nhận pipeline có thể release PCF tự động. Nguyên tắc source, version và solution packaging dựa trên [PCF ALM](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/code-components-alm).

Giữ cách release Dev hiện có; không suy diễn thành production-ready. Managed solution cho môi trường downstream là quyết định ALM riêng khi có môi trường đích.

## 9. Verification và rollback khi implement

Checklist nghiệm thu đề xuất, **không phải các kiểm thử đã chạy trong lần viết plan**:

- Unit: mapping null/0, thiếu cột, timestamp không hợp lệ, đơn vị rỗng, freshness boundary và độ chính xác hiển thị.
- Component: loading, empty, error, retry/refresh, ngôn ngữ, keyboard và resize.
- Lifecycle: refresh không lặp vô hạn; chuyển trang bị khóa khi loading; destroy dọn tài nguyên.
- Host thật: view và Equipment subgrid giữ filter/sort; mở đúng Point; không hiện record Equipment khác.
- Security: Viewer và Operator chỉ thấy record/cột được cấp quyền; lỗi thiếu quyền được hiển thị, không bypass.
- Build/ALM: build production từ clean checkout, control resources đầy đủ, tăng version, export/unpack và release PCF-only được kiểm tra.
- Regression: dashboard, trang Sync Data và default grids còn hoạt động. Không chạy ingestion/sync chỉ để kiểm tra một thay đổi UI.

Không xóa control chuẩn trong MVP. Ghi lại cấu hình trước thay đổi; nếu PCF lỗi, chuyển view/subgrid về control mặc định và publish. Giữ source/artifact trước đó để sửa hoặc phát hành lại qua quy trình có version phù hợp; không giả định import ZIP version thấp luôn rollback được. Không xóa bảng hoặc dữ liệu để gỡ control; kiểm tra dependency trước khi remove solution component.

## 10. Kết luận phạm vi

Nên thêm **một module frontend PCF có phân tầng**, không thêm một backend API/EF Core/database mới. Tận dụng bảng, quyền và solution hiện có; giữ dashboard generative page độc lập. Khi bắt đầu implement, thứ tự là **P0 → P1 → P2 → P3 → P4**; chỉ mở rộng chart/field control sau khi grid đã được nghiệm thu.

Lần cập nhật sau đã scaffold và implement local P1/P2 trong `dataverse/pcf/BmsPointGrid`, có comment tiếng Việt và README. Release planner cũng bắt buộc thay đổi PCF phải đi cùng source solution `FMCentralBms` đã export/unpack, đồng thời build PCF trước deploy. `FMC.Bms.BmsPointGrid` đã được import/export xác nhận trong Developer environment ngày 2026-09-21. Live export có các thay đổi không thuộc PCF đang lệch với Git, nên chỉ artifact PCF, root component và version đã được merge vào source; chưa overwrite toàn bộ solution. Chưa chạy test host hoặc thay database.

## 11. Receipt `ScrollDownButton`

Ngày 2026-09-22, component tối giản `fmc_FMC.Bms.ScrollDownButton` version
`1.0.0` đã được push vào đúng Developer organization
`ab191700-b99e-f111-aaa0-000d3a80bb96` và thêm vào solution `FMCentralBms`.
Binding thử nghiệm với `fmc_name` trên form `BMS Point - Demo` sau đó đã được gỡ
và publish lại vì yêu cầu cuối là đặt nút ở Home, không phải Current Point.

Home generative page `9d05b7f9-f4c4-4572-b6b8-0220b51812f3` được cập nhật bằng
nút Fluent UI `↓` cố định ở góc dưới phải. Nút cuộn smooth tới cuối page, tôn
trọng `prefers-reduced-motion`, có nhãn Việt/Anh và không thêm data query.
Upload/read-back xác nhận source live có handler và button; `ValidateApp` của
`FMC BMS Demo` thành công. PCF vẫn là root component của solution nhưng không
còn form binding (`FormBindingPresent: false`). Script kiểm tra metadata là
`scripts/Deploy-ScrollDownButton.ps1 -Mode Verify`.

Sau phản hồi runtime cùng ngày, `contain: layout` được gỡ khỏi scroll root vì
containment này làm `position: fixed` neo theo chính container và bị cuốn lên.
Regression test xác nhận tọa độ button không đổi trước/sau khi scroll; bản sửa
đã được upload, publish và đọc ngược lại từ đúng Home page ID.
