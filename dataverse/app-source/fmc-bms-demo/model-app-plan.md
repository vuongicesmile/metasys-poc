# FMC BMS Demo — design

Demo tiếng Việt cho nhập catalog, Bronze-to-Silver, SQL sync và tiếp nhận file SharePoint.

Generated from `app-spec.json` by `scripts/write-app-spec-doc.js`. **Regenerate rather than
hand-edit** — `app-spec.json` is the source of truth, so a manual edit here is lost on the next
run and silently disagrees with what actually builds.

## Environment

| Setting | Value |
|---|---|
| Environment | https://org06cbc9ec.crm5.dynamics.com/ |
| Solution | FMCentralBms |
| Publisher prefix | fmc |
| App unique name | fmc_FMCBMSDemo |

## Jobs to be done

| Persona | Job to be done | Surfaces that satisfy it |
|---|---|---|
| FMC BMS Demo Operator | Theo dõi pipeline end-to-end | trung-tam-van-hanh, Điểm BMS hiện tại, Lịch sử Bronze gần đây, Dữ liệu Silver đã chuẩn hóa |
| FMC BMS Demo Operator | Nhập catalog BMS | Tạo nhanh tòa nhà BMS, Tạo nhanh thiết bị BMS, Tòa nhà BMS - Demo, Thiết bị BMS - Demo |
| FMC BMS Demo Operator | Yêu cầu và theo dõi đồng bộ | Đồng bộ dữ liệu, Yêu cầu đồng bộ gần đây, Yêu cầu đang xử lý |
| FMC BMS Demo Operator | Kiểm tra tiếp nhận file SharePoint | File SharePoint gần đây, File SharePoint - Demo, Dòng import SharePoint |
| FMC BMS Demo Viewer | Xem demo ở chế độ chỉ đọc | trung-tam-van-hanh, Điểm BMS hiện tại, Dữ liệu Silver đã chuẩn hóa, Yêu cầu đồng bộ gần đây, File SharePoint gần đây |

## Data model

### BMS Building `fmc_bmsbuilding` _(existing table — reused, not created)_

Building catalog synchronized from BMS and available for controlled demo entry.

| Column | Type | Notes |
|---|---|---|
| Name | Text | primary name |
| Code | Text | — |
| Description | Memo | — |
| Source Building | Text | — |

### BMS Equipment `fmc_bmsequipment` _(existing table — reused, not created)_

Equipment catalog linked to a building; the existing plug-in validates the parent and code.

| Column | Type | Notes |
|---|---|---|
| Name | Text | primary name |
| Code | Text | — |
| Equipment Type | Choice | choices: Water Meter, Temperature Sensor, Test Rig |
| Description | Memo | — |

### [Bronze] BMS Point `fmc_bmspoint` _(existing table — reused, not created)_

Current Bronze point state synchronized from SQL.

| Column | Type | Notes |
|---|---|---|
| Name | Text | primary name |
| Object ID | Text | — |
| Object Type | Text | — |
| Building | Text | — |
| Current Value | Decimal | — |
| Unit | Text | — |
| Reading Time | DateTime | — |
| SQL Reading ID | Text | — |
| Source System | Text | — |

### [Bronze] BMS Reading `fmc_bmsreading` _(existing table — reused, not created)_

Retained Bronze reading history in an elastic table with event-time TTL.

| Column | Type | Notes |
|---|---|---|
| Name | Text | primary name |
| Object ID | Text | — |
| Object Name | Text | — |
| Building | Text | — |
| Reading Value | Decimal | — |
| Unit | Text | — |
| Reading Time | DateTime | — |
| SQL Reading ID | Text | — |
| Source System | Text | — |

### [Silver-Cloud] BMS Point `cr3c8_silvernewbmspoint` _(existing table — reused, not created)_

Cloud dataflow output with normalized values, units and quality flags.

| Column | Type | Notes |
|---|---|---|
| BMS Point ID | Text | primary name |
| Name | Text | — |
| Object ID | Text | — |
| Building | Text | — |
| Value Converted | Double | — |
| Unit Converted | Memo | — |
| Warning Flag | Memo | — |
| Reading Time | DateTime | — |

### Sync Request `fmc_syncrequest` _(existing table — reused, not created)_

Command queue and execution receipt for SQL-to-Dataverse synchronization.

