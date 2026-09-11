# Custom API yêu cầu SQL-to-Dataverse sync

Trạng thái: **Đã implement và deploy trên Developer**
Ngày triển khai: 2026-09-11
Environment: `5abcb0e5-99b2-e51f-aa0e-90d84405798b`
Organization: `ab191700-b99e-f111-aaa0-000d3a80bb96`
Solution: `FMCentralBms`

## 1. Kết quả

Model-driven app `FMC BMS Demo` có trang **Power Automate Demo**. Người dùng chỉ
cần bấm **Request BMS Sync**; không có ô `Reason` và không phải tự tạo
`fmc_syncrequest`.

```text
Button trong app
  -> POST fmc_RequestBmsSync
  -> RequestBmsSync plug-in
  -> tạo hoặc reuse fmc_syncrequest
  -> trả RequestId/Created/Status/Message
  -> phát Dataverse business event
  -> Power Automate flow nhận event
```

Custom API không kết nối SQL. `DataverseSyncWorker` vẫn là thành phần duy nhất
claim request, đọc `FM_Central`, ghi current state/history vào Dataverse và
acknowledge delivery ledger.

## 2. Contract đã triển khai

### Custom API

| Property | Giá trị |
| --- | --- |
| Display/Unique name | `Request BMS Sync` / `fmc_RequestBmsSync` |
| Binding | Global, unbound |
| Type | Action, không phải Function |
| Private | `false` |
| Workflow enabled | `true` |
| Allowed custom processing step | `Async Only` |
| Main plug-in | `FMCentralBms.Plugins.RequestBmsSync` |
| Execute privilege | `prvCreatefmc_syncrequest` |

`Async Only` cho phép Custom API được dùng như business event. Main Operation
plug-in vẫn chạy đồng bộ để app nhận ngay kết quả enqueue; Power Automate nhận
event bất đồng bộ sau khi action hoàn tất.

### Input

| Name | Type | Required | Ý nghĩa |
| --- | --- | --- | --- |
| `ClientRequestId` | String GUID | No | Mã riêng cho một lần bấm/retry |

Không có parameter `Reason` theo yêu cầu demo.

### Output

| Name | Type | Ý nghĩa |
| --- | --- | --- |
| `RequestId` | Guid | ID của `fmc_syncrequest` được tạo/reuse |
| `Created` | Boolean | `true` nếu tạo mới; `false` nếu reuse |
| `Status` | Integer | Status hiện tại, ví dụ Queued `789100000` |
| `Message` | String | Kết quả dễ đọc cho app/caller |

## 3. Chống request active trùng nhau

`fmc_syncrequest` được thêm:

| Thành phần | Giá trị |
| --- | --- |
| Column | `fmc_activekey`, Text 200, optional |
| Alternate key | `fmc_syncrequest_activekey` |
| Giá trị lúc active | `<OrganizationId>:<SourceId>` |

Ví dụ pipeline hiện tại:

```text
ab191700-b99e-f111-aaa0-000d3a80bb96:FMC
```

Quy tắc:

1. Nếu `ClientRequestId` đã nằm trong `fmc_correlationid`, trả request đó.
2. Nếu pipeline đã có request `Queued` hoặc `Running`, reuse request active.
3. Nếu chưa có, tạo request Queued với `fmc_activekey = pipeline`.
4. Nếu hai caller create đồng thời, alternate key chọn một winner; caller còn
   lại đọc winner và trả `Created=false`.
5. Khi request sang trạng thái terminal, worker clear `fmc_activekey` để lần sau
   có thể tạo request mới mà vẫn giữ history cũ.

Lưu ý: nếu một `ClientRequestId` reuse request active do caller khác tạo thì ID
đó không được lưu thành correlation thứ hai. Retry trong khi request còn active
vẫn reuse đúng; retry sau khi request đó đã terminal có thể tạo request mới.

## 4. Business event và Power Automate

Custom API được đưa vào catalog:

```text
fmc_BmsDemoEvents
└── fmc_BmsAppEvents
    └── fmc_RequestBmsSync
```

Flow `FMC - App Request BMS Sync Event` dùng Microsoft Dataverse trigger
**When an action is performed** (`BusinessEventsTrigger`) với:

```json
{
  "catalog": "eef0e546-6cbf-49a7-acc3-0d6b1c3f940f",
  "category": "31f00635-c05c-4de8-9619-f63fef619d9b",
  "subscriptionRequest/entityname": "none",
  "subscriptionRequest/sdkmessagename": "fmc_RequestBmsSync"
}
```

Điểm dễ sai khi tạo flow bằng JSON: `catalog/category` ở root nhưng table/action
phải có prefix `subscriptionRequest/`. Connector docs hiển thị tên rút gọn; JSON
export thực tế dùng contract ở trên.

