import { useRef, useState } from "react";
import type { GeneratedComponentProps } from "../RuntimeTypes";
import type { ReadablePoint, ReadableSyncRequest, ReadableSpoFile } from "./models/dashboard";
import { localizedDate, localizedNumber, translate, statusLabel } from "./services/localization";
import { LATEST_LIMIT } from "./services/dashboardService";
import { getFormattedValue, normalizeSearch, matchesSearch } from "./services/presentation";
import { getSignedInUserName, openAppItem } from "./services/navigationService";
import { openAgentChat } from "./services/agentChatService";
import { useStyles } from "./styles/dashboardStyles";
import { LanguageContext, useLanguagePreference } from "./hooks/useLanguage";
import { useDashboard } from "./hooks/useDashboard";
import { useSpoChanges } from "./hooks/useSpoChanges";
import { canClaimSpoChanges } from "./services/spoChangeService";
import { KpiCard, PipelineStep, EmptyList } from "./components/DashboardCards";
import { SpoChangeBanner } from "./components/SpoChangeBanner";
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
} from "@fluentui/react-components";
import {
    AddRegular,
    ArrowDownRegular,
    ArrowClockwiseRegular,
    ArrowSyncRegular,
    BuildingRegular,
    ChatRegular,
    CheckmarkCircleRegular,
    DatabaseRegular,
    DesktopRegular,
    FolderRegular,
    OpenRegular,
    PersonRegular,
    PulseRegular,
    SearchRegular,
    TableRegular,
} from "@fluentui/react-icons";

// Component entry point của dashboard generative page.
// Nó kết nối dataApi của Power Apps với hook dữ liệu, bộ lọc, bảng và navigation.
export const GeneratedComponent = (props: GeneratedComponentProps) => {
    const { dataApi, pageInput } = props;
    void pageInput;
    const styles = useStyles();
    const { language, changeLanguage } = useLanguagePreference();
    const t = (text: string) => translate(text, language);
    const locale = language === "vi" ? "vi-VN" : "en-US";
    const formatDateTime = (value?: Date) => localizedDate(value, language);
    const formatNumber = (value?: number, digits = 2) => localizedNumber(value, language, digits);
    const userName = getSignedInUserName(language);
    const { state, refresh, dataReady } = useDashboard(dataApi);
    const spoChanges = useSpoChanges(dataApi);
    const canClaimChanges = canClaimSpoChanges();
    const [search, setSearch] = useState("");
    const [navigationError, setNavigationError] = useState<string | null>(null);
    const [agentError, setAgentError] = useState<string | null>(null);
    const [agentOpening, setAgentOpening] = useState(false);
    const pageRef = useRef<HTMLElement>(null);

    const scrollToBottom = () => {
        const page = pageRef.current;
        if (!page) return;
        const target = page.scrollHeight > page.clientHeight ? page : document.scrollingElement;
        if (!target) return;
        const reduceMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
        target.scrollTo({ top: target.scrollHeight, behavior: reduceMotion ? "auto" : "smooth" });
    };

    // Mở record/page trong model-driven app và hiển thị lỗi thân thiện nếu host từ chối.
    const navigate = async (input: Record<string, unknown>) => {
        setNavigationError(null);
        try {
            await openAppItem(input);
        } catch {
            setNavigationError("Không thể mở mục đã chọn trong Power Apps. Vui lòng thử lại.");
        }
    };

    const openChat = async () => {
        setAgentError(null);
        setAgentOpening(true);
        try {
            if ((await openAgentChat()) === "unavailable") {
                setAgentError("Copilot chat chưa được bật hoặc chưa khả dụng cho tài khoản này trong Power Apps.");
            }
        } catch {
            setAgentError("Không thể mở Copilot chat. Hãy thử lại hoặc kiểm tra cấu hình Agent trong app.");
        } finally {
            setAgentOpening(false);
        }
    };

    // Chuẩn hóa một lần rồi dùng chung cho ba danh sách trên dashboard.
    const normalizedSearch = normalizeSearch(search);
    // Lọc riêng từng danh sách để search không làm thay đổi snapshot dữ liệu gốc.
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

    // Cấu hình cột cho bảng Point; compare dùng locale hiện tại để sort đúng ngôn ngữ.
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

    // Cấu hình cột cho bảng Sync Request và trạng thái option-set.
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

    // Cấu hình cột cho bảng SPO File/Import receipt.
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

    // Phần render bên dưới chỉ dựng UI; việc đọc dữ liệu nằm trong useDashboard.
    return (
        <LanguageContext.Provider value={language}>
        <main ref={pageRef} lang={language} className={styles.root} aria-label={t("Trung tâm vận hành BMS")}>
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

                <section className={styles.agentCard} aria-labelledby="agent-heading">
                    <div className={styles.agentIcon}><ChatRegular aria-hidden="true" /></div>
                    <div className={styles.agentCopy}>
                        <Text className={styles.agentEyebrow}>{t("Trợ lý AI")}</Text>
                        <h2 id="agent-heading" className={styles.agentTitle}>BMS Operations Assistant</h2>
                        <Text className={styles.agentDescription}>{t("Agent chưa được kết nối với ứng dụng. Nếu Microsoft 365 Copilot đã bật, bạn có thể mở khung chat của Power Apps.")}</Text>
                        {agentError && <Text className={styles.agentError} role="alert">{t(agentError)}</Text>}
                    </div>
                    <Button className={styles.agentButton} appearance="primary" icon={<ChatRegular />} onClick={() => void openChat()} disabled={agentOpening}>
                        {agentOpening ? t("Đang mở Copilot chat") : t("Mở khung chat")}
                    </Button>
                </section>

                <SpoChangeBanner
                    items={spoChanges.items}
                    now={spoChanges.now}
                    claimingId={spoChanges.claimingId}
                    error={spoChanges.error}
                    language={language}
                    canClaim={canClaimChanges}
                    styles={styles}
                    onClaim={(request) => void spoChanges.claim(request)}
                    onViewAll={() => void navigate({ pageType: "entitylist", entityName: "fmc_spochangerequest" })}
                />

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
            <Button
                className={styles.scrollDownButton}
                appearance="primary"
                shape="circular"
                size="large"
                icon={<ArrowDownRegular />}
                onClick={scrollToBottom}
                aria-label={t("Cuộn xuống cuối trang")}
                title={t("Cuộn xuống cuối trang")}
            />
        </main>
        </LanguageContext.Provider>
    );
};

export default GeneratedComponent;
