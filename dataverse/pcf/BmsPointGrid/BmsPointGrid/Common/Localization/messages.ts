/** Text nằm ngoài JSX để dễ đọc, dễ review và sẵn sàng chuyển sang resx khi cần host localization. */
export interface PointGridText {
    readonly title: string;
    readonly refresh: string;
    readonly previous: string;
    readonly next: string;
    readonly loading: string;
    readonly empty: string;
    readonly errorPrefix: string;
    readonly point: string;
    readonly objectId: string;
    readonly currentValue: string;
    readonly readingTime: string;
    readonly source: string;
    readonly freshness: Record<"fresh" | "stale" | "unknown", string>;
    readonly total: (count: number) => string;
    readonly rowAction: (name: string) => string;
    readonly assistant: string;
    readonly closeAssistant: string;
    readonly assistantNotConfigured: string;
}

const EN: PointGridText = {
    title: "Current BMS Points",
    refresh: "Refresh",
    previous: "Previous",
    next: "Next",
    loading: "Loading points…",
    empty: "No points are available in this view.",
    errorPrefix: "Unable to load points",
    point: "Point",
    objectId: "Object ID",
    currentValue: "Current value",
    readingTime: "Reading time",
    source: "Source",
    freshness: { fresh: "Current", stale: "Stale", unknown: "Unknown" },
    total: (count) => `${count} point(s)`,
    rowAction: (name) => `Open ${name}`,
    assistant: "BMS Assistant",
    closeAssistant: "Close assistant",
    assistantNotConfigured: "Configure the Copilot Studio Web Chat embed URL in this PCF control."
};

const VI: PointGridText = {
    title: "Điểm BMS hiện tại",
    refresh: "Làm mới",
    previous: "Trang trước",
    next: "Trang sau",
    loading: "Đang tải các điểm…",
    empty: "View này chưa có Point nào.",
    errorPrefix: "Không thể tải Point",
    point: "Point",
    objectId: "Object ID",
    currentValue: "Giá trị hiện tại",
    readingTime: "Thời điểm đọc",
    source: "Nguồn",
    freshness: { fresh: "Mới", stale: "Cũ", unknown: "Chưa rõ" },
    total: (count) => `${count} Point`,
    rowAction: (name) => `Mở ${name}`,
    assistant: "Trợ lý BMS",
    closeAssistant: "Đóng trợ lý",
    assistantNotConfigured: "Hãy cấu hình URL Web Chat của Copilot Studio trong PCF control này."
};

/** Model-driven app trả LCID 1066 cho Vietnamese; các ngôn ngữ khác fallback English. */
export function getText(languageId: number | undefined): PointGridText {
    return languageId === 1066 ? VI : EN;
}
