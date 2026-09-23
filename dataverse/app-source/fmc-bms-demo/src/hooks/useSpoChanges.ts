import { useCallback, useEffect, useState } from "react";
import type { GeneratedComponentProps } from "../../RuntimeTypes";
import type { ReadableSpoChangeRequest } from "../models/dashboard";
import { claimSpoChange, queryPendingSpoChanges } from "../services/spoChangeService";

type SpoChangeState = { items: ReadableSpoChangeRequest[]; loading: boolean; error: string | null; claimingId: string | null; now: Date };

export function useSpoChanges(dataApi: GeneratedComponentProps["dataApi"]) {
    const dataReady = !!dataApi;
    const [state, setState] = useState<SpoChangeState>({ items: [], loading: dataReady, error: null, claimingId: null, now: new Date() });
    const [refreshKey, setRefreshKey] = useState(0);

    useEffect(() => {
        if (!dataReady) return;
        let disposed = false;
        const load = async () => {
            try {
                const items = await queryPendingSpoChanges(dataApi);
                if (!disposed) setState((current) => ({ ...current, items, loading: false, error: null }));
            } catch {
                if (!disposed) setState((current) => ({ ...current, loading: false, error: "Không thể tải các thay đổi SharePoint đang chờ." }));
            }
        };
        void load();
        const poll = window.setInterval(() => { if (document.visibilityState === "visible") void load(); }, 20_000);
        const onVisible = () => { if (document.visibilityState === "visible") void load(); };
        document.addEventListener("visibilitychange", onVisible);
        return () => { disposed = true; window.clearInterval(poll); document.removeEventListener("visibilitychange", onVisible); };
        // Hosts recreate dataApi on a render; refreshKey deliberately controls re-query.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [dataReady, refreshKey]);

    useEffect(() => {
        const interval = window.setInterval(() => setState((current) => ({ ...current, now: new Date() })), 1_000);
        return () => window.clearInterval(interval);
    }, []);

    const refresh = useCallback(() => setRefreshKey((value) => value + 1), []);
    const claim = useCallback(async (request: ReadableSpoChangeRequest) => {
        setState((current) => ({ ...current, claimingId: request.fmc_spochangerequestid, error: null }));
        try {
            await claimSpoChange(request);
            setRefreshKey((value) => value + 1);
        } catch (error) {
            setState((current) => ({ ...current, error: error instanceof Error ? error.message : "Không thể yêu cầu đồng bộ ngay.", claimingId: null }));
            return;
        }
        setState((current) => ({ ...current, claimingId: null }));
    }, []);
    return { ...state, refresh, claim };
}
