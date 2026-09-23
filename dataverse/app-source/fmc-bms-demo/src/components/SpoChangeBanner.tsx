import { Badge, Button, Spinner, Text } from "@fluentui/react-components";
import { ArrowSyncRegular, ClockRegular, FolderRegular } from "@fluentui/react-icons";
import type { ReadableSpoChangeRequest } from "../models/dashboard";
import type { Language } from "../services/localization";
import { localizedDate, translate } from "../services/localization";

type Props = {
    items: ReadableSpoChangeRequest[];
    now: Date;
    claimingId: string | null;
    error: string | null;
    language: Language;
    canClaim: boolean;
    onClaim: (request: ReadableSpoChangeRequest) => void;
    onViewAll: () => void;
    styles: Record<string, string>;
};

function remaining(dueAt: Date | undefined, now: Date, language: Language): string {
    const seconds = Math.max(0, Math.ceil((new Date(dueAt || now).getTime() - now.getTime()) / 1000));
    if (seconds === 0) return translate("Đang chờ lệnh tự động đồng bộ…", language);
    const minutes = Math.floor(seconds / 60);
    return translate("Tự đồng bộ sau", language) + ` ${minutes}:${String(seconds % 60).padStart(2, "0")}`;
}

export function SpoChangeBanner({ items, now, claimingId, error, language, canClaim, onClaim, onViewAll, styles }: Props) {
    const t = (text: string) => translate(text, language);
    // A denied/unavailable optional queue must not add a second generic error
    // banner beside the dashboard's primary data-access error.
    if (items.length === 0) return null;
    return (
        <section className={styles.spoChangeSection} aria-labelledby="spo-change-heading" aria-live="polite">
            <div className={styles.spoChangeHeader}>
                <div>
                    <Text className={styles.spoChangeEyebrow}>{t("SharePoint thay đổi")}</Text>
                    <h2 id="spo-change-heading" className={styles.sectionTitle}>{t("Tệp mới sẵn sàng để đồng bộ")}</h2>
                </div>
                <Button appearance="subtle" size="small" onClick={onViewAll}>{t("Xem tất cả")}</Button>
            </div>
            {error && <div className={styles.spoChangeError}><Text>{t(error)}</Text></div>}
            {items.map((item) => {
                const busy = claimingId === item.fmc_spochangerequestid;
                return (
                    <div className={styles.spoChangeCard} key={item.fmc_spochangerequestid}>
                        <div className={styles.spoChangeIcon}><FolderRegular aria-hidden="true" /></div>
                        <div className={styles.spoChangeCopy}>
                            <Text weight="semibold" className={styles.truncate} title={item.fmc_filename}>{item.fmc_filename || t("Tệp SharePoint")}</Text>
                            <Text className={styles.spoChangePath} title={item.fmc_sharepointpath}>{item.fmc_sharepointpath || t("Đường dẫn SharePoint chưa có")}</Text>
                            <div className={styles.spoChangeMeta}>
                                <Badge appearance="tint" icon={<ClockRegular />}>{remaining(item.fmc_dueat, now, language)}</Badge>
                                <Text size={200}>{t("Phát hiện")} {localizedDate(item.fmc_detectedat, language)}</Text>
                            </div>
                        </div>
                        {canClaim && (
                            <Button appearance="primary" icon={busy ? <Spinner size="tiny" /> : <ArrowSyncRegular />} disabled={busy} onClick={() => onClaim(item)}>
                                {busy ? t("Đang gửi") : t("Đồng bộ ngay")}
                            </Button>
                        )}
                    </div>
                );
            })}
        </section>
    );
}
