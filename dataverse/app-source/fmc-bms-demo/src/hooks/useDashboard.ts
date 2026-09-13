import { useEffect, useState } from "react";
import type { GeneratedComponentProps } from "../../RuntimeTypes";
import type { DashboardData, DashboardState } from "../models/dashboard";
import { queryDashboard } from "../services/dashboardService";

export const CACHE_KEY = "__genpage_bms_operations_dashboard_v1";
export const INFLIGHT_KEY = "__genpage_bms_operations_dashboard_inflight_v1";
export const GENERATION_KEY = "__genpage_bms_operations_dashboard_generation_v1";
export const winAny = window as unknown as Record<string, unknown>;
export function useDashboard(dataApi: GeneratedComponentProps["dataApi"]) {
    const dataReady = !!dataApi;
    const [state, setState] = useState<DashboardState>(() => {
        const cached = winAny[CACHE_KEY] as DashboardData | undefined;
        return { value: cached ?? null, loading: cached === undefined, error: null };
    });
    const [reloadKey, setReloadKey] = useState(0);

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
    return { state, refresh, dataReady };
}