| Column | Type | Notes |
|---|---|---|
| Name | Text | primary name |
| Sync Status | Choice | choices: Queued, Running, Succeeded, Completed with issues, Failed |
| Pipeline | Text | — |
| Command | Text | — |
| Requested By | Text | — |
| Requested Cutoff SQL ID | Text | — |
| Delivered Rows | BigInt | — |
| Pending After | BigInt | — |
| Quarantined Rows | BigInt | — |
| Claim Attempts | Integer | — |
| Started At | DateTime | — |
| Completed At | DateTime | — |
| Error Message | Memo | — |

### SPO File `fmc_spofile` _(existing table — reused, not created)_

Idempotent SharePoint receipt with archived Dataverse file content.

| Column | Type | Notes |
|---|---|---|
| Name | Text | primary name |
| File Name | Text | — |
| Archived File | File | — |
| File Size | Integer | — |
| SharePoint URL | Text | — |
| Archive Status | Choice | choices: Received, Processing, Archived, Failed, Ignored |
| Import Status | Choice | choices: Not Requested, Processing, Imported, Failed |
| Imported Row Count | Integer | — |
| Received At | DateTime | — |
| Processed At | DateTime | — |
| Error Message | Memo | — |

### SPO Import Row `fmc_spoimportrow` _(existing table — reused, not created)_

Parsed data rows associated with an imported SharePoint file version.

| Column | Type | Notes |
|---|---|---|
| Name | Text | primary name |
| Ordinal | Integer | — |
| Code | Text | — |
| Building Name | Text | — |
| Floor Count | Integer | — |
| Source ETag | Text | — |

### Relationships

| Kind | From | To | Lookup |
|---|---|---|---|
| 1:N | fmc_bmsbuilding | fmc_bmsequipment | fmc_buildingid |
| 1:N | fmc_bmsequipment | fmc_bmspoint | fmc_equipmentid |
| 1:N | fmc_spofile | fmc_spoimportrow | fmc_fileid |

## Surfaces

### Generative pages

| Page | Key | Purpose | Reads | Navigates to | State |
|---|---|---|---|---|---|
| Trung tâm vận hành BMS | `trung-tam-van-hanh` | Vietnamese landing page for a live demo. Show the signed-in Microsoft Entra user and explain that sign-out is available from the Power Apps profile menu. Present KPI cards for Buildings, Equipment, current Bronze Points, retained Bronze Readings, Silver rows, Sync Requests and SharePoint files. Show only the newest five Bronze points, newest five sync requests and newest SharePoint receipt. Add a Bronze-to-Silver pipeline strip and quick actions that open create forms for fmc_bmsbuilding and fmc_bmsequipment, open fmc_/pages/BmsEventDemo.html, and open the entity lists. Query only counts and the first small page of large tables; never enumerate all elastic readings. Use Fluent UI V9 with responsive cards, loading, empty, error and refresh states. | fmc_bmsbuilding, fmc_bmsequipment, fmc_bmspoint, fmc_bmsreading, cr3c8_silvernewbmspoint, fmc_syncrequest, fmc_spofile, fmc_spoimportrow | — | built (`trung-tam-van-hanh.tsx`) |

### Forms

| Form | Table | Type | Layout | Sub-grids |
|---|---|---|---|---|
| Tòa nhà BMS - Demo | fmc_bmsbuilding | Main | explicit (1 tab) | fmc_bmsequipment |
| Tạo nhanh tòa nhà BMS | fmc_bmsbuilding | QuickCreate | explicit (1 tab) | — |
| Thiết bị BMS - Demo | fmc_bmsequipment | Main | explicit (1 tab) | fmc_bmspoint |
| Tạo nhanh thiết bị BMS | fmc_bmsequipment | QuickCreate | explicit (1 tab) | — |
| Điểm BMS - Demo | fmc_bmspoint | Main | explicit (1 tab) | — |
| Yêu cầu đồng bộ - Demo | fmc_syncrequest | Main | explicit (1 tab) | — |
| File SharePoint - Demo | fmc_spofile | Main | explicit (1 tab) | fmc_spoimportrow |
| Dòng import SharePoint - Demo | fmc_spoimportrow | Main | explicit (1 tab) | — |

### Views

