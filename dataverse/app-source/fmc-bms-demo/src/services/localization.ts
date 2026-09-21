

export type Language = "en" | "vi";
// Khóa lưu preference ngôn ngữ dùng chung với web resource demo.
export const LANGUAGE_KEY = "fmc.bms.language";
export const englishMessages: Record<string, string> = {
    "Bắt đầu": "Started",
    "Bốn điểm kiểm soát của luồng dữ liệu trong bản demo.": "Four checkpoints in the demo data pipeline.",
    "Chi tiết tệp SharePoint gần nhất": "Latest SharePoint file details",
    "Chuẩn hóa": "Normalize",
    "Chưa có": "Not available",
    "Chưa có dữ liệu BMS": "No BMS data yet",
    "Chưa xác định": "Unknown",
    "Chưa yêu cầu": "Not requested",
    "Chưa đặt tên": "Untitled",
    "Chỉ đọc trang đầu có giới hạn; dấu “+” cho biết còn bản ghi phía sau.": "Counts show a limited first page; “+” means more records are available.",
    "Các bước xử lý Bronze sang Silver": "Bronze to Silver processing steps",
    "Danh mục vận hành": "Operations catalog",
    "Danh sách tệp SharePoint": "SharePoint files",
    "Danh sách điểm BMS": "BMS points",
    "Danh sách đồng bộ": "Sync requests",
    "Dòng Silver": "Silver rows",
    "Dòng catalog đã thấy": "Catalog rows found",
    "Dữ liệu đã chuẩn hóa": "Normalized data",
    "Giá trị BMS mới nhất": "Latest BMS values",
    "Giá trị hiện tại": "Current value",
    "Giới hạn": "Limited",
    "Giới hạn 25 dòng đầu": "First 25 rows only",
    "Giữ dữ liệu nguồn": "Preserve source data",
    "Hoạt động mới nhất": "Recent activity",
    "Hãy thêm tòa nhà, thiết bị hoặc chạy luồng demo để bắt đầu.": "Add a building or equipment, or run the demo pipeline to get started.",
    "Không có tên": "Untitled",
    "Không có tệp phù hợp": "No matching files",
    "Không có yêu cầu phù hợp": "No matching requests",
    "Không có điểm phù hợp": "No matching points",
    "Không thể mở mục đã chọn trong Power Apps. Vui lòng thử lại.": "Unable to open this item in Power Apps. Please try again.",
    "Không thể tải dữ liệu vận hành từ Dataverse. Hãy kiểm tra quyền truy cập rồi thử lại.": "Unable to load operations data from Dataverse. Check your access and try again.",
    "Không tải được dữ liệu": "Unable to load data",
    "Luồng Bronze → Silver": "Bronze → Silver pipeline",
    "Làm mới": "Refresh",
    "Làm mới dữ liệu vận hành": "Refresh operations data",
    "Lần đọc gần nhất": "Last reading",
    "Lọc hoạt động mới nhất": "Filter recent activity",
    "Lọc điểm đo, yêu cầu đồng bộ và tệp SharePoint": "Filter points, sync requests and SharePoint files",
    "Mở BMS Event Demo": "Open BMS Sync Demo",
    "Mở biểu mẫu, trang demo hoặc danh sách Dataverse trong ứng dụng.": "Open forms, the sync demo or Dataverse lists in this app.",
    "Người dùng Microsoft Entra": "Microsoft Entra user",
    "Người dùng hiện tại": "Signed-in user",
    "Nhập catalog": "Catalog import",
    "Năm yêu cầu đồng bộ mới nhất": "Five latest sync requests",
    "Năm điểm Bronze mới nhất": "Five latest Bronze points",
    "Reading Bronze lưu giữ": "Retained Bronze readings",
    "Sẵn sàng khai thác": "Ready for analysis",
    "Số dòng khai báo": "Reported row count",
    "Sự kiện và reading": "Events and readings",
    "Thao tác nhanh": "Quick actions",
    "Theo dõi luồng dữ liệu từ Bronze sang Silver, yêu cầu đồng bộ SQL và tệp catalog tiếp nhận từ SharePoint.": "Monitor Bronze to Silver data, SQL sync requests and catalog files received from SharePoint.",
    "Thiết bị": "Equipment",
    "Thiết bị đã đăng ký": "Registered equipment",
    "Thu nhận BMS": "BMS ingestion",
    "Thêm thiết bị": "Add equipment",
    "Thêm tòa nhà": "Add building",
    "Thử lại": "Retry",
    "Thử xóa bộ lọc hoặc chạy dữ liệu demo.": "Clear the filter or run the demo data pipeline.",
    "Thử xóa bộ lọc hoặc chờ luồng tiếp nhận SharePoint.": "Clear the filter or wait for SharePoint ingestion.",
    "Thử xóa bộ lọc hoặc tạo yêu cầu đồng bộ mới.": "Clear the filter or create a new sync request.",
    "Tiếp nhận": "Archive status",
    "Toàn cảnh dữ liệu": "Data overview",
    "Trung tâm vận hành BMS": "BMS Operations Center1",
    "Trạng thái": "Status",
    "Tòa nhà": "Buildings",
    "Tệp": "File",
    "Tệp SharePoint": "SharePoint files",
    "Tệp SharePoint tiếp nhận gần nhất": "Latest SharePoint file",
    "Tối đa năm điểm Bronze, năm yêu cầu đồng bộ và một tệp SharePoint gần nhất.": "Up to five recent Bronze points, five sync requests and the latest SharePoint file.",
    "Yêu cầu": "Request",
    "Yêu cầu đồng bộ": "Sync requests",
    "Yêu cầu đồng bộ mới nhất": "Latest sync requests",
    "Đang làm mới": "Refreshing",
    "Đang tải dữ liệu vận hành…": "Loading operations data…",
    "Điểm Bronze hiện tại": "Current Bronze points",
    "Điểm Bronze mới nhất": "Latest Bronze points",
    "Điểm đo": "Point",
    "Đã chuyển": "Delivered",
    "Đã nhận lúc": "Received at",
    "Đã xử lý lúc": "Processed at",
    "Đường dẫn SharePoint": "SharePoint path",
    "Để đăng xuất, mở menu hồ sơ Power Apps ở góc trên bên phải rồi chọn “Đăng xuất”.": "To sign out, open your Power Apps profile menu at the top right and select “Sign out”.",
    "Đồng bộ và chuyển đổi": "Sync and transform",
    "Dòng catalog": "Catalog rows",
    "Ngôn ngữ": "Language",
    "Yêu cầu đồng bộ SQL": "SQL sync requests",
    "FMC · Vận hành BMS": "FMC · BMS operations"
};
// Đọc preference an toàn vì embedded host có thể chặn localStorage.
export function readLanguage(): Language {
    try { return window.localStorage.getItem(LANGUAGE_KEY) === "vi" ? "vi" : "en"; }
    catch { return "en"; }
}
// English map dùng key tiếng Việt để component giữ một bộ text duy nhất.
export function translate(text: string, language: Language): string {
    return language === "en" ? englishMessages[text] ?? text : text;
}
export const statusMessages: Record<string, Record<number, [string, string]>> = {
    sync: {789100000:["Queued","Đang chờ"],789100001:["Running","Đang chạy"],789100002:["Succeeded","Thành công"],789100003:["Completed with issues","Hoàn tất có lỗi"],789100004:["Failed","Thất bại"]},
    archive: {789110000:["Received","Đã nhận"],789110001:["Processing","Đang xử lý"],789110002:["Archived","Đã lưu"],789110003:["Failed","Thất bại"],789110004:["Ignored","Đã bỏ qua"]},
    import: {789111000:["Not requested","Chưa yêu cầu"],789111001:["Processing","Đang xử lý"],789111002:["Imported","Đã nhập"],789111003:["Failed","Thất bại"]}
};
// Đổi option-set value Dataverse thành label theo loại bảng và ngôn ngữ hiện tại.
export function statusLabel(kind: string, value: number | undefined, language: Language, fallback?: string): string {
    const labels = value === undefined ? undefined : statusMessages[kind]?.[value];
    return labels ? labels[language === "vi" ? 1 : 0] : fallback || translate("Chưa xác định", language);
}
// Format Date theo locale nhưng vẫn trả text thân thiện khi value thiếu/hỏng.
export function localizedDate(value: Date | undefined, language: Language): string {
    const t = (text: string) => translate(text, language);
    if (!value) return t("Chưa có");
    const parsed = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(parsed.getTime())) return t("Chưa có");
    return new Intl.DateTimeFormat(language === "vi" ? "vi-VN" : "en-US", {
        dateStyle: "short",
        timeStyle: "short",
    }).format(parsed);
}

// Format số theo locale và giới hạn số chữ số thập phân hiển thị.
export function localizedNumber(value: number | undefined, language: Language, maximumFractionDigits = 2): string {
    if (typeof value !== "number" || !Number.isFinite(value)) return "—";
    return new Intl.NumberFormat(language === "vi" ? "vi-VN" : "en-US", { maximumFractionDigits }).format(value);
}
