# Template dữ liệu SPO ingestion

Thư mục này chỉ có các template nhỏ để copy, chỉnh sửa và upload lên SharePoint:

| File | Mục đích |
| --- | --- |
| `templates/bms-catalog-template.json` | Building, Equipment và Point → Equipment mapping |
| `templates/bms-readings-template.csv` | Reading CSV, đồng thời cập nhật Point current state |
| `templates/bms-readings-template.json` | Cùng contract Reading ở định dạng JSON |
| `templates/sharepoint-list-template.csv` | Header/row mẫu để tạo hoặc seed SharePoint List |

Quy tắc:

- Giữ `sourceId=FMC` cho catalog để tương thích deterministic identity hiện tại.
- Business code dùng uppercase ASCII, số, `-` hoặc `_`.
- `equipmentType` hiện chỉ nhận `WaterMeter`, `TemperatureSensor`, `TestRig`.
- `sourceReadingId` phải ổn định và duy nhất với event sinh từ SPO.
- `readingTime` dùng ISO 8601 UTC; decimal dùng dấu chấm và tối đa bốn số lẻ.
- Không dùng SharePoint Item ID hoặc row ordinal làm `sourceReadingId`.

Sau khi multi-file service trong
[plan](../../plans/spo-cloud-ingestion-service.vi.md) được implement, upload theo
mapping/folder đã cấu hình. Hiện tại manual flow chỉ tìm file
`/Shared Documents/employee_bronze_profiling_demo_full.csv` và chỉ archive binary;
muốn thử archive ngay, copy/rename CSV template thành đúng tên đó tại root
`Shared Documents`. Parser và automatic upload trigger chưa được triển khai.
