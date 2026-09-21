# Persistence boundaries và DI

Refactor local dùng các boundary khác nhau theo cơ chế lưu dữ liệu:

| Service | Boundary | Lifetime |
| --- | --- | --- |
| BMS.Ingestion | Catalog/readings stage cùng DbContext; Unit of Work commit bằng SaveChangesAsync | Factory singleton, UoW/repositories scoped theo catalog hoặc event |
| BMS.Fake | IFakeUnitOfWork giữ context và transaction đọc–đổi point; trả COV sau commit | Factory singleton, mỗi update một UoW |
| Dataverse.SyncWorker | SyncEngine ở Business dùng ISyncBatchUnitOfWork; SQL implementation giữ connection/app lock và per-row Ack/quarantine | Factory singleton; một session cho mỗi batch; Dispose luôn Unlock rồi đóng connection |
| SPO.Ingestion | ISpoJobUnitOfWork giữ lease, ràng buộc manifest/receipts vào một job; checkpoints giữ retry riêng | Factory singleton; session ngắn hạn theo job; CLI và Functions dùng chung AddSpoProcessing |
| Dataverse.Plugin | Transaction và IServiceProvider do Dataverse cung cấp | Services được resolve trong execution context của plugin |

Fake read-only queries vẫn dùng DbContext factory. Dispose UoW giải phóng transaction
và context cả khi lỗi; bỏ UoW chưa commit không lưu thay đổi. EF entities nằm trong
DataAccess; IFakeUnitOfWork là abstraction nội bộ tầng persistence, không truyền
DbContext sang Business.

Không có transaction chung giữa SQL, Blob Storage và Dataverse. UoW ở SQL không
thay thế deterministic identity, delivery ledger, lease hay receipts của remote writes.
Schema, timestamp mapping và retention không thay đổi trong refactor này.

`SyncEngine` không tham chiếu SqlClient/DataAccess. Batch session trả null khi lock
bận; semaphore vẫn serialize trong process. Ack từng row chỉ chạy sau remote writes.
`SpoJobProcessor` dispose lease qua job UoW khi hoàn tất hoặc lỗi. Các receipt đã ghi
vẫn bền vững sau partial failure; UoW không giả lập rollback Dataverse/Blob.

SPO CLI resolve parser, mapper, validator, local processor và inbox processor qua DI.
Preview chỉ resolve dịch vụ đọc file; không khởi tạo Dataverse connection. Connection
sở hữu SDK client dùng chung cho writer/inbox; provider được dispose khi command kết thúc.
Đường dẫn appsettings mặc định theo layout `Dataverse.SyncWorker/Dataverse.SyncWorker.App`.
SyncWorker maintenance cũng dispose host khi thoát; relation seeder được resolve qua DI.

Kiểm tra local: build solution với `-p:SignAssembly=false` và chạy MetasysPoc.Tests.
Test InMemory xác nhận stage/discard nhưng không chứng minh isolation/rollback của SQL Server.
Build không ký chỉ dùng kiểm tra source, không dùng DLL đó để deploy plugin.

Kiểm tra refactor Dataverse/SPO ngày 2026-09-21: 50 unit test pass; SQL self-test
pass với sink giả và database tạm đã được xóa. CLI preview chạy qua DI trên dữ liệu
mẫu: 5 nguồn Ready, equipment có 1.789 lỗi SPO-CHOICE do cấu hình thiếu equipment
types, 3 nguồn DisabledContract. Vì vậy preview trả exit code 1; chưa xác minh ghi
Dataverse/Blob live trong lần refactor này.
