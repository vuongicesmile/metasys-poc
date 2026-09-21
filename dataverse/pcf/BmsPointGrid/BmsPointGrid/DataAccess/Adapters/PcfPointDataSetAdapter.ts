import { IPointDataSource } from "../../Business/Abstractions/IPointDataSource";
import { PointDataSetSnapshot } from "../../Domain/Models/PointSnapshot";
import { mapPointRecord } from "../Mappers/PointRecordMapper";

type DataSet = ComponentFramework.PropertyTypes.DataSet;

/**
 * Adapter duy nhất biết DataSet API của PCF.
 * View/subgrid của host quyết định filter, sort và quyền; control chỉ đọc kết quả đã cấp.
 */
export class PcfPointDataSetAdapter implements IPointDataSource {
    private dataSet?: DataSet;
    private isPagingRequestInFlight = false;

    /** updateView gọi hàm này trước khi Presentation đọc snapshot mới. */
    public update(dataSet: DataSet): void {
        this.dataSet = dataSet;

        // Power Apps gửi updateView khi paging hoàn thành; lúc đó mới cho phép yêu cầu trang tiếp theo.
        if (!dataSet.loading) {
            this.isPagingRequestInFlight = false;
        }
    }

    public getSnapshot(): PointDataSetSnapshot {
        if (!this.dataSet) {
            return emptySnapshot();
        }

        const rows = this.dataSet.sortedRecordIds
            .map((id) => this.dataSet?.records[id])
            .filter((record): record is ComponentFramework.PropertyHelper.DataSetApi.EntityRecord => record !== undefined)
            .map((record) => mapPointRecord(record));

        const totalResultCount = this.dataSet.paging.totalResultCount;
        return {
            rows,
            isLoading: this.dataSet.loading,
            errorMessage: this.dataSet.error ? this.dataSet.errorMessage ?? "Power Apps could not load points." : undefined,
            hasNextPage: this.dataSet.paging.hasNextPage,
            hasPreviousPage: this.dataSet.paging.hasPreviousPage,
            // -1 nghĩa host không biết tổng, không hiển thị thành một số giả.
            totalResultCount: totalResultCount >= 0 ? totalResultCount : undefined
        };
    }

    public refresh(): void {
        if (!this.dataSet || this.dataSet.loading) {
            return;
        }

        // refresh/reset do host thực hiện và sẽ quay lại updateView với snapshot mới.
        this.dataSet.refresh();
    }

    public loadNextPage(): void {
        if (!this.canChangePage(this.dataSet?.paging.hasNextPage)) {
            return;
        }

        this.isPagingRequestInFlight = true;
        this.dataSet?.paging.loadNextPage();
    }

    public loadPreviousPage(): void {
        if (!this.canChangePage(this.dataSet?.paging.hasPreviousPage)) {
            return;
        }

        this.isPagingRequestInFlight = true;
        this.dataSet?.paging.loadPreviousPage();
    }

    private canChangePage(hasRequestedPage: boolean | undefined): boolean {
        return Boolean(this.dataSet && hasRequestedPage && !this.dataSet.loading && !this.isPagingRequestInFlight);
    }
}

/** Giá trị an toàn trong lần render hiếm hoi trước khi dataset được Power Apps cấp. */
function emptySnapshot(): PointDataSetSnapshot {
    return { rows: [], isLoading: true, hasNextPage: false, hasPreviousPage: false };
}
