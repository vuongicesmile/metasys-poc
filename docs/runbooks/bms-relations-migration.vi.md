# Chạy full flow Building → Equipment → Point

Chạy các lệnh từ `D:\metasys-poc`.

## Cách hiểu nếu quen Python

| Thành phần | Tương đương quen thuộc trong Python |
| --- | --- |
| `sql/create-bms-tables.sql` | schema migration |
| `sql/seed-bms-relations.sql` | idempotent data migration/fixture |
| `DataverseProvisioner.BmsRelations.cs` | migration Dataverse bằng SDK |
| record trong `Models` | `dataclass`/Pydantic model |
| `BmsReadingRepository` | repository dùng SQL driver |
| `ReadingMapper` | serializer/mapper sang payload Dataverse |
| `SyncEngine` | service chạy transaction workflow |
| `Program.cs` | CLI/web entry point |

C# không dùng Alembic/Django migration trong POC này. SQL schema dùng script có `IF ... IS NULL`; Dataverse schema dùng provisioner kiểm tra metadata trước khi tạo. Cả hai đều có thể chạy lại.

## 1. Migrate và seed SQL

```powershell
sqlcmd -S localhost -E -C -b -i .\sql\create-bms-tables.sql
sqlcmd -S localhost -E -C -b -i .\sql\create-dataverse-sync-tables.sql
sqlcmd -S localhost -E -C -b -i .\sql\seed-bms-relations.sql
```

Seed SQL là fixture dự phòng trùng với Fake API. Luồng runtime chuẩn vẫn để ingestion lấy catalog từ Fake API và upsert vào SQL.

## 2. Chạy Fake API và ingestion

Mở hai terminal:

```powershell
dotnet run --project .\FakeMetasysApi
```

```powershell
dotnet run --project .\BmsIngestionApp
```

Kiểm tra:

```powershell
Invoke-RestMethod http://localhost:5100/api/metasys/buildings
Invoke-RestMethod http://localhost:5100/api/metasys/equipment
Invoke-RestMethod http://localhost:5100/api/metasys/objects
Invoke-RestMethod http://localhost:5200/api/ingestion/status
```

Kết quả mong đợi: 3 Building, 4 Equipment, 5 Point; status `Listening`, `buildingsUpserted=3`, `equipmentUpserted=4`.

## 3. Provision Dataverse

```powershell
.\scripts\Invoke-BmsRelations.ps1 -Mode Provision
```

Lệnh kiểm tra đúng Developer organization, tạo/kiểm tra tables, columns, keys, relationships, forms, views và quyền role `FM Central BMS Integration`.

## 4. Trigger một full sync

Cách demo tương đương Power Automate:

```powershell
dotnet run --project .\DataverseSyncWorker -- --enqueue
dotnet run --project .\DataverseSyncWorker -- --process-command-once
```

Trong demo nghiệp vụ, thay `--enqueue` bằng flow `FMC - Request SQL to Dataverse Sync`, và giữ command worker chạy bằng `START-SQL-TO-DATAVERSE.cmd`.

Một request sẽ upsert catalog trước, rồi drain readings đến cutoff. COV đến sau cutoff được delivery ledger giữ cho request kế tiếp.

## 5. Verify

```powershell
dotnet run --project .\DataverseSyncWorker -- --verify-bms-relations
dotnet run --project .\DataverseSyncWorker -- --verify
```

Lệnh đầu kiểm tra 5 join Point → Equipment → Building từ mapping SQL. Lệnh sau kiểm tra catalog và tối đa 25 history sample với SQL. `PendingRows` có thể tăng khi ingestion vẫn chạy; `DeadLetterRows` phải bằng 0.

## 6. Test local

```powershell
dotnet build .\MetasysPoc.sln
dotnet run --project .\DataverseSyncWorker -- --self-test
```

Self-test tạo database SQL có tên ngẫu nhiên, dùng simulated Dataverse sink và tự xóa database sau khi hoàn tất.
