import { useEffect, useState } from "react";
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

function formatDateTime(value?: Date): string {
    if (!value) return "Chưa có";
    const parsed = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(parsed.getTime())) return "Chưa có";
    return new Intl.DateTimeFormat("vi-VN", {
        dateStyle: "short",
        timeStyle: "short",
    }).format(parsed);
}

function formatNumber(value?: number, maximumFractionDigits = 2): string {
    if (typeof value !== "number" || !Number.isFinite(value)) return "—";
    return new Intl.NumberFormat("vi-VN", { maximumFractionDigits }).format(value);
}

function getFormattedValue(row: Record<string, unknown>, column: string): string | undefined {
    const value = row[`${column}@OData.Community.Display.V1.FormattedValue`];
    return typeof value === "string" && value.length > 0 ? value : undefined;
}

function getSignedInUserName(): string {
    try {
        const xrm = (window as unknown as { Xrm?: NavigationApi }).Xrm;
        return xrm?.Utility?.getGlobalContext?.().userSettings?.userName || "Người dùng Microsoft Entra";
    } catch {
        return "Người dùng Microsoft Entra";
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
    return (
        <Card className={styles.kpiCard} appearance="outline">
            <div className={styles.kpiTop}>
                <span className={styles.kpiIcon} aria-hidden="true">
                    {props.icon}
                </span>
                {props.count.isBounded && <Badge appearance="tint">Giới hạn</Badge>}
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
    const userName = getSignedInUserName();

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
                getFormattedValue(row as unknown as Record<string, unknown>, "fmc_status"),
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
            compare: (a, b) => (a.fmc_name ?? "").localeCompare(b.fmc_name ?? "", "vi"),
            renderHeaderCell: () => "Điểm đo",
            renderCell: (item) => (
                <TableCellLayout className={styles.cellLayout}>
                    <span className={styles.truncate} title={item.fmc_name || "Chưa đặt tên"}>
                        {item.fmc_name || "Chưa đặt tên"}
                    </span>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadablePoint>({
            columnId: "building",
            compare: (a, b) => (a.fmc_building ?? "").localeCompare(b.fmc_building ?? "", "vi"),
            renderHeaderCell: () => "Tòa nhà",
            renderCell: (item) => (
                <TableCellLayout className={styles.cellLayout}>
                    <span className={styles.truncate} title={item.fmc_building || "Chưa xác định"}>
                        {item.fmc_building || "Chưa xác định"}
                    </span>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadablePoint>({
            columnId: "currentValue",
            compare: (a, b) => (a.fmc_currentvalue ?? 0) - (b.fmc_currentvalue ?? 0),
            renderHeaderCell: () => "Giá trị hiện tại",
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
            renderHeaderCell: () => "Lần đọc gần nhất",
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
            compare: (a, b) => (a.fmc_name ?? "").localeCompare(b.fmc_name ?? "", "vi"),
            renderHeaderCell: () => "Yêu cầu",
            renderCell: (item) => (
                <TableCellLayout className={styles.cellLayout}>
                    <span className={styles.truncate} title={item.fmc_name || "Không có tên"}>
                        {item.fmc_name || "Không có tên"}
                    </span>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadableSyncRequest>({
            columnId: "pipeline",
            compare: (a, b) => (a.fmc_pipeline ?? "").localeCompare(b.fmc_pipeline ?? "", "vi"),
            renderHeaderCell: () => "Pipeline",
            renderCell: (item) => (
                <TableCellLayout className={styles.cellLayout}>
                    <span className={styles.truncate} title={item.fmc_pipeline || "Chưa xác định"}>
                        {item.fmc_pipeline || "Chưa xác định"}
                    </span>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadableSyncRequest>({
            columnId: "syncStatus",
            compare: (a, b) => (a.fmc_status ?? 0) - (b.fmc_status ?? 0),
            renderHeaderCell: () => "Trạng thái",
            renderCell: (item) => (
                <TableCellLayout>
                    <Badge appearance="tint">
                        {getFormattedValue(item as unknown as Record<string, unknown>, "fmc_status") || "Chưa xác định"}
                    </Badge>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadableSyncRequest>({
            columnId: "deliveredRows",
            compare: (a, b) => (a.fmc_deliveredrows ?? 0) - (b.fmc_deliveredrows ?? 0),
            renderHeaderCell: () => "Đã chuyển",
            renderCell: (item) => (
                <TableCellLayout>
                    <span className={styles.numeric}>{formatNumber(item.fmc_deliveredrows, 0)}</span>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadableSyncRequest>({
            columnId: "startedAt",
            compare: (a, b) => new Date(a.fmc_startedat ?? 0).getTime() - new Date(b.fmc_startedat ?? 0).getTime(),
            renderHeaderCell: () => "Bắt đầu",
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
            compare: (a, b) => (a.fmc_filename ?? a.fmc_name ?? "").localeCompare(b.fmc_filename ?? b.fmc_name ?? "", "vi"),
            renderHeaderCell: () => "Tệp",
            renderCell: (item) => {
                const fileName = item.fmc_filename || item.fmc_name || "Không có tên";
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
            renderHeaderCell: () => "Tiếp nhận",
            renderCell: (item) => (
                <TableCellLayout>
                    <Badge appearance="tint">
                        {getFormattedValue(item as unknown as Record<string, unknown>, "fmc_status") || "Chưa xác định"}
                    </Badge>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadableSpoFile>({
            columnId: "importStatus",
            compare: (a, b) => (a.fmc_importstatus ?? 0) - (b.fmc_importstatus ?? 0),
            renderHeaderCell: () => "Nhập catalog",
            renderCell: (item) => (
                <TableCellLayout>
                    <Badge appearance="tint">
                        {getFormattedValue(item as unknown as Record<string, unknown>, "fmc_importstatus") ||
                            "Chưa yêu cầu"}
                    </Badge>
                </TableCellLayout>
            ),
        }),
        createTableColumn<ReadableSpoFile>({
            columnId: "receivedAt",
            compare: (a, b) =>
                new Date(a.fmc_receivedat ?? 0).getTime() - new Date(b.fmc_receivedat ?? 0).getTime(),
            renderHeaderCell: () => "Đã nhận lúc",
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
        <main className={styles.root} aria-label="Trung tâm vận hành BMS">
            <div className={styles.content}>
                <header className={styles.hero}>
                    <div className={styles.heroCopy}>
                        <Text className={styles.eyebrow}>FMC · BMS operations</Text>
                        <h1 className={styles.title}>Trung tâm vận hành BMS</h1>
                        <Text className={styles.heroDescription}>
                            Theo dõi luồng dữ liệu từ Bronze sang Silver, yêu cầu đồng bộ SQL và tệp catalog tiếp nhận từ SharePoint.
                        </Text>
                    </div>
                    <aside className={styles.userPanel} aria-label="Người dùng hiện tại">
                        <div className={styles.userLine}>
                            <PersonRegular aria-hidden="true" />
                            <Text weight="semibold">{userName}</Text>
                        </div>
                        <Text className={styles.profileHint}>
                            Để đăng xuất, mở menu hồ sơ Power Apps ở góc trên bên phải rồi chọn “Đăng xuất”.
                        </Text>
                    </aside>
                </header>

                <section className={styles.section} aria-labelledby="kpi-heading">
                    <div className={styles.sectionHeader}>
                        <div className={styles.sectionHeading}>
                            <h2 id="kpi-heading" className={styles.sectionTitle}>
                                Toàn cảnh dữ liệu
                            </h2>
                            <Text className={styles.sectionDescription}>
                                Chỉ đọc trang đầu có giới hạn; dấu “+” cho biết còn bản ghi phía sau.
                            </Text>
                        </div>
                        <Button
                            appearance="secondary"
                            icon={<ArrowClockwiseRegular />}
                            onClick={refresh}
                            disabled={state.loading || !dataReady}
                            aria-label="Làm mới dữ liệu vận hành"
                        >
                            {state.loading ? "Đang làm mới" : "Làm mới"}
                        </Button>
                    </div>

                    {state.error && (
                        <div className={styles.message} role="alert">
                            <PulseRegular aria-hidden="true" />
                            <div>
                                <Text weight="semibold">Không tải được dữ liệu</Text>
                                <Text block>{state.error}</Text>
                                <Button appearance="transparent" onClick={refresh}>
                                    Thử lại
                                </Button>
                            </div>
                        </div>
                    )}
                    {navigationError && (
                        <div className={styles.message} role="alert">
                            <OpenRegular aria-hidden="true" />
                            <Text>{navigationError}</Text>
                        </div>
                    )}

                    {state.loading && !state.value ? (
                        <div className={styles.loading} aria-live="polite">
                            <Spinner label="Đang tải dữ liệu vận hành…" labelPosition="below" />
                        </div>
                    ) : state.value && allCountsEmpty ? (
                        <EmptyList
                            title="Chưa có dữ liệu BMS"
                            description="Hãy thêm tòa nhà, thiết bị hoặc chạy luồng demo để bắt đầu."
                        />
                    ) : state.value ? (
                        <div className={styles.kpiGrid} aria-live="polite">
                            <KpiCard icon={<BuildingRegular />} label="Tòa nhà" count={state.value.buildings} note="Danh mục vận hành" />
                            <KpiCard icon={<DesktopRegular />} label="Thiết bị" count={state.value.equipment} note="Thiết bị đã đăng ký" />
                            <KpiCard icon={<PulseRegular />} label="Điểm Bronze hiện tại" count={state.value.bronzePoints} note="Giá trị BMS mới nhất" />
                            <KpiCard icon={<DatabaseRegular />} label="Reading Bronze lưu giữ" count={state.value.bronzeReadings} note="Giới hạn 25 dòng đầu" />
                            <KpiCard icon={<CheckmarkCircleRegular />} label="Dòng Silver" count={state.value.silverRows} note="Dữ liệu đã chuẩn hóa" />
                            <KpiCard icon={<ArrowSyncRegular />} label="Yêu cầu đồng bộ" count={state.value.syncRequests} note="SQL sync requests" />
                            <KpiCard icon={<FolderRegular />} label="Tệp SharePoint" count={state.value.sharePointFiles} note={`Dòng catalog: ${state.value.importRows.label}`} />
                        </div>
                    ) : null}
                </section>

                <section className={styles.section} aria-labelledby="pipeline-heading">
                    <div className={styles.sectionHeading}>
                        <h2 id="pipeline-heading" className={styles.sectionTitle}>
                            Luồng Bronze → Silver
                        </h2>
                        <Text className={styles.sectionDescription}>
                            Bốn điểm kiểm soát của luồng dữ liệu trong bản demo.
                        </Text>
                    </div>
                    <div className={styles.pipeline} role="list" aria-label="Các bước xử lý Bronze sang Silver">
                        <PipelineStep icon={<PulseRegular />} label="Thu nhận BMS" detail="Sự kiện và reading" showArrow />
                        <PipelineStep icon={<DatabaseRegular />} label="Bronze" detail="Giữ dữ liệu nguồn" showArrow />
                        <PipelineStep icon={<ArrowSyncRegular />} label="Chuẩn hóa" detail="Đồng bộ và chuyển đổi" showArrow />
                        <PipelineStep icon={<CheckmarkCircleRegular />} label="Silver" detail="Sẵn sàng khai thác" showArrow={false} />
                    </div>
                </section>

                <section className={styles.section} aria-labelledby="actions-heading">
                    <div className={styles.sectionHeading}>
                        <h2 id="actions-heading" className={styles.sectionTitle}>
                            Thao tác nhanh
                        </h2>
                        <Text className={styles.sectionDescription}>
                            Mở biểu mẫu, trang demo hoặc danh sách Dataverse trong ứng dụng.
                        </Text>
                    </div>
                    <div className={styles.actionGrid}>
                        <Button className={styles.actionButton} appearance="primary" icon={<AddRegular />} onClick={() => navigate({ pageType: "entityrecord", entityName: "fmc_bmsbuilding" })}>
                            Thêm tòa nhà
                        </Button>
                        <Button className={styles.actionButton} appearance="primary" icon={<AddRegular />} onClick={() => navigate({ pageType: "entityrecord", entityName: "fmc_bmsequipment" })}>
                            Thêm thiết bị
                        </Button>
                        <Button className={styles.actionButton} appearance="secondary" icon={<OpenRegular />} onClick={() => navigate({ pageType: "webresource", webresourceName: "fmc_/pages/BmsEventDemo.html" })}>
                            Mở BMS Event Demo
                        </Button>
                        <Button className={styles.actionButton} appearance="secondary" icon={<TableRegular />} onClick={() => navigate({ pageType: "entitylist", entityName: "fmc_bmspoint" })}>
                            Danh sách điểm BMS
                        </Button>
                        <Button className={styles.actionButton} appearance="secondary" icon={<ArrowSyncRegular />} onClick={() => navigate({ pageType: "entitylist", entityName: "fmc_syncrequest" })}>
                            Danh sách đồng bộ
                        </Button>
                        <Button className={styles.actionButton} appearance="secondary" icon={<FolderRegular />} onClick={() => navigate({ pageType: "entitylist", entityName: "fmc_spofile" })}>
                            Danh sách tệp SharePoint
                        </Button>
                    </div>
                </section>

                <section className={styles.section} aria-labelledby="latest-heading">
                    <div className={styles.sectionHeader}>
                        <div className={styles.sectionHeading}>
                            <h2 id="latest-heading" className={styles.sectionTitle}>
                                Hoạt động mới nhất
                            </h2>
                            <Text className={styles.sectionDescription}>
                                Tối đa năm điểm Bronze, năm yêu cầu đồng bộ và một tệp SharePoint gần nhất.
                            </Text>
                        </div>
                        <div className={styles.toolbar} role="search">
                            <SearchBox
                                className={styles.search}
                                value={search}
                                onChange={(_, data) => setSearch(data.value ?? "")}
                                contentBefore={<SearchRegular />}
                                placeholder="Lọc hoạt động mới nhất"
                                aria-label="Lọc điểm đo, yêu cầu đồng bộ và tệp SharePoint"
                            />
                        </div>
                    </div>

                    <div className={styles.dataCard}>
                        <div className={styles.dataCardHeader}>
                            <Text className={styles.dataCardTitle}>Điểm Bronze mới nhất</Text>
                            <Badge appearance="tint">{pointRows.length}/{LATEST_LIMIT}</Badge>
                        </div>
                        {pointRows.length === 0 ? (
                            <EmptyList title="Không có điểm phù hợp" description="Thử xóa bộ lọc hoặc chạy dữ liệu demo." />
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
                                    aria-label="Năm điểm Bronze mới nhất"
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
                            <Text className={styles.dataCardTitle}>Yêu cầu đồng bộ mới nhất</Text>
                            <Badge appearance="tint">{syncRows.length}/{LATEST_LIMIT}</Badge>
                        </div>
                        {syncRows.length === 0 ? (
                            <EmptyList title="Không có yêu cầu phù hợp" description="Thử xóa bộ lọc hoặc tạo yêu cầu đồng bộ mới." />
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
                                    aria-label="Năm yêu cầu đồng bộ mới nhất"
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
                            <Text className={styles.dataCardTitle}>Tệp SharePoint tiếp nhận gần nhất</Text>
                            <Badge appearance="tint">{fileRows.length}/1</Badge>
                        </div>
                        {fileRows.length === 0 ? (
                            <EmptyList title="Không có tệp phù hợp" description="Thử xóa bộ lọc hoặc chờ luồng tiếp nhận SharePoint." />
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
                                        aria-label="Tệp SharePoint tiếp nhận gần nhất"
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
                                    <div className={styles.latestReceipt} aria-label="Chi tiết tệp SharePoint gần nhất">
                                        <div className={styles.receiptField}>
                                            <Text className={styles.receiptLabel}>Số dòng khai báo</Text>
                                            <Text className={styles.numeric} weight="semibold">
                                                {formatNumber(latestFile.fmc_rowcount, 0)}
                                            </Text>
                                        </div>
                                        <div className={styles.receiptField}>
                                            <Text className={styles.receiptLabel}>Đã xử lý lúc</Text>
                                            <Text weight="semibold">{formatDateTime(latestFile.fmc_processedat)}</Text>
                                        </div>
                                        <div className={styles.receiptField}>
                                            <Text className={styles.receiptLabel}>Đường dẫn SharePoint</Text>
                                            <Text className={styles.truncate} title={latestFile.fmc_sharepointpath || "Chưa có"} weight="semibold">
                                                {latestFile.fmc_sharepointpath || "Chưa có"}
                                            </Text>
                                        </div>
                                        <div className={styles.receiptField}>
                                            <Text className={styles.receiptLabel}>Dòng catalog đã thấy</Text>
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
    );
};

export default GeneratedComponent;
