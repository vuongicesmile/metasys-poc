import { createContext, useContext, useEffect, useState } from "react";
import type { ReactNode } from "react";
import type {
    GeneratedComponentProps,
    ReadableTableRow,
    cr3c8_silvernewbmspoint,
    fmc_bmsbuilding,
    fmc_bmsequipment,
    fmc_bmspoint,
    fmc_bmsreading,
    fmc_spofile,
    fmc_spoimportrow,
    fmc_syncrequest,
} from "./RuntimeTypes";
import {
    Badge,
    Button,
    Card,
    DataGrid,
    DataGridBody,
    DataGridCell,
    DataGridHeader,
    DataGridHeaderCell,
    DataGridRow,
    SearchBox,
    Select,
    Spinner,
    TableCellLayout,
    Text,
    createTableColumn,
    makeStyles,
    tokens,
} from "@fluentui/react-components";
import {
    AddRegular,
    ArrowClockwiseRegular,
    ArrowSyncRegular,
    BuildingRegular,
    CheckmarkCircleRegular,
    ChevronRightRegular,
    DatabaseRegular,
    DesktopRegular,
    FolderRegular,
    OpenRegular,
    PersonRegular,
    PulseRegular,
    SearchRegular,
    TableRegular,
} from "@fluentui/react-icons";

type ReadableBuilding = ReadableTableRow<fmc_bmsbuilding>;
type ReadableEquipment = ReadableTableRow<fmc_bmsequipment>;
type ReadablePoint = ReadableTableRow<fmc_bmspoint>;
type ReadableReading = ReadableTableRow<fmc_bmsreading>;
type ReadableSilverPoint = ReadableTableRow<cr3c8_silvernewbmspoint>;
type ReadableSyncRequest = ReadableTableRow<fmc_syncrequest>;
type ReadableSpoFile = ReadableTableRow<fmc_spofile>;
type ReadableSpoImportRow = ReadableTableRow<fmc_spoimportrow>;

type BoundedCount = {
    observed: number;
    label: string;
    isBounded: boolean;
};

type DashboardData = {
    buildings: BoundedCount;
    equipment: BoundedCount;
    bronzePoints: BoundedCount;
    bronzeReadings: BoundedCount;
    silverRows: BoundedCount;
    syncRequests: BoundedCount;
    sharePointFiles: BoundedCount;
    importRows: BoundedCount;
    latestPoints: ReadablePoint[];
    latestSyncRequests: ReadableSyncRequest[];
    latestFiles: ReadableSpoFile[];
};

type DashboardState = {
    value: DashboardData | null;
    loading: boolean;
    error: string | null;
};

type NavigationApi = {
    Navigation?: {
        navigateTo?: (input: Record<string, unknown>, options?: { target?: 1 | 2 }) => Promise<unknown>;
    };
    Utility?: {
        getGlobalContext?: () => { userSettings?: { userName?: string } };
    };
};


