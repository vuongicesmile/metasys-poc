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
} from "../../RuntimeTypes";

// Các alias này biến TableRow generated thành kiểu dễ đọc trong component dashboard.
export type ReadableBuilding = ReadableTableRow<fmc_bmsbuilding>;
export type ReadableEquipment = ReadableTableRow<fmc_bmsequipment>;
export type ReadablePoint = ReadableTableRow<fmc_bmspoint>;
export type ReadableReading = ReadableTableRow<fmc_bmsreading>;
export type ReadableSilverPoint = ReadableTableRow<cr3c8_silvernewbmspoint>;
export type ReadableSyncRequest = ReadableTableRow<fmc_syncrequest>;
export type ReadableSpoFile = ReadableTableRow<fmc_spofile>;
export type ReadableSpoImportRow = ReadableTableRow<fmc_spoimportrow>;

export type BoundedCount = {
    // label có thể có dấu "+" khi page API còn bản ghi ngoài giới hạn đọc.
    observed: number;
    label: string;
    isBounded: boolean;
};

export type DashboardData = {
    // Snapshot dùng chung cho KPI, bảng gần nhất và thông tin trạng thái pipeline.
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

export type DashboardState = {
    value: DashboardData | null;
    loading: boolean;
    error: string | null;
};
