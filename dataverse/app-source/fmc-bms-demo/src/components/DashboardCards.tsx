import type { ReactNode } from "react";
import { Badge, Card, Text } from "@fluentui/react-components";
import { ChevronRightRegular, TableRegular } from "@fluentui/react-icons";
import type { BoundedCount } from "../models/dashboard";
import { useStyles } from "../styles/dashboardStyles";
import { useTranslation } from "../hooks/useLanguage";

export function KpiCard(props: { icon: ReactNode; label: string; count: BoundedCount; note: string }) {
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

export function PipelineStep(props: {
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

export function EmptyList(props: { title: string; description: string }) {
    const styles = useStyles();
    return (
        <div className={styles.empty} role="status">
            <TableRegular aria-hidden="true" />
            <Text weight="semibold">{props.title}</Text>
            <Text>{props.description}</Text>
        </div>
    );
}