type Language = "en" | "vi";
const LANGUAGE_KEY = "fmc.bms.language";
const englishMessages: Record<string, string> = {
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
    "Trung tâm vận hành BMS": "BMS Operations Center",
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
const LanguageContext = createContext<Language>("en");
function readLanguage(): Language {
    try { return window.localStorage.getItem(LANGUAGE_KEY) === "vi" ? "vi" : "en"; }
    catch { return "en"; }
}
function translate(text: string, language: Language): string {
    return language === "en" ? englishMessages[text] ?? text : text;
}
function useTranslation() {
    const language = useContext(LanguageContext);
    return (text: string) => translate(text, language);
}
const statusMessages: Record<string, Record<number, [string, string]>> = {
    sync: {789100000:["Queued","Đang chờ"],789100001:["Running","Đang chạy"],789100002:["Succeeded","Thành công"],789100003:["Completed with issues","Hoàn tất có lỗi"],789100004:["Failed","Thất bại"]},
    archive: {789110000:["Received","Đã nhận"],789110001:["Processing","Đang xử lý"],789110002:["Archived","Đã lưu"],789110003:["Failed","Thất bại"],789110004:["Ignored","Đã bỏ qua"]},
    import: {789111000:["Not requested","Chưa yêu cầu"],789111001:["Processing","Đang xử lý"],789111002:["Imported","Đã nhập"],789111003:["Failed","Thất bại"]}
};
function statusLabel(kind: string, value: number | undefined, language: Language, fallback?: string): string {
    const labels = value === undefined ? undefined : statusMessages[kind]?.[value];
    return labels ? labels[language === "vi" ? 1 : 0] : fallback || translate("Chưa xác định", language);
}

const CACHE_KEY = "__genpage_bms_operations_dashboard_v1";
const INFLIGHT_KEY = "__genpage_bms_operations_dashboard_inflight_v1";
const GENERATION_KEY = "__genpage_bms_operations_dashboard_generation_v1";
const winAny = window as unknown as Record<string, unknown>;

const MASTER_COUNT_LIMIT = 100;
const ELASTIC_COUNT_LIMIT = 25;
const LATEST_LIMIT = 5;

const useStyles = makeStyles({
    root: {
        position: "relative",
        contain: "layout",
        display: "flex",
        flexDirection: "column",
        width: "100%",
        height: "100%",
        overflowY: "auto",
        boxSizing: "border-box",
        color: tokens.colorNeutralForeground1,
        backgroundColor: tokens.colorNeutralBackground2,
    },
    content: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXL,
        width: "100%",
        maxWidth: "96rem",
        marginLeft: "auto",
        marginRight: "auto",
        padding: tokens.spacingHorizontalXXL,
        boxSizing: "border-box",
        "@media (max-width: 768px)": {
            padding: tokens.spacingHorizontalL,
            gap: tokens.spacingVerticalL,
        },
    },
    hero: {
        display: "flex",
        alignItems: "flex-start",
        justifyContent: "space-between",
        gap: tokens.spacingHorizontalXL,
        padding: tokens.spacingHorizontalXXL,
        borderRadius: tokens.borderRadiusXLarge,
        color: tokens.colorNeutralForegroundOnBrand,
        backgroundImage: `linear-gradient(135deg, ${tokens.colorBrandBackground}, ${tokens.colorPaletteTealForeground2})`,
        boxShadow: tokens.shadow16,
        "@media (max-width: 768px)": {
            flexDirection: "column",
            padding: tokens.spacingHorizontalXL,
        },
    },
    heroCopy: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        minWidth: 0,
    },
    eyebrow: {
        color: tokens.colorNeutralForegroundOnBrand,
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        letterSpacing: "0.08em",
        textTransform: "uppercase",
    },
    title: {
        margin: 0,
        color: tokens.colorNeutralForegroundOnBrand,
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeHero900,
        fontWeight: tokens.fontWeightSemibold,
        lineHeight: tokens.lineHeightHero900,
        "@media (max-width: 480px)": {
            fontSize: tokens.fontSizeHero700,
            lineHeight: tokens.lineHeightHero700,
        },
    },
    heroDescription: {
        maxWidth: "52rem",
        color: tokens.colorNeutralForegroundOnBrand,
        fontSize: tokens.fontSizeBase400,
        lineHeight: tokens.lineHeightBase400,
    },
    userPanel: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        minWidth: "16rem",
        padding: tokens.spacingHorizontalL,
        border: `${tokens.strokeWidthThin} solid rgba(255, 255, 255, 0.38)`,
        borderRadius: tokens.borderRadiusLarge,
        backgroundColor: "rgba(0, 0, 0, 0.14)",
        "@media (max-width: 768px)": {
            width: "100%",
            minWidth: 0,
            boxSizing: "border-box",
        },
    },
    userLine: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        color: tokens.colorNeutralForegroundOnBrand,
    },
    profileHint: {
        color: tokens.colorNeutralForegroundOnBrand,
        fontSize: tokens.fontSizeBase200,
        lineHeight: tokens.lineHeightBase200,
    },
    section: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
    },
    sectionHeader: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        gap: tokens.spacingHorizontalM,
        flexWrap: "wrap",
    },
    sectionHeading: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
    },
    sectionTitle: {
        margin: 0,
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeBase600,
        fontWeight: tokens.fontWeightSemibold,
        lineHeight: tokens.lineHeightBase600,
    },
    sectionDescription: {
        color: tokens.colorNeutralForeground2,
    },
    kpiGrid: {
        display: "grid",
        gridTemplateColumns: "repeat(auto-fit, minmax(11rem, 1fr))",
        gap: tokens.spacingHorizontalM,
    },
    kpiCard: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
        minHeight: "8.75rem",
        padding: tokens.spacingHorizontalL,
        border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
        borderRadius: tokens.borderRadiusLarge,
        backgroundColor: tokens.colorNeutralBackground1,
        boxShadow: tokens.shadow2,
    },
    kpiTop: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        gap: tokens.spacingHorizontalS,
    },
    kpiIcon: {
        display: "inline-flex",
        alignItems: "center",
        justifyContent: "center",
        width: "2rem",
        height: "2rem",
        borderRadius: tokens.borderRadiusCircular,
        color: tokens.colorBrandForeground1,
        backgroundColor: tokens.colorBrandBackground2,
    },
    kpiValue: {
        fontFamily: tokens.fontFamilyBase,
        fontSize: tokens.fontSizeHero800,
        fontWeight: tokens.fontWeightSemibold,
        lineHeight: tokens.lineHeightHero800,
        fontVariantNumeric: "tabular-nums",
    },
    kpiLabel: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase300,
    },
    countNote: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
    },
    pipeline: {
        display: "grid",
        gridTemplateColumns: "repeat(4, minmax(9rem, 1fr))",
        gap: tokens.spacingHorizontalS,
        overflowX: "auto",
        paddingBottom: tokens.spacingVerticalXS,
    },
    pipelineStep: {
        position: "relative",
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalM,
        minWidth: "9rem",
        padding: tokens.spacingHorizontalL,
        borderRadius: tokens.borderRadiusLarge,
        color: tokens.colorNeutralForeground1,
        backgroundColor: tokens.colorNeutralBackground1,
        border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    },
    pipelineIcon: {
        display: "inline-flex",
        alignItems: "center",
        justifyContent: "center",
        flexShrink: 0,
        width: "2.25rem",
        height: "2.25rem",
        borderRadius: tokens.borderRadiusCircular,
        color: tokens.colorBrandForeground1,
        backgroundColor: tokens.colorBrandBackground2,
    },
    pipelineCopy: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
        minWidth: 0,
    },
    pipelineLabel: {
        fontWeight: tokens.fontWeightSemibold,
    },
    pipelineDetail: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
    },
    pipelineArrow: {
        position: "absolute",
        right: `calc(-1 * ${tokens.spacingHorizontalS})`,
        zIndex: 1,
        color: tokens.colorNeutralForeground3,
        transform: "translateX(50%)",
    },
    actionGrid: {
        display: "grid",
        gridTemplateColumns: "repeat(auto-fit, minmax(13rem, 1fr))",
        gap: tokens.spacingHorizontalM,
    },
    actionButton: {
        justifyContent: "flex-start",
        minHeight: "2.75rem",
    },
    toolbar: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        flexWrap: "wrap",
    },
    search: {
        minWidth: "16rem",
        "@media (max-width: 480px)": {
            minWidth: "100%",
        },
    },
    message: {
        display: "flex",
        alignItems: "flex-start",
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingHorizontalL,
        borderRadius: tokens.borderRadiusMedium,
        color: tokens.colorStatusDangerForeground1,
        backgroundColor: tokens.colorStatusDangerBackground1,
        border: `${tokens.strokeWidthThin} solid ${tokens.colorStatusDangerBorder1}`,
    },
    loading: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        minHeight: "15rem",
        padding: tokens.spacingVerticalXXL,
        borderRadius: tokens.borderRadiusLarge,
        backgroundColor: tokens.colorNeutralBackground1,
    },
    empty: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        gap: tokens.spacingVerticalS,
        minHeight: "10rem",
        padding: tokens.spacingVerticalXXL,
        textAlign: "center",
        borderRadius: tokens.borderRadiusLarge,
        color: tokens.colorNeutralForeground2,
        backgroundColor: tokens.colorNeutralBackground1,
        border: `${tokens.strokeWidthThin} dashed ${tokens.colorNeutralStroke2}`,
    },
    dataCard: {
        overflow: "hidden",
        border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
        borderRadius: tokens.borderRadiusLarge,
        backgroundColor: tokens.colorNeutralBackground1,
    },
    dataCardHeader: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        gap: tokens.spacingHorizontalM,
        padding: tokens.spacingHorizontalL,
        borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    },
    dataCardTitle: {
        fontWeight: tokens.fontWeightSemibold,
    },
    gridScroller: {
        overflowX: "auto",
    },
    cellLayout: {
        minWidth: 0,
        overflow: "hidden",
    },
    truncate: {
        display: "block",
        minWidth: 0,
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
    },
    numeric: {
        fontFamily: tokens.fontFamilyBase,
        fontVariantNumeric: "tabular-nums",
    },
    latestReceipt: {
        display: "grid",
        gridTemplateColumns: "repeat(4, minmax(0, 1fr))",
        gap: tokens.spacingHorizontalL,
        padding: tokens.spacingHorizontalL,
        borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground3,
        "@media (max-width: 768px)": {
            gridTemplateColumns: "repeat(2, minmax(0, 1fr))",
        },
        "@media (max-width: 480px)": {
            gridTemplateColumns: "1fr",
        },
    },
    receiptField: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
        minWidth: 0,
    },
    receiptLabel: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
    },
});