| View | Table | Columns | Filters | Sort |
|---|---|---|---|---|
| Tòa nhà BMS | fmc_bmsbuilding | fmc_name, fmc_buildingcode, fmc_sourcebuilding, modifiedon | — | fmc_name asc |
| Thiết bị BMS | fmc_bmsequipment | fmc_name, fmc_equipmentcode, fmc_equipmenttype, fmc_buildingid, modifiedon | — | fmc_name asc |
| Điểm BMS hiện tại | fmc_bmspoint | fmc_name, fmc_objectid, fmc_building, fmc_equipmentid, fmc_currentvalue, fmc_unit, fmc_lastreadingtime | — | fmc_lastreadingtime desc |
| Lịch sử Bronze gần đây | fmc_bmsreading | fmc_objectname, fmc_objectid, fmc_building, fmc_readingvalue, fmc_unit, fmc_readingtime, fmc_sourcesystem | all records | fmc_readingtime desc |
| Dữ liệu Silver đã chuẩn hóa | cr3c8_silvernewbmspoint | cr3c8_fmc_name, cr3c8_fmc_objectid, cr3c8_fmc_building, cr3c8_fmc_valueconverted, cr3c8_fmc_unitconverted, cr3c8_fmc_warningflag, cr3c8_fmc_lastreadingtime | — | modifiedon desc |
| Cảnh báo chất lượng Silver | cr3c8_silvernewbmspoint | cr3c8_fmc_name, cr3c8_fmc_objectid, cr3c8_fmc_valueconverted, cr3c8_fmc_unitconverted, cr3c8_fmc_warningflag | cr3c8_fmc_warningflag eq Flagged | modifiedon desc |
| Yêu cầu đồng bộ gần đây | fmc_syncrequest | fmc_name, fmc_status, fmc_requestedby, fmc_requestedcutoffid, fmc_deliveredrows, fmc_pendingafter, fmc_quarantinedrows, fmc_completedat | — | createdon desc |
| Yêu cầu đang xử lý | fmc_syncrequest | fmc_name, fmc_status, fmc_requestedby, fmc_attempts, fmc_startedat, modifiedon | fmc_status in Queued/Running | createdon asc |
| File SharePoint gần đây | fmc_spofile | fmc_filename, fmc_status, fmc_importstatus, fmc_filesize, fmc_rowcount, fmc_processedat | — | createdon desc |
| Dòng import SharePoint | fmc_spoimportrow | fmc_name, fmc_ordinal, fmc_code, fmc_buildingname, fmc_floorcount, fmc_sourceetag | — | fmc_ordinal asc |

## Navigation

- **FMC BMS Demo**
  - Tổng quan
    - Trung tâm vận hành → page `trung-tam-van-hanh`
    - Đồng bộ dữ liệu → URL $webresource:fmc_/pages/BmsEventDemo.html
  - Nhập liệu
    - Tòa nhà → table `fmc_bmsbuilding` — icon: the table's own
    - Thiết bị → table `fmc_bmsequipment` — icon: the table's own
  - Dữ liệu Bronze
    - Điểm hiện tại → table `fmc_bmspoint` — icon: the table's own
    - Lịch sử readings → table `fmc_bmsreading` — icon: the table's own
  - Dữ liệu Silver
    - Điểm đã chuẩn hóa → table `cr3c8_silvernewbmspoint` — icon: the table's own
  - SharePoint
    - File đã tiếp nhận → table `fmc_spofile` — icon: the table's own
    - Dòng đã parse → table `fmc_spoimportrow` — icon: the table's own
  - Vận hành
    - Yêu cầu đồng bộ → table `fmc_syncrequest` — icon: the table's own
    - Lịch sử Power Automate → URL https://make.powerautomate.com/environments/5abcb0e5-99b2-e51f-aa0e-90d84405798b/flows/d13bfe37-ca87-412b-9531-d3ed381b5b21/details

## Security

### Role: FMC BMS Demo Operator

The app is granted to this role, so it opens for this persona.

| Table | Access | Scope |
|---|---|---|
| fmc_bmsbuilding | read, create, write, append, appendTo | organization |
| fmc_bmsequipment | read, create, write, append, appendTo | organization |
| fmc_bmspoint | read, appendTo | organization |
| fmc_bmsreading | read | organization |
| cr3c8_silvernewbmspoint | read | organization |
| fmc_syncrequest | read, create | organization |
| fmc_spofile | read | organization |
| fmc_spoimportrow | read | organization |

### Role: FMC BMS Demo Viewer

The app is granted to this role, so it opens for this persona.

| Table | Access | Scope |
|---|---|---|
| fmc_bmsbuilding | read | organization |
| fmc_bmsequipment | read | organization |
| fmc_bmspoint | read | organization |
| fmc_bmsreading | read | organization |
| cr3c8_silvernewbmspoint | read | organization |
| fmc_syncrequest | read | organization |
| fmc_spofile | read | organization |
| fmc_spoimportrow | read | organization |

## Design contract

| Token | Value |
|---|---|
| accentColor | #0f6cbd |
| density | comfortable |
| cornerRadius | medium |
| darkMode | system |
| layout | cards |

These tokens are threaded to every generative page so the app looks consistent.
