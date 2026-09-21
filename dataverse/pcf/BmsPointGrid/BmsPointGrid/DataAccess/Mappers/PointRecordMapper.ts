import { PointSnapshot } from "../../Domain/Models/PointSnapshot";

/**
 * Hình dạng nhỏ nhất cần dùng từ EntityRecord.
 * Khai báo này giúp mapper độc lập với PCF để unit test bằng object giả đơn giản.
 */
export interface PointRecord {
    getRecordId(): string;
    // Dataset có thể chứa lookup/boolean/array cho cột khác; mapper tự lọc đúng kiểu từng cột Point.
    getValue(columnName: string): unknown;
}

/** Chuyển EntityRecord của Power Apps thành model Domain thuần. */
export function mapPointRecord(record: PointRecord): PointSnapshot {
    return {
        id: record.getRecordId(),
        objectId: asText(record.getValue("fmc_objectid")),
        name: asText(record.getValue("fmc_name")),
        currentValue: asNumber(record.getValue("fmc_currentvalue")),
        unit: asText(record.getValue("fmc_unit")),
        lastReadingTimeUtc: asDate(record.getValue("fmc_lastreadingtime")),
        sourceSystem: asText(record.getValue("fmc_sourcesystem"))
    };
}

/** Chuỗi rỗng vẫn được giữ để Business quyết định fallback hiển thị. */
function asText(value: unknown): string | undefined {
    return typeof value === "string" ? value : undefined;
}

/** Chỉ nhận số hữu hạn; không đổi chuỗi lỗi hoặc giá trị thiếu thành 0. */
function asNumber(value: unknown): number | undefined {
    return typeof value === "number" && Number.isFinite(value) ? value : undefined;
}

/** Dataset trả Date cho cột Dataverse DateTime; clone để UI không sửa object gốc. */
function asDate(value: unknown): Date | undefined {
    if (!(value instanceof Date) || Number.isNaN(value.getTime())) {
        return undefined;
    }

    return new Date(value.getTime());
}
