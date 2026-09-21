/**
 * Dữ liệu Point thuần sau khi rời Power Apps dataset.
 * `undefined` có nghĩa cột/giá trị không có; không dùng 0 để thay cho dữ liệu thiếu.
 */
export interface PointSnapshot {
    readonly id: string;
    readonly objectId?: string;
    readonly name?: string;
    readonly currentValue?: number;
    readonly unit?: string;
    readonly lastReadingTimeUtc?: Date;
    readonly sourceSystem?: string;
}

/** Snapshot của một trang dataset. Nó không phụ thuộc React hay ComponentFramework. */
export interface PointDataSetSnapshot {
    readonly rows: readonly PointSnapshot[];
    readonly isLoading: boolean;
    readonly errorMessage?: string;
    readonly hasNextPage: boolean;
    readonly hasPreviousPage: boolean;
    readonly totalResultCount?: number;
}