function createBoundedCount(observed: number, hasMoreRows: boolean): BoundedCount {
    return {
        observed,
        label: hasMoreRows ? `${observed}+` : String(observed),
        isBounded: hasMoreRows,
    };
}

function localizedDate(value: Date | undefined, language: Language): string {
    const t = (text: string) => translate(text, language);
    if (!value) return t("Chưa có");
    const parsed = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(parsed.getTime())) return t("Chưa có");
    return new Intl.DateTimeFormat(language === "vi" ? "vi-VN" : "en-US", {
        dateStyle: "short",
        timeStyle: "short",
    }).format(parsed);
}

function localizedNumber(value: number | undefined, language: Language, maximumFractionDigits = 2): string {
    if (typeof value !== "number" || !Number.isFinite(value)) return "—";
    return new Intl.NumberFormat(language === "vi" ? "vi-VN" : "en-US", { maximumFractionDigits }).format(value);
}

function getFormattedValue(row: Record<string, unknown>, column: string): string | undefined {
    const value = row[`${column}@OData.Community.Display.V1.FormattedValue`];
    return typeof value === "string" && value.length > 0 ? value : undefined;
}

function getSignedInUserName(language: Language): string {
    const t = (text: string) => translate(text, language);
    try {
        const xrm = (window as unknown as { Xrm?: NavigationApi }).Xrm;
        return xrm?.Utility?.getGlobalContext?.().userSettings?.userName || t("Người dùng Microsoft Entra");
    } catch {
        return t("Người dùng Microsoft Entra");
    }
}

function normalizeSearch(value: string): string {
    return value.trim().toLocaleLowerCase("vi-VN");
}

function matchesSearch(values: Array<string | number | undefined>, search: string): boolean {
    if (!search) return true;
    return values.some((value) => String(value ?? "").toLocaleLowerCase("vi-VN").includes(search));
}

async function queryDashboard(dataApi: GeneratedComponentProps["dataApi"]): Promise<DashboardData> {
    const [buildings, equipment, points, readings, silverRows, syncRequests, files, importRows] =
        await Promise.all([
            dataApi.queryTable("fmc_bmsbuilding", {
                select: ["fmc_bmsbuildingid"],
                pageSize: MASTER_COUNT_LIMIT,
            }),
            dataApi.queryTable("fmc_bmsequipment", {
                select: ["fmc_bmsequipmentid"],
                pageSize: MASTER_COUNT_LIMIT,
            }),
            dataApi.queryTable("fmc_bmspoint", {
                select: [
                    "fmc_bmspointid",
                    "fmc_name",
                    "fmc_building",
                    "fmc_currentvalue",
                    "fmc_unit",
                    "fmc_lastreadingtime",
                    "fmc_sourcesystem",
                ],
                orderBy: "fmc_lastreadingtime desc",
                pageSize: MASTER_COUNT_LIMIT,
            }),
            dataApi.queryTable("fmc_bmsreading", {
                select: ["fmc_bmsreadingid"],
                pageSize: ELASTIC_COUNT_LIMIT,
            }),
            dataApi.queryTable("cr3c8_silvernewbmspoint", {
                select: ["cr3c8_silvernewbmspointid"],
                pageSize: MASTER_COUNT_LIMIT,
            }),
            dataApi.queryTable("fmc_syncrequest", {
                select: [
                    "fmc_syncrequestid",
                    "fmc_name",
                    "fmc_pipeline",
                    "fmc_status",
                    "fmc_deliveredrows",
                    "fmc_pendingafter",
                    "fmc_startedat",
                    "fmc_completedat",
                ],
                orderBy: "fmc_startedat desc",
                pageSize: MASTER_COUNT_LIMIT,
            }),
            dataApi.queryTable("fmc_spofile", {
                select: [
                    "fmc_spofileid",
                    "fmc_filename",
                    "fmc_name",
                    "fmc_status",
                    "fmc_importstatus",
                    "fmc_receivedat",
                    "fmc_processedat",
                    "fmc_rowcount",
                    "fmc_sharepointpath",
                ],
                orderBy: "fmc_receivedat desc",
                pageSize: MASTER_COUNT_LIMIT,
            }),
            dataApi.queryTable("fmc_spoimportrow", {
                select: ["fmc_spoimportrowid"],
                pageSize: ELASTIC_COUNT_LIMIT,
            }),
        ]);

    return {
        buildings: createBoundedCount(buildings.rows.length, buildings.hasMoreRows),
        equipment: createBoundedCount(equipment.rows.length, equipment.hasMoreRows),
        bronzePoints: createBoundedCount(points.rows.length, points.hasMoreRows),
        bronzeReadings: createBoundedCount(readings.rows.length, readings.hasMoreRows),
        silverRows: createBoundedCount(silverRows.rows.length, silverRows.hasMoreRows),
        syncRequests: createBoundedCount(syncRequests.rows.length, syncRequests.hasMoreRows),
        sharePointFiles: createBoundedCount(files.rows.length, files.hasMoreRows),
        importRows: createBoundedCount(importRows.rows.length, importRows.hasMoreRows),
        latestPoints: (points.rows as ReadablePoint[]).slice(0, LATEST_LIMIT),
        latestSyncRequests: (syncRequests.rows as ReadableSyncRequest[]).slice(0, LATEST_LIMIT),
        latestFiles: (files.rows as ReadableSpoFile[]).slice(0, 1),
    };
}