Flow hiện có một action `Demo_received` (Compose) để chứng minh event đã tới.
Flow không thực hiện SQL sync; worker xử lý request độc lập.

## 5. Thành phần source

| File | Vai trò |
| --- | --- |
| `plugins/FMCentralBms.Plugins/RequestBmsSync.cs` | Main Operation plug-in |
| `DataverseSyncWorker/Services/DataversePluginProvisioner.cs` | Register assembly/type và provision Custom API contract |
| `DataverseSyncWorker/Services/DataverseProvisioner.cs` | Provision `fmc_activekey` và alternate key |
| `DataverseSyncWorker/Services/SyncRequestStore.cs` | Enqueue/complete theo active-key contract |
| `dataverse/app-source/BmsEventDemo.html` | Source trang/nút trong app |
| `scripts/Invoke-BmsEventDemo.ps1` | Deploy, verify và test app/flow demo |
| `dataverse/FMCentralBms/customapis/` | Custom API đã export |
| `dataverse/FMCentralBms/catalogs/` | Business event catalogs đã export |
| `dataverse/FMCentralBms/Workflows/FMC-AppRequestBMSSyncEvent-*.json` | Flow đã export |

Assembly giữ nguyên strong name/public key token và tăng version lên `1.0.0.2`,
nên Dataverse update assembly hiện có thay vì tạo assembly trùng fullname.

## 6. Tracing

`RequestBmsSync` ghi các marker:

| Marker | Khi nào xuất hiện |
| --- | --- |
| `START` | Bắt đầu xử lý Custom API |
| `REUSE_BY_ID` | Correlation ID đã tồn tại |
| `REUSE_ACTIVE` | Pipeline đang có request active |
| `CREATED` | Tạo request mới |
| `RACE_REUSE` | Thua duplicate-key race và reuse winner |

Trace có pipeline, correlation và request ID; không có token, connection string
hay SQL payload.

## 7. Deployment đã thực hiện

Thứ tự đã dùng:

1. Build solution Release.
2. Provision column/key bằng `DataverseSyncWorker --provision`.
3. Register assembly `1.0.0.2`, plug-in type và Custom API bằng
   `--register-plugin`.
4. Tạo catalog/category/assignment, flow, web resource và sitemap bằng
   `Invoke-BmsEventDemo.ps1 -Mode Deploy`.
5. Publish và validate `FMC BMS Demo`.
6. Export/unpack solution và đồng bộ source vào `dataverse/FMCentralBms`.

Live IDs dùng để đối chiếu receipt, không được giả định là trạng thái của
environment khác:

| Component | ID |
| --- | --- |
| Custom API | `48a65cba-b0ad-f111-aaad-00224819a344` |
| Plug-in type | `44a65cba-b0ad-f111-aaad-00224819a344` |
| Flow | `d13bfe37-ca87-412b-9531-d3ed381b5b21` |
| Callback registration | `e2fd9b60-b2ad-f111-aaad-00224819a344` |
| App | `d19f4897-d227-4df3-8361-988f97c53e89` |

## 8. Kết quả kiểm thử

- `dotnet build MetasysPoc.sln -c Release`: pass, 0 warning/error.
- Custom API gọi lần đầu: `Created=true`, Queued.
- Retry cùng `ClientRequestId`: cùng `RequestId`, `Created=false`.
- Request demo: `671d5a8a-b2ad-f111-aaad-00224819a344`.
- Hai Power Automate run tương ứng: đều `Succeeded`.
- Callback registration trỏ tới `fmc_RequestBmsSync`, `entityname=none`.
- Alternate key `fmc_syncrequest_activekey`: `Active`.
- Flow: Active; app/web resource: publish và verify khớp source.
- Repacked unmanaged solution chứa Custom API, catalogs, flow, web resource,
  plug-in assembly và `fmc_syncrequest` schema.

Worker self-test đã pass phần logic độc lập. Phần SQL integration không chạy hết
trên máy hiện tại vì SQL Server yêu cầu encryption nhưng client báo không hỗ trợ;
đây là limitation local còn lại, không phải lỗi của Custom API/flow.

## 9. Rollback

1. Deactivate flow `FMC - App Request BMS Sync Event`.
2. Bỏ SubArea `fmc_eventdemo` khỏi sitemap nếu không muốn hiện trang demo.
3. Giữ column/key additive trong hot rollback; không xóa schema hoặc request
   history.
4. Không reset SQL ledger và không xóa queued request để “làm sạch”.

## 10. Tài liệu Microsoft

- [Create and use Custom APIs](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api)
- [Microsoft Dataverse business events](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/business-events)
- [Catalog and CatalogAssignment](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/catalog-catalogassignment)
- [When an action is performed](https://learn.microsoft.com/en-us/power-automate/dataverse/action-trigger)
