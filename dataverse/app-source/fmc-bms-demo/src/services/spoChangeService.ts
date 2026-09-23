import type { GeneratedComponentProps } from "../../RuntimeTypes";
import type { ReadableSpoChangeRequest } from "../models/dashboard";

export const SPO_CHANGE_PENDING = 789112000;
export const SPO_CHANGE_LIMIT = 3;

export async function queryPendingSpoChanges(dataApi: GeneratedComponentProps["dataApi"]): Promise<ReadableSpoChangeRequest[]> {
    // The queue is deliberately small. Read a bounded page then filter locally
    // so the generated page remains compatible with hosts that omit OData filter support.
    const result = await dataApi.queryTable("fmc_spochangerequest", {
        select: [
            "fmc_spochangerequestid", "fmc_filename", "fmc_sharepointpath", "fmc_expectedetag",
            "fmc_detectedat", "fmc_dueat", "fmc_status", "fmc_errormessage",
        ],
        orderBy: "fmc_dueat asc",
        pageSize: 50,
    });
    return (result.rows as ReadableSpoChangeRequest[])
        .filter((row) => row.fmc_status === SPO_CHANGE_PENDING)
        .slice(0, SPO_CHANGE_LIMIT);
}

type GlobalContext = { getClientUrl?: () => string };
type Role = { name?: string };
type GlobalContextWithRoles = GlobalContext & { userSettings?: { roles?: { get?: () => Role[] } } };
type XrmLike = { Utility?: { getGlobalContext?: () => GlobalContextWithRoles } };

export function canClaimSpoChanges(): boolean {
    const roles = (window as unknown as { Xrm?: XrmLike }).Xrm?.Utility?.getGlobalContext?.().userSettings?.roles?.get?.() ?? [];
    return roles.some((role) =>
        role.name === "FMC BMS Demo Operator" || role.name === "System Administrator"
    );
}

export async function claimSpoChange(request: ReadableSpoChangeRequest): Promise<void> {
    const xrm = (window as unknown as { Xrm?: XrmLike }).Xrm;
    const baseUrl = xrm?.Utility?.getGlobalContext?.().getClientUrl?.();
    if (!baseUrl) throw new Error("Dataverse context is unavailable.");
    const clientRequestId = typeof crypto?.randomUUID === "function"
        ? crypto.randomUUID()
        : "00000000-0000-4000-8000-" + Date.now().toString(16).padStart(12, "0").slice(-12);
    const response = await fetch(baseUrl.replace(/\/$/, "") + "/api/data/v9.2/fmc_ClaimSpoChange", {
        method: "POST",
        credentials: "same-origin",
        headers: { Accept: "application/json", "Content-Type": "application/json", "OData-MaxVersion": "4.0", "OData-Version": "4.0" },
        body: JSON.stringify({
            ChangeRequestId: request.fmc_spochangerequestid,
            ExpectedETag: request.fmc_expectedetag,
            DispatchSource: "Manual",
            ClientRequestId: clientRequestId,
        }),
    });
    if (!response.ok) throw new Error("Dataverse rejected the immediate sync request.");
    const result = await response.json() as { Accepted?: boolean; Message?: string };
    if (!result.Accepted) throw new Error(result.Message || "Another dispatch already claimed this file.");
}