function KpiCard(props: { icon: ReactNode; label: string; count: BoundedCount; note: string }) {
    const styles = useStyles();
    const t = useTranslation();
    return (
        <Card className={styles.kpiCard} appearance="outline">
            <div className={styles.kpiTop}>
                <span className={styles.kpiIcon} aria-hidden="true">
                    {props.icon}
                </span>
                {props.count.isBounded && <Badge appearance="tint">{t("Giới hạn")}</Badge>}
            </div>
            <Text className={styles.kpiValue}>{props.count.label}</Text>
            <div>
                <Text className={styles.kpiLabel}>{props.label}</Text>
                <Text block className={styles.countNote}>
                    {props.note}
                </Text>
            </div>
        </Card>
    );
}

function PipelineStep(props: {
    icon: ReactNode;
    label: string;
    detail: string;
    showArrow: boolean;
}) {
    const styles = useStyles();
    return (
        <div className={styles.pipelineStep}>
            <span className={styles.pipelineIcon} aria-hidden="true">
                {props.icon}
            </span>
            <div className={styles.pipelineCopy}>
                <Text className={styles.pipelineLabel}>{props.label}</Text>
                <Text className={styles.pipelineDetail}>{props.detail}</Text>
            </div>
            {props.showArrow && <ChevronRightRegular className={styles.pipelineArrow} aria-hidden="true" />}
        </div>
    );
}

function EmptyList(props: { title: string; description: string }) {
    const styles = useStyles();
    return (
        <div className={styles.empty} role="status">
            <TableRegular aria-hidden="true" />
            <Text weight="semibold">{props.title}</Text>
            <Text>{props.description}</Text>
        </div>
    );
}

