# BMS Ingestion Refactor Lab

Trang thực hành local cho kế hoạch tách `BMS.Ingestion.App` thành Presentation,
Business, Common, Metasys adapter và SQL adapter. Trang không gọi SQL, Dataverse
hoặc dịch vụ cloud.

```powershell
npm --prefix .\docs\interactive\bms-ingestion-refactor-lab test
npm --prefix .\docs\interactive\bms-ingestion-refactor-lab run test:browser
npm --prefix .\docs\interactive\bms-ingestion-refactor-lab start
```

Mở `http://127.0.0.1:5402`. Plan Markdown đầy đủ nằm tại
`docs/plans/bms-ingestion-layered-refactor.vi.md`. Checkbox chỉ lưu trong
`localStorage` của browser và không thay đổi source.
