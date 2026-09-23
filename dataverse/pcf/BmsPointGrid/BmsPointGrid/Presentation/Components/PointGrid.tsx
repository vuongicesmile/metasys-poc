import * as React from "react";
import { Button } from "@fluentui/react-components";
import { PointGridViewModel } from "../../Business/Contracts/PointGridDto";
import { PointGridText } from "../../Common/Localization/messages";
import { GridState } from "./GridState";
import { ReadingValue } from "./ReadingValue";

export interface PointGridProps {
    readonly model: PointGridViewModel;
    readonly text: PointGridText;
    readonly onRefresh: () => void;
    readonly onNextPage: () => void;
    readonly onPreviousPage: () => void;
    readonly onOpenPoint: (id: string) => Promise<void>;
    readonly agentWebChatUrl?: string;
}

/** Presentation chỉ render DTO và gọi callback; không đọc context hoặc dataset trực tiếp. */
export function PointGrid(props: PointGridProps): React.ReactElement {
    const [actionError, setActionError] = React.useState<string | undefined>();
    const [assistantOpen, setAssistantOpen] = React.useState(false);
    const safeAgentWebChatUrl = getSafeAgentWebChatUrl(props.agentWebChatUrl);

    const openPoint = async (id: string): Promise<void> => {
        setActionError(undefined);
        try {
            await props.onOpenPoint(id);
        } catch {
            // Không hiển thị lỗi kỹ thuật/credential từ host cho người dùng cuối.
            setActionError(props.text.errorPrefix);
        }
    };

    const hasRows = props.model.rows.length > 0;
    return <section className="fmc-point-grid" aria-label={props.text.title}>
        <header className="fmc-point-grid__header">
            <div>
                <h2 className="fmc-point-grid__title">{props.text.title}</h2>
                {props.model.totalResultCount !== undefined && <span className="fmc-point-grid__count">{props.text.total(props.model.totalResultCount)}</span>}
            </div>
            <div className="fmc-point-grid__header-actions">
                <Button appearance="secondary" onClick={() => setAssistantOpen((open) => !open)} aria-expanded={assistantOpen}>
                    {assistantOpen ? props.text.closeAssistant : props.text.assistant}
                </Button>
                <Button appearance="secondary" onClick={props.onRefresh} disabled={props.model.isLoading}>{props.text.refresh}</Button>
            </div>
        </header>

        {assistantOpen && <section className="fmc-point-grid__assistant" aria-label={props.text.assistant}>
            {safeAgentWebChatUrl ? <iframe className="fmc-point-grid__assistant-frame" src={safeAgentWebChatUrl} title={props.text.assistant} referrerPolicy="no-referrer" allow="microphone" /> : <p role="status">{props.text.assistantNotConfigured}</p>}
        </section>}

        {actionError && <div className="fmc-point-grid__state fmc-point-grid__state--error" role="alert">{actionError}</div>}
        {!hasRows ? <GridState
            isLoading={props.model.isLoading}
            errorMessage={props.model.errorMessage}
            loadingText={props.text.loading}
            emptyText={props.text.empty}
            errorPrefix={props.text.errorPrefix}
        /> : <table className="fmc-point-grid__table">
            <thead>
                <tr>
                    <th scope="col">{props.text.point}</th>
                    <th scope="col">{props.text.objectId}</th>
                    <th scope="col">{props.text.currentValue}</th>
                    <th scope="col">{props.text.readingTime}</th>
                    <th scope="col">{props.text.source}</th>
                </tr>
            </thead>
            <tbody>
                {props.model.rows.map((row) => <tr key={row.id}>
                    <th scope="row"><Button appearance="transparent" onClick={() => { void openPoint(row.id); }} aria-label={props.text.rowAction(row.name)}>{row.name}</Button></th>
                    <td>{row.objectId}</td>
                    <td><ReadingValue value={row.currentValue} unit={row.unit} /></td>
                    <td><time dateTime={row.lastReadingTimeUtc?.toISOString()}>{formatReadingTime(row.lastReadingTimeUtc)}</time><span className={`fmc-point-grid__freshness fmc-point-grid__freshness--${row.freshness}`}>{props.text.freshness[row.freshness]}</span></td>
                    <td>{row.sourceSystem ?? "—"}</td>
                </tr>)}
            </tbody>
        </table>}

        <footer className="fmc-point-grid__pager" aria-label="Paging">
            <Button appearance="secondary" onClick={props.onPreviousPage} disabled={props.model.isLoading || !props.model.hasPreviousPage}>{props.text.previous}</Button>
            <Button appearance="secondary" onClick={props.onNextPage} disabled={props.model.isLoading || !props.model.hasNextPage}>{props.text.next}</Button>
        </footer>
    </section>;
}

/** Only allow an HTTPS Microsoft Copilot host; never treat control metadata as executable HTML. */
function getSafeAgentWebChatUrl(value: string | null | undefined): string | undefined {
    if (!value?.trim()) return undefined;
    try {
        const url = new URL(value.trim());
        const host = url.hostname.toLowerCase();
        const isMicrosoftHost = host === "microsoft.com" || host.endsWith(".microsoft.com") || host === "powerva.microsoft.com" || host.endsWith(".powerva.microsoft.com");
        return url.protocol === "https:" && isMicrosoftHost ? url.toString() : undefined;
    } catch {
        return undefined;
    }
}

/** Format chỉ để đọc trên browser; Date gốc vẫn được giữ UTC trong DTO. */
function formatReadingTime(value: Date | undefined): string {
    if (!value || Number.isNaN(value.getTime())) {
        return "—";
    }

    return new Intl.DateTimeFormat(undefined, { dateStyle: "short", timeStyle: "medium" }).format(value);
}
