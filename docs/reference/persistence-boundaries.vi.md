# Persistence boundaries và DI

Refactor local dùng các boundary khác nhau theo cơ chế lưu dữ liệu:

| Service | Boundary | Lifetime |
| --- | --- | --- |
| BMS.Ingestion | Catalog/readings stage cùng DbContext; Unit of Work commit bằng SaveChangesAsync | Factory singleton, UoW/repositories scoped theo catalog hoặc event |
| BMS.Fake | IFakeUnitOfWork giữ context và transaction đọc–đổi point; trả COV sau commit | Factory singleton, mỗi update một UoW |
| Dataverse.SyncWorker | SqlStore giữ transaction/app lock và delivery ledger; remote writes được acknowledge theo contract hiện có | DataAccess registrations tập trung trong AddSyncDataAccess |
| SPO.Ingestion | Blob job lease, receipt và Dataverse writes giữ retry riêng | AddSpoDataAccess đăng ký ISpoJobStore và BlobJobStore cùng instance; DI sở hữu ServiceClient |
| Dataverse.Plugin | Transaction và IServiceProvider do Dataverse cung cấp | Services được resolve trong execution context của plugin |

Fake read-only queries vẫn dùng DbContext factory. Dispose UoW giải phóng transaction
và context cả khi lỗi; bỏ UoW chưa commit không lưu thay đổi. EF entities nằm trong
DataAccess; IFakeUnitOfWork là abstraction nội bộ tầng persistence, không truyền
DbContext sang Business.

Không có transaction chung giữa SQL, Blob Storage và Dataverse. UoW ở SQL không
thay thế deterministic identity, delivery ledger, lease hay receipts của remote writes.
Schema, timestamp mapping và retention không thay đổi trong refactor này.

Kiểm tra local: build solution với `-p:SignAssembly=false` và chạy MetasysPoc.Tests.
Test InMemory xác nhận stage/discard nhưng không chứng minh isolation/rollback của SQL Server.
Build không ký chỉ dùng kiểm tra source, không dùng DLL đó để deploy plugin.