const GeneratedComponent = (props: GeneratedComponentProps) => {
    const { dataApi, pageInput } = props;
    void pageInput;
    const styles = useStyles();
    const dataReady = !!dataApi;
    const [language, setLanguage] = useState<Language>(readLanguage);
    const t = (text: string) => translate(text, language);
    const locale = language === "vi" ? "vi-VN" : "en-US";
    const formatDateTime = (value?: Date) => localizedDate(value, language);
    const formatNumber = (value?: number, digits = 2) => localizedNumber(value, language, digits);
    const userName = getSignedInUserName(language);
    const changeLanguage = (value: string) => {
        const next: Language = value === "vi" ? "vi" : "en";
        setLanguage(next);
        try { window.localStorage.setItem(LANGUAGE_KEY, next); } catch { /* Session-only if storage is blocked. */ }
    };
    useEffect(() => {
        const onStorage = (event: StorageEvent) => {
            if (event.key === LANGUAGE_KEY || event.key === null) setLanguage(readLanguage());
        };
        window.addEventListener("storage", onStorage);
        return () => window.removeEventListener("storage", onStorage);
    }, []);

    const [state, setState] = useState<DashboardState>(() => {
        const cached = winAny[CACHE_KEY] as DashboardData | undefined;
        return { value: cached ?? null, loading: cached === undefined, error: null };
    });
    const [reloadKey, setReloadKey] = useState(0);
    const [search, setSearch] = useState("");
    const [navigationError, setNavigationError] = useState<string | null>(null);

    useEffect(() => {
        if (!dataReady) return;

        const cached = winAny[CACHE_KEY] as DashboardData | undefined;
        if (cached !== undefined) {
            if (state.value !== cached) setState({ value: cached, loading: false, error: null });
            return;
        }

        let cancelled = false;
        let inflight = winAny[INFLIGHT_KEY] as Promise<DashboardData> | undefined;
        if (!inflight) {
            const generation = Number(winAny[GENERATION_KEY] ?? 0);
            inflight = queryDashboard(dataApi)
                .then((dashboard) => {
                    if (Number(winAny[GENERATION_KEY] ?? 0) === generation) {
                        winAny[CACHE_KEY] = dashboard;
                    }
                    return dashboard;
                })
                .finally(() => {
                    if (winAny[INFLIGHT_KEY] === inflight) delete winAny[INFLIGHT_KEY];
                });
            winAny[INFLIGHT_KEY] = inflight;
        }

        inflight
            .then((dashboard) => {
                if (!cancelled) setState({ value: dashboard, loading: false, error: null });
            })
            .catch(() => {
                if (!cancelled) {
                    setState({
                        value: null,
                        loading: false,
                        error: "Không thể tải dữ liệu vận hành từ Dataverse. Hãy kiểm tra quyền truy cập rồi thử lại.",
                    });
                }
            });

        return () => {
            cancelled = true;
        };
        // dataApi changes identity on every host render. Depend on readiness and the explicit refresh key only.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [dataReady, reloadKey]);

    const refresh = () => {
        winAny[GENERATION_KEY] = Number(winAny[GENERATION_KEY] ?? 0) + 1;
        delete winAny[CACHE_KEY];
        delete winAny[INFLIGHT_KEY];
        setState((current) => ({ value: current.value, loading: true, error: null }));
        setReloadKey((current) => current + 1);
    };

    const navigate = async (input: Record<string, unknown>) => {
        setNavigationError(null);
        try {
            const xrm = (window as unknown as { Xrm?: NavigationApi }).Xrm;
            if (!xrm?.Navigation?.navigateTo) throw new Error("Navigation unavailable");
            await xrm.Navigation.navigateTo(input);
        } catch {
            setNavigationError("Không thể mở mục đã chọn trong Power Apps. Vui lòng thử lại.");
        }
    };

    const normalizedSearch = normalizeSearch(search);
    const pointRows = (state.value?.latestPoints ?? []).filter((row) =>
        matchesSearch(
            [row.fmc_name, row.fmc_building, row.fmc_sourcesystem, row.fmc_currentvalue, row.fmc_unit],
            normalizedSearch,
        ),
    );
    const syncRows = (state.value?.latestSyncRequests ?? []).filter((row) =>
        matchesSearch(
            [
                row.fmc_name,
                row.fmc_pipeline,
                statusLabel("sync", row.fmc_status, language, getFormattedValue(row as unknown as Record<string, unknown>, "fmc_status")),
                row.fmc_deliveredrows,
            ],
            normalizedSearch,
        ),
    );
    const fileRows = (state.value?.latestFiles ?? []).filter((row) =>
        matchesSearch(
            [
                row.fmc_filename,
                row.fmc_name,
                row.fmc_sharepointpath,
                getFormattedValue(row as unknown as Record<string, unknown>, "fmc_status"),
            ],
            normalizedSearch,
        ),
    );
    const allCountsEmpty = state.value
        ? [
              state.value.buildings,
              state.value.equipment,
              state.value.bronzePoints,
              state.value.bronzeReadings,
              state.value.silverRows,
              state.value.syncRequests,
              state.value.sharePointFiles,
          ].every((metric) => metric.observed === 0)
        : false;

    const pointColumns = [
        createTableColumn<ReadablePoint>({
            columnId: "pointName",
            compare: (a, b) => (a.fmc_name ?? "").localeCompare(b.fmc_name ?? "", locale),
            renderHeaderCell: () => t("Điểm đo"),
            renderCell: (item) => (
                <TableCellLayout className={styles.cellLayout}>
                    <span className={styles.truncate} title={item.fmc_name || t("Chưa đặt tên")}>
                        {item.fmc_name || t("Chưa đặt tên")}
                    </span>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadablePoint>({
            columnId: "building",
            compare: (a, b) => (a.fmc_building ?? "").localeCompare(b.fmc_building ?? "", locale),
            renderHeaderCell: () => t("Tòa nhà"),
            renderCell: (item) => (
                <TableCellLayout className={styles.cellLayout}>
                    <span className={styles.truncate} title={item.fmc_building || t("Chưa xác định")}>
                        {item.fmc_building || t("Chưa xác định")}
                    </span>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadablePoint>({
            columnId: "currentValue",
            compare: (a, b) => (a.fmc_currentvalue ?? 0) - (b.fmc_currentvalue ?? 0),
            renderHeaderCell: () => t("Giá trị hiện tại"),
            renderCell: (item) => (
                <TableCellLayout className={styles.cellLayout}>
                    <span className={styles.numeric}>
                        {formatNumber(item.fmc_currentvalue)} {item.fmc_unit || ""}
                    </span>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadablePoint>({
            columnId: "lastReading",
            compare: (a, b) =>
                new Date(a.fmc_lastreadingtime ?? 0).getTime() - new Date(b.fmc_lastreadingtime ?? 0).getTime(),
            renderHeaderCell: () => t("Lần đọc gần nhất"),
            renderCell: (item) => (
                <TableCellLayout className={styles.cellLayout}>
                    <span className={styles.truncate} title={formatDateTime(item.fmc_lastreadingtime)}>
                        {formatDateTime(item.fmc_lastreadingtime)}
                    </span>
                </TableCellLayout>
            ),
        }),
    ];

    const syncColumns = [
        createTableColumn<ReadableSyncRequest>({
            columnId: "syncName",
            compare: (a, b) => (a.fmc_name ?? "").localeCompare(b.fmc_name ?? "", locale),
            renderHeaderCell: () => t("Yêu cầu"),
            renderCell: (item) => (
                <TableCellLayout className={styles.cellLayout}>
                    <span className={styles.truncate} title={item.fmc_name || t("Không có tên")}>
                        {item.fmc_name || t("Không có tên")}
                    </span>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadableSyncRequest>({
            columnId: "pipeline",
            compare: (a, b) => (a.fmc_pipeline ?? "").localeCompare(b.fmc_pipeline ?? "", locale),
            renderHeaderCell: () => "Pipeline",
            renderCell: (item) => (
                <TableCellLayout className={styles.cellLayout}>
                    <span className={styles.truncate} title={item.fmc_pipeline || t("Chưa xác định")}>
                        {item.fmc_pipeline || t("Chưa xác định")}
                    </span>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadableSyncRequest>({
            columnId: "syncStatus",
            compare: (a, b) => (a.fmc_status ?? 0) - (b.fmc_status ?? 0),
            renderHeaderCell: () => t("Trạng thái"),
            renderCell: (item) => (
                <TableCellLayout>
                    <Badge appearance="tint">
                        {statusLabel("sync", item.fmc_status, language, getFormattedValue(item as unknown as Record<string, unknown>, "fmc_status"))}
                    </Badge>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadableSyncRequest>({
            columnId: "deliveredRows",
            compare: (a, b) => (a.fmc_deliveredrows ?? 0) - (b.fmc_deliveredrows ?? 0),
            renderHeaderCell: () => t("Đã chuyển"),
            renderCell: (item) => (
                <TableCellLayout>
                    <span className={styles.numeric}>{formatNumber(item.fmc_deliveredrows, 0)}</span>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadableSyncRequest>({
            columnId: "startedAt",
            compare: (a, b) => new Date(a.fmc_startedat ?? 0).getTime() - new Date(b.fmc_startedat ?? 0).getTime(),
            renderHeaderCell: () => t("Bắt đầu"),
            renderCell: (item) => (
                <TableCellLayout className={styles.cellLayout}>
                    <span className={styles.truncate} title={formatDateTime(item.fmc_startedat)}>
                        {formatDateTime(item.fmc_startedat)}
                    </span>
                </TableCellLayout>
            ),
        }),
    ];

    const fileColumns = [
        createTableColumn<ReadableSpoFile>({
            columnId: "fileName",
            compare: (a, b) => (a.fmc_filename ?? a.fmc_name ?? "").localeCompare(b.fmc_filename ?? b.fmc_name ?? "", locale),
            renderHeaderCell: () => t("Tệp"),
            renderCell: (item) => {
                const fileName = item.fmc_filename || item.fmc_name || t("Không có tên");
                return (
                    <TableCellLayout className={styles.cellLayout}>
                        <span className={styles.truncate} title={fileName}>
                            {fileName}
                        </span>
                    </TableCellLayout>
                );
            },
        }),
        createTableColumn<ReadableSpoFile>({
            columnId: "fileStatus",
            compare: (a, b) => (a.fmc_status ?? 0) - (b.fmc_status ?? 0),
            renderHeaderCell: () => t("Tiếp nhận"),
            renderCell: (item) => (
                <TableCellLayout>
                    <Badge appearance="tint">
                        {statusLabel("archive", item.fmc_status, language, getFormattedValue(item as unknown as Record<string, unknown>, "fmc_status"))}
                    </Badge>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadableSpoFile>({
            columnId: "importStatus",
            compare: (a, b) => (a.fmc_importstatus ?? 0) - (b.fmc_importstatus ?? 0),
            renderHeaderCell: () => t("Nhập catalog"),
            renderCell: (item) => (
                <TableCellLayout>
                    <Badge appearance="tint">
                        {statusLabel("import", item.fmc_importstatus, language, getFormattedValue(item as unknown as Record<string, unknown>, "fmc_importstatus"))}
                    </Badge>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadableSpoFile>({
            columnId: "receivedAt",
            compare: (a, b) =>
                new Date(a.fmc_receivedat ?? 0).getTime() - new Date(b.fmc_receivedat ?? 0).getTime(),
            renderHeaderCell: () => t("Đã nhận lúc"),
            renderCell: (item) => (
                <TableCellLayout className={styles.cellLayout}>
                    <span className={styles.truncate} title={formatDateTime(item.fmc_receivedat)}>
                        {formatDateTime(item.fmc_receivedat)}
                    </span>
                </TableCellLayout>
            ),
        }),
    ];

    const latestFile = fileRows[0];

    return (
        <LanguageContext.Provider value={language}>
        <main lang={language} className={styles.root} aria-label={t("Trung tâm vận hành BMS")}>
            <div className={styles.content}>
                <header className={styles.hero}>
                    <div className={styles.heroCopy}>
                        <Text className={styles.eyebrow}>{t("FMC · Vận hành BMS")}</Text>
                        <h1 className={styles.title}>{t("Trung tâm vận hành BMS")}</h1>
                        <Text className={styles.heroDescription}>{t("Theo dõi luồng dữ liệu từ Bronze sang Silver, yêu cầu đồng bộ SQL và tệp catalog tiếp nhận từ SharePoint.")}</Text>
                    </div>
                    <aside className={styles.userPanel} aria-label={t("Người dùng hiện tại")}>
                        <label>
                            <Text>{t("Ngôn ngữ")}</Text>
                            <Select aria-label={t("Ngôn ngữ")} value={language} onChange={(_, data) => changeLanguage(data.value)}>
                                <option value="en">English</option>
                                <option value="vi">Tiếng Việt</option>
                            </Select>
                        </label>
                        <div className={styles.userLine}>
                            <PersonRegular aria-hidden="true" />
                            <Text weight="semibold">{userName}</Text>
                        </div>
                        <Text className={styles.profileHint}>{t("Để đăng xuất, mở menu hồ sơ Power Apps ở góc trên bên phải rồi chọn “Đăng xuất”.")}</Text>
                    </aside>
                </header>

                <section className={styles.section} aria-labelledby="kpi-heading">
                    <div className={styles.sectionHeader}>
                        <div className={styles.sectionHeading}>
                            <h2 id="kpi-heading" className={styles.sectionTitle}>{t("Toàn cảnh dữ liệu")}</h2>
                            <Text className={styles.sectionDescription}>{t("Chỉ đọc trang đầu có giới hạn; dấu “+” cho biết còn bản ghi phía sau.")}</Text>
                        </div>
                        <Button
                            appearance="secondary"
                            icon={<ArrowClockwiseRegular />}
                            onClick={refresh}
                            disabled={state.loading || !dataReady}
                            aria-label={t("Làm mới dữ liệu vận hành")}
                        >
                            {state.loading ? t("Đang làm mới") : t("Làm mới")}
                        </Button>
                    </div>

                    {state.error && (
                        <div className={styles.message} role="alert">
                            <PulseRegular aria-hidden="true" />
                            <div>
                                <Text weight="semibold">{t("Không tải được dữ liệu")}</Text>
                                <Text block>{t(state.error)}</Text>
                                <Button appearance="transparent" onClick={refresh}>{t("Thử lại")}</Button>
                            </div>
                        </div>
                    )}
                    {navigationError && (
                        <div className={styles.message} role="alert">
                            <OpenRegular aria-hidden="true" />
                            <Text>{t(navigationError)}</Text>
                        </div>
                    )}

                    {state.loading && !state.value ? (
                        <div className={styles.loading} aria-live="polite">
                            <Spinner label={t("Đang tải dữ liệu vận hành…")} labelPosition="below" />
                        </div>
                    ) : state.value && allCountsEmpty ? (
                        <EmptyList
                            title={t("Chưa có dữ liệu BMS")}
                            description={t("Hãy thêm tòa nhà, thiết bị hoặc chạy luồng demo để bắt đầu.")}
                        />
                    ) : state.value ? (
                        <div className={styles.kpiGrid} aria-live="polite">
                            <KpiCard icon={<BuildingRegular />} label={t("Tòa nhà")} count={state.value.buildings} note={t("Danh mục vận hành")} />
                            <KpiCard icon={<DesktopRegular />} label={t("Thiết bị")} count={state.value.equipment} note={t("Thiết bị đã đăng ký")} />
                            <KpiCard icon={<PulseRegular />} label={t("Điểm Bronze hiện tại")} count={state.value.bronzePoints} note={t("Giá trị BMS mới nhất")} />
                            <KpiCard icon={<DatabaseRegular />} label={t("Reading Bronze lưu giữ")} count={state.value.bronzeReadings} note={t("Giới hạn 25 dòng đầu")} />
                            <KpiCard icon={<CheckmarkCircleRegular />} label={t("Dòng Silver")} count={state.value.silverRows} note={t("Dữ liệu đã chuẩn hóa")} />
                            <KpiCard icon={<ArrowSyncRegular />} label={t("Yêu cầu đồng bộ")} count={state.value.syncRequests} note={t("Yêu cầu đồng bộ SQL")} />
                            <KpiCard icon={<FolderRegular />} label={t("Tệp SharePoint")} count={state.value.sharePointFiles} note={`${t("Dòng catalog")}: ${state.value.importRows.label}`} />
                        </div>
                    ) : null}
                </section>

                <section className={styles.section} aria-labelledby="pipeline-heading">
                    <div className={styles.sectionHeading}>
                        <h2 id="pipeline-heading" className={styles.sectionTitle}>{t("Luồng Bronze → Silver")}</h2>
                        <Text className={styles.sectionDescription}>{t("Bốn điểm kiểm soát của luồng dữ liệu trong bản demo.")}</Text>
                    </div>
                    <div className={styles.pipeline} role="list" aria-label={t("Các bước xử lý Bronze sang Silver")}>
                        <PipelineStep icon={<PulseRegular />} label={t("Thu nhận BMS")} detail={t("Sự kiện và reading")} showArrow />
                        <PipelineStep icon={<DatabaseRegular />} label="Bronze" detail={t("Giữ dữ liệu nguồn")} showArrow />
                        <PipelineStep icon={<ArrowSyncRegular />} label={t("Chuẩn hóa")} detail={t("Đồng bộ và chuyển đổi")} showArrow />
                        <PipelineStep icon={<CheckmarkCircleRegular />} label="Silver" detail={t("Sẵn sàng khai thác")} showArrow={false} />
                    </div>
                </section>

                <section className={styles.section} aria-labelledby="actions-heading">
                    <div className={styles.sectionHeading}>
                        <h2 id="actions-heading" className={styles.sectionTitle}>{t("Thao tác nhanh")}</h2>
                        <Text className={styles.sectionDescription}>{t("Mở biểu mẫu, trang demo hoặc danh sách Dataverse trong ứng dụng.")}</Text>
                    </div>
                    <div className={styles.actionGrid}>
                        <Button className={styles.actionButton} appearance="primary" icon={<AddRegular />} onClick={() => navigate({ pageType: "entityrecord", entityName: "fmc_bmsbuilding" })}>{t("Thêm tòa nhà")}</Button>
                        <Button className={styles.actionButton} appearance="primary" icon={<AddRegular />} onClick={() => navigate({ pageType: "entityrecord", entityName: "fmc_bmsequipment" })}>{t("Thêm thiết bị")}</Button>
                        <Button className={styles.actionButton} appearance="secondary" icon={<OpenRegular />} onClick={() => navigate({ pageType: "webresource", webresourceName: "fmc_/pages/BmsEventDemo.html" })}>{t("Mở BMS Event Demo")}</Button>
                        <Button className={styles.actionButton} appearance="secondary" icon={<TableRegular />} onClick={() => navigate({ pageType: "entitylist", entityName: "fmc_bmspoint" })}>{t("Danh sách điểm BMS")}</Button>
                        <Button className={styles.actionButton} appearance="secondary" icon={<ArrowSyncRegular />} onClick={() => navigate({ pageType: "entitylist", entityName: "fmc_syncrequest" })}>{t("Danh sách đồng bộ")}</Button>
                        <Button className={styles.actionButton} appearance="secondary" icon={<FolderRegular />} onClick={() => navigate({ pageType: "entitylist", entityName: "fmc_spofile" })}>{t("Danh sách tệp SharePoint")}</Button>
                    </div>
                </section>

                <section className={styles.section} aria-labelledby="latest-heading">
                    <div className={styles.sectionHeader}>
                        <div className={styles.sectionHeading}>
                            <h2 id="latest-heading" className={styles.sectionTitle}>{t("Hoạt động mới nhất")}</h2>
                            <Text className={styles.sectionDescription}>{t("Tối đa năm điểm Bronze, năm yêu cầu đồng bộ và một tệp SharePoint gần nhất.")}</Text>
                        </div>
                        <div className={styles.toolbar} role="search">
                            <SearchBox
                                className={styles.search}
                                value={search}
                                onChange={(_, data) => setSearch(data.value ?? "")}
                                contentBefore={<SearchRegular />}
                                placeholder={t("Lọc hoạt động mới nhất")}
                                aria-label={t("Lọc điểm đo, yêu cầu đồng bộ và tệp SharePoint")}
                            />
                        </div>
                    </div>

                    <div className={styles.dataCard}>
                        <div className={styles.dataCardHeader}>
                            <Text className={styles.dataCardTitle}>{t("Điểm Bronze mới nhất")}</Text>
                            <Badge appearance="tint">{pointRows.length}/{LATEST_LIMIT}</Badge>
                        </div>
                        {pointRows.length === 0 ? (
                            <EmptyList title={t("Không có điểm phù hợp")} description={t("Thử xóa bộ lọc hoặc chạy dữ liệu demo.")} />
                        ) : (
                            <div className={styles.gridScroller}>
                                <DataGrid
                                    items={pointRows}
                                    columns={pointColumns}
                                    getRowId={(row) => row.fmc_bmspointid}
                                    sortable
                                    resizableColumns
                                    columnSizingOptions={{
                                        pointName: { defaultWidth: 230, minWidth: 150 },
                                        building: { defaultWidth: 180, minWidth: 130 },
                                        currentValue: { defaultWidth: 150, minWidth: 120 },
                                        lastReading: { defaultWidth: 180, minWidth: 150 },
                                    }}
                                    aria-label={t("Năm điểm Bronze mới nhất")}
                                >
                                    <DataGridHeader>
                                        <DataGridRow>
                                            {({ renderHeaderCell }) => <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>}
                                        </DataGridRow>
                                    </DataGridHeader>
                                    <DataGridBody<ReadablePoint>>
                                        {({ item }) => (
                                            <DataGridRow<ReadablePoint> key={item.fmc_bmspointid}>
                                                {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
                                            </DataGridRow>
                                        )}
                                    </DataGridBody>
                                </DataGrid>
                            </div>
                        )}
                    </div>

                    <div className={styles.dataCard}>
                        <div className={styles.dataCardHeader}>
                            <Text className={styles.dataCardTitle}>{t("Yêu cầu đồng bộ mới nhất")}</Text>
                            <Badge appearance="tint">{syncRows.length}/{LATEST_LIMIT}</Badge>
                        </div>
                        {syncRows.length === 0 ? (
                            <EmptyList title={t("Không có yêu cầu phù hợp")} description={t("Thử xóa bộ lọc hoặc tạo yêu cầu đồng bộ mới.")} />
                        ) : (
                            <div className={styles.gridScroller}>
                                <DataGrid
                                    items={syncRows}
                                    columns={syncColumns}
                                    getRowId={(row) => row.fmc_syncrequestid}
                                    sortable
                                    resizableColumns
                                    columnSizingOptions={{
                                        syncName: { defaultWidth: 220, minWidth: 150 },
                                        pipeline: { defaultWidth: 180, minWidth: 130 },
                                        syncStatus: { defaultWidth: 160, minWidth: 130 },
                                        deliveredRows: { defaultWidth: 120, minWidth: 100 },
                                        startedAt: { defaultWidth: 180, minWidth: 150 },
                                    }}
                                    aria-label={t("Năm yêu cầu đồng bộ mới nhất")}
                                >
                                    <DataGridHeader>
                                        <DataGridRow>
                                            {({ renderHeaderCell }) => <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>}
                                        </DataGridRow>
                                    </DataGridHeader>
                                    <DataGridBody<ReadableSyncRequest>>
                                        {({ item }) => (
                                            <DataGridRow<ReadableSyncRequest> key={item.fmc_syncrequestid}>
                                                {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
                                            </DataGridRow>
                                        )}
                                    </DataGridBody>
                                </DataGrid>
                            </div>
                        )}
                    </div>

                    <div className={styles.dataCard}>
                        <div className={styles.dataCardHeader}>
                            <Text className={styles.dataCardTitle}>{t("Tệp SharePoint tiếp nhận gần nhất")}</Text>
                            <Badge appearance="tint">{fileRows.length}/1</Badge>
                        </div>
                        {fileRows.length === 0 ? (
                            <EmptyList title={t("Không có tệp phù hợp")} description={t("Thử xóa bộ lọc hoặc chờ luồng tiếp nhận SharePoint.")} />
                        ) : (
                            <>
                                <div className={styles.gridScroller}>
                                    <DataGrid
                                        items={fileRows}
                                        columns={fileColumns}
                                        getRowId={(row) => row.fmc_spofileid}
                                        sortable
                                        resizableColumns
                                        columnSizingOptions={{
                                            fileName: { defaultWidth: 260, minWidth: 170 },
                                            fileStatus: { defaultWidth: 150, minWidth: 120 },
                                            importStatus: { defaultWidth: 160, minWidth: 130 },
                                            receivedAt: { defaultWidth: 180, minWidth: 150 },
                                        }}
                                        aria-label={t("Tệp SharePoint tiếp nhận gần nhất")}
                                    >
                                        <DataGridHeader>
                                            <DataGridRow>
                                                {({ renderHeaderCell }) => <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>}
                                            </DataGridRow>
                                        </DataGridHeader>
                                        <DataGridBody<ReadableSpoFile>>
                                            {({ item }) => (
                                                <DataGridRow<ReadableSpoFile> key={item.fmc_spofileid}>
                                                    {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
                                                </DataGridRow>
                                            )}
                                        </DataGridBody>
                                    </DataGrid>
                                </div>
                                {latestFile && (
                                    <div className={styles.latestReceipt} aria-label={t("Chi tiết tệp SharePoint gần nhất")}>
                                        <div className={styles.receiptField}>
                                            <Text className={styles.receiptLabel}>{t("Số dòng khai báo")}</Text>
                                            <Text className={styles.numeric} weight="semibold">
                                                {formatNumber(latestFile.fmc_rowcount, 0)}
                                            </Text>
                                        </div>
                                        <div className={styles.receiptField}>
                                            <Text className={styles.receiptLabel}>{t("Đã xử lý lúc")}</Text>
                                            <Text weight="semibold">{formatDateTime(latestFile.fmc_processedat)}</Text>
                                        </div>
                                        <div className={styles.receiptField}>
                                            <Text className={styles.receiptLabel}>{t("Đường dẫn SharePoint")}</Text>
                                            <Text className={styles.truncate} title={latestFile.fmc_sharepointpath || t("Chưa có")} weight="semibold">
                                                {latestFile.fmc_sharepointpath || t("Chưa có")}
                                            </Text>
                                        </div>
                                        <div className={styles.receiptField}>
                                            <Text className={styles.receiptLabel}>{t("Dòng catalog đã thấy")}</Text>
                                            <Text className={styles.numeric} weight="semibold">
                                                {state.value?.importRows.label ?? "0"}
                                            </Text>
                                        </div>
                                    </div>
                                )}
                            </>
                        )}
                    </div>
                </section>
            </div>
        </main>
        </LanguageContext.Provider>
    );
};

export default GeneratedComponent;
