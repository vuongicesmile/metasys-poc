# Point Issue — React Custom API CRUD Handbook

Tài liệu React tiếng Việt hướng dẫn **tự implement** Point Issue Tracker trong
FMC BMS Demo hiện có. Code minh họa nằm trong nội dung docs, không phải feature
đã được đưa vào app hoặc deploy Dataverse. CRUD playground chỉ dùng bộ nhớ trình
duyệt; tiến độ đọc lưu localStorage. Server không có API ghi dữ liệu hoặc quyền
đọc toàn bộ repository.

Từ `D:\metasys-poc`:

```powershell
cd docs/interactive/custom-api-crud-guide
npm ci
npm run build
npm start
```

Mở <http://127.0.0.1:5431/>. Đổi port bằng `$env:CRUD_GUIDE_PORT = '5433'`.
Không mở trực tiếp index.html bằng file://; React bundle cần build và HTTP server.

## Nội dung

10 chương: phạm vi, cấu trúc repo, schema, contract, C# service, plugin,
registration, React, build/deploy và kiểm thử. Có copy code, tìm kiếm,
đánh dấu tiến độ, sơ đồ luồng, CRUD sandbox và hướng dẫn lỗi thường gặp.

`src/snippets.ts` chứa code đề xuất để người học tự tạo theo đúng đường dẫn;
`src/content.ts` chứa bài học; `src/main.tsx` là UI tài liệu. Không sửa code app
thật khi chỉnh các snippet này. Các mẫu là bài tập tích hợp, không phải một
gói production đã compile/deploy. Đọc đầy đủ checkpoint và giới hạn từng bài.

## Kiểm tra UI

```powershell
npm test
```

Test dùng Playwright đã cài tại `dataverse/app-source/fmc-bms-demo/node_modules`
và Microsoft Edge; nếu thiếu, chạy `npm ci` ở thư mục app demo trước. Test khởi
động server riêng port 5432, kiểm tra desktop/mobile, navigation, copy, progress,
CRUD simulation, lỗi JS và chụp ảnh vào `.artifacts/browser-qa`.

Không gọi Dataverse, SQL, Power Automate, không cần token. Toàn bộ server chỉ bind
loopback; không dùng server tài liệu này để host app production.
