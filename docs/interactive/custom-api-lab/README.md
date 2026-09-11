# Custom API interactive lab

Lab sinh nội dung từ source thật của `fmc_RequestBmsSync`; không gọi Dataverse.

```powershell
cd D:\metasys-poc\docs\interactive\custom-api-lab
npm install
npm run build
npm test
npm start
```

Mở `http://127.0.0.1:5401`. Có thể mở `index.html` trực tiếp, nhưng chạy local server
cho trải nghiệm nhất quán hơn.

Sau khi sửa handler hoặc runbook, luôn chạy lại:

```powershell
npm run build
npm test
npm run test:browser
```

`content.js` là file generated và được commit để `index.html` dùng được ngay. Không sửa
file đó bằng tay. Build chỉ đọc whitelist trong `build.mjs`; `.snk`, `appsettings.json`,
token và secrets không được nhúng vào handbook.
