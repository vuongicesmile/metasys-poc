import type { GeneratedComponentProps } from "../../RuntimeTypes";
import type { BoundedCount, DashboardData, ReadablePoint, ReadableSyncRequest, ReadableSpoFile } from "../models/dashboard";

export const MASTER_COUNT_LIMIT = 100;
export const ELASTIC_COUNT_LIMIT = 25;
export const LATEST_LIMIT = 5;
export function createBoundedCount(observed: number, hasMoreRows: boolean): BoundedCount {
    return {
        observed,
        label: hasMoreRows ? `${observed}+` : String(observed),
        isBounded: hasMoreRows,
    };
}
export async function queryDashboard(dataApi: GeneratedComponentProps["dataApi"]): Promise<DashboardData> {
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
