import { PointDataSetSnapshot } from "../../Domain/Models/PointSnapshot";

/** Cổng đọc dataset để Business/Presentation không gọi API PCF trực tiếp. */
export interface IPointDataSource {
    getSnapshot(): PointDataSetSnapshot;
    refresh(): void;
    loadNextPage(): void;
    loadPreviousPage(): void;
}
