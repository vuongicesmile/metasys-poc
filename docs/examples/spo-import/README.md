# Fixtures cho SPO JSON import

Các file trong thư mục này không chứa dữ liệu thật. Chúng dùng để kiểm tra phase JSON import của
`FMC - Archive SPO File` sau khi file đã được archive thành công vào `fmc_spofile.fmc_file`.

| File | Kết quả mong đợi |
| --- | --- |
| `buildings-v1.json` | `Archived` + `Imported`, tạo/cập nhật đúng 2 `fmc_spoimportrow` |
| `invalid-schema-version.json` | Archive giữ nguyên; import `Failed` vì schemaVersion khác 1 |
| `invalid-missing-name.json` | Archive giữ nguyên; import `Failed` trước khi ghi row |
| `invalid-blank-name.json` | Archive giữ nguyên; import `Failed` vì name rỗng sau trim |
| `invalid-floor-count.json` | Archive giữ nguyên; import `Failed` vì floorCount ngoài 0..200 |
| `invalid-duplicate-code.json` | Archive giữ nguyên; import `Failed` vì code trùng sau trim + uppercase |
| `invalid-over-100-items.json` | Archive giữ nguyên; import `Failed` vì items vượt giới hạn 100 |

Với file hợp lệ, key của row là file lookup + ETag + ordinal. Chạy lại cùng version phải giữ nguyên
GUID và tổng row count. Consumer chỉ đọc các row có `fmc_sourceetag` bằng `fmc_importedetag` của
parent và parent đang có `fmc_importstatus = Imported`.
