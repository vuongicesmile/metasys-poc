import { PointDataSetSnapshot } from "../../Domain/Models/PointSnapshot";
import { classifyReadingFreshness } from "../../Domain/Policies/ReadingFreshness";
import { PointGridViewModel } from "../Contracts/PointGridDto";

/** Tham số nghiệp vụ của lần render, tách khỏi context Power Apps. */
export interface PointGridOptions {
    readonly now: Date;
    readonly staleAfterMinutes: number;
}

/** Chuyển dữ liệu thô thành DTO an toàn để màn hình dùng. */
export class PointGridService {
    public createViewModel(snapshot: PointDataSetSnapshot, options: PointGridOptions): PointGridViewModel {
        return {
            rows: snapshot.rows.map((point) => ({
                id: point.id,
                // Fallback chỉ phục vụ nhãn màn hình; không thay đổi Object ID lưu trong Dataverse.
                objectId: nonEmptyText(point.objectId) ?? "—",
                name: nonEmptyText(point.name) ?? nonEmptyText(point.objectId) ?? "(Unnamed Point)",
                currentValue: point.currentValue,
                unit: nonEmptyText(point.unit),
                lastReadingTimeUtc: point.lastReadingTimeUtc,
                sourceSystem: nonEmptyText(point.sourceSystem),
                freshness: classifyReadingFreshness(point.lastReadingTimeUtc, options.now, options.staleAfterMinutes)
            })),
            isLoading: snapshot.isLoading,
            errorMessage: snapshot.errorMessage,
            hasNextPage: snapshot.hasNextPage,
            hasPreviousPage: snapshot.hasPreviousPage,
            totalResultCount: snapshot.totalResultCount
        };
    }
}

/** Chuỗi toàn khoảng trắng không hữu ích trên UI nên được xem là dữ liệu thiếu. */
function nonEmptyText(value: string | undefined): string | undefined {
    if (value === undefined) {
        return undefined;
    }

    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : undefined;
}
