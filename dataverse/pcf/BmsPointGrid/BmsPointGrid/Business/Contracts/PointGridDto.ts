import { ReadingFreshness } from "../../Domain/Policies/ReadingFreshness";

/** DTO đã sẵn sàng cho table React, tách khỏi record thô của Power Apps. */
export interface PointGridRowDto {
    readonly id: string;
    readonly objectId: string;
    readonly name: string;
    readonly currentValue?: number;
    readonly unit?: string;
    readonly lastReadingTimeUtc?: Date;
    readonly sourceSystem?: string;
    readonly freshness: ReadingFreshness;
}

/** Trạng thái đầy đủ mà Presentation cần để render một lần. */
export interface PointGridViewModel {
    readonly rows: readonly PointGridRowDto[];
    readonly isLoading: boolean;
    readonly errorMessage?: string;
    readonly hasNextPage: boolean;
    readonly hasPreviousPage: boolean;
    readonly totalResultCount?: number;
}
