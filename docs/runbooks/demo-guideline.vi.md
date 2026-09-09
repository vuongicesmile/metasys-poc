# Guideline trình bày demo full flow BMS

Thời lượng gợi ý: **15–20 phút**.

## Chuẩn bị trước demo

1. Mở ba trang Swagger: Fake API `5100`, ingestion `5200`, worker `5300`.
2. Mở SQL query cho `raw.bms_building`, `raw.bms_equipment`, `raw.bms_reading`.
3. Mở solution `FMCentralBms` trong đúng Developer environment.
4. Chuẩn bị các table/view: `fmc_bmsbuilding`, `fmc_bmsequipment`, `fmc_bmspoint`, `fmc_syncrequest`.
5. Chạy `--verify-bms-relations` trước buổi demo và giữ output dự phòng.

## Phần 1 — Bài toán và mô hình, 2 phút

Nói: “Trước đây readings chỉ mang Building dạng text. Bản mở rộng bổ sung catalog Building và Equipment, rồi liên kết Point theo mô hình Building 1:N Equipment 1:N Point.”

Nhấn mạnh ba bảng nghiệp vụ chính; history vẫn nằm trong `fmc_bmsreading` theo thiết kế retention/TTL đã có.

## Phần 2 — Dữ liệu thật ở Fake API, 3 phút

Mở lần lượt:

- `/api/metasys/buildings`: chỉ ra 3 Building.
- `/api/metasys/equipment`: chỉ ra 4 Equipment và `buildingCode`.
- `/api/metasys/objects`: chỉ ra 5 Point và `equipmentCode`.

Nói: “Catalog và mapping nằm ở simulator, nên Fake API là nguồn dữ liệu demo. COV event cũng mang `equipmentCode`.”

## Phần 3 — Ingestion và ba bảng SQL, 3 phút

Mở `http://localhost:5200/api/ingestion/status`. Chỉ vào:

- `state = Listening`
- `buildingsUpserted = 3`
- `equipmentUpserted = 4`
- `eventsReceived` và `rowsInserted` đang tăng

Chạy:

```sql
SELECT * FROM raw.bms_building ORDER BY building_code;
SELECT * FROM raw.bms_equipment ORDER BY equipment_code;
SELECT TOP (10) object_id,equipment_code,reading_value,reading_time
FROM raw.bms_reading ORDER BY id DESC;
```

Nói: “Ingestion upsert hai catalog trong một SQL transaction, sau đó tiếp tục append COV vào bảng readings.”

## Phần 4 — Trigger sync, 4 phút

Chạy Power Automate `FMC - Request SQL to Dataverse Sync`, hoặc dùng CLI dự phòng:

```powershell
dotnet run --project .\DataverseSyncWorker -- --enqueue
dotnet run --project .\DataverseSyncWorker -- --process-command-once
```

Trong log, chỉ vào dòng `Catalog synchronized: 3 buildings, 4 equipment`, sau đó các batch readings. Mở `fmc_syncrequest` và chỉ trạng thái `Succeeded`.

Giải thích: một request có cutoff cố định; catalog được upsert theo GUID deterministic trước Equipment và Point; readings đến sau cutoff thuộc request kế tiếp.

## Phần 5 — Quan hệ trên Dataverse, 4 phút

1. Mở Building A, xem subgrid Equipment có Water Meter và Temperature Sensor.
2. Mở `EQ-TEST-RIG-001`, xem Building là Test Building.
3. Trong subgrid Points, chỉ ra hai Point test cùng thuộc TestRig.
4. Mở một Point, xem lookup Equipment và giá trị COV hiện tại.

Chạy kết luận kỹ thuật:

```powershell
dotnet run --project .\DataverseSyncWorker -- --verify-bms-relations
```

Đọc 5 dòng `PASS: live relationship ...` để chứng minh join thật trên Dataverse.

## Phần 6 — Migration và C# cho người quen Python, 2 phút

Nói: “`create-bms-tables.sql` giống schema migration; `seed-bms-relations.sql` giống idempotent data migration. Dataverse không chạy Alembic, nên `DataverseProvisioner.BmsRelations.cs` kiểm tra metadata rồi tạo phần thiếu bằng SDK.”

Chỉ bốn file chính:

- `MetasysPointStore.cs`: fixture nguồn.
- `BmsReadingRepository.cs`: ghi SQL.
- `SyncEngine.cs`: thứ tự catalog rồi readings.
- `ReadingMapper.cs`: tạo payload và GUID/lookup deterministic.

## Kết quả đã kiểm chứng ngày 2026-09-09

- Build solution: pass.
- Self-test SQL tạm và simulated Dataverse: pass.
- Fake API: 3 Building, 4 Equipment, 5 Point.
- Ingestion: `Listening`, catalog 3/4, COV có `equipmentCode`.
- SQL: 3 Building, 4 Equipment, 5 Point mapped.
- Sync request kiểm chứng: `c99f3d86-1fac-f111-aaad-00224819a344`, trạng thái `Succeeded`, cutoff SQL `23377`.
- Live Dataverse: 3 Building, 4 Equipment và cả 5 join Point → Equipment → Building pass.

Nếu ingestion vẫn chạy, `PendingRows` tăng sau cutoff là bình thường. Tập trung vào request vừa demo phải `Succeeded` và `DeadLetterRows = 0`.
