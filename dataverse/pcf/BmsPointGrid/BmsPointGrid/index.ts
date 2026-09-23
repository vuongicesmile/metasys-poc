import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import { PointGridService } from "./Business/Services/PointGridService";
import { PcfPointDataSetAdapter } from "./DataAccess/Adapters/PcfPointDataSetAdapter";
import { PcfPointNavigationAdapter } from "./DataAccess/Adapters/PcfPointNavigationAdapter";
import { getText } from "./Common/Localization/messages";
import { PointGrid } from "./Presentation/Components/PointGrid";

/** Số phút mặc định trước khi UI gắn nhãn dữ liệu cũ. */
const DEFAULT_STALE_AFTER_MINUTES = 30;

export class BmsPointGrid implements ComponentFramework.ReactControl<IInputs, IOutputs> {
    /** Adapter giữ ranh giới giữa API Power Apps và code nghiệp vụ thuần. */
    private readonly pointDataSource = new PcfPointDataSetAdapter();

    /** Service chuyển dữ liệu Point thành DTO mà React cần để hiển thị. */
    private readonly pointGridService = new PointGridService();

    /** Adapter navigation được tạo ở init vì Power Apps mới cấp context tại thời điểm đó. */
    private navigation?: PcfPointNavigationAdapter;

    /**
     * Used to initialize the control instance. Controls can kick off remote server calls and other initialization actions here.
     * Data-set values are not initialized here, use updateView.
     * @param context The entire property bag available to control via Context Object; It contains values as set up by the customizer mapped to property names defined in the manifest, as well as utility functions.
     * @param notifyOutputChanged A callback method to alert the framework that the control has new outputs ready to be retrieved asynchronously.
     * @param state A piece of data that persists in one session for a single user. Can be set at any point in a controls life cycle by calling 'setControlState' in the Mode interface.
     */
    public init(
        context: ComponentFramework.Context<IInputs>,
        _notifyOutputChanged: () => void,
        _state: ComponentFramework.Dictionary
    ): void {
        // MVP chỉ đọc nên không có output; đặt dấu _ để nói rõ hai tham số này chưa cần dùng.
        this.navigation = new PcfPointNavigationAdapter(context.navigation);
    }

    /**
     * Called when any value in the property bag has changed. This includes field values, data-sets, global values such as container height and width, offline status, control metadata values such as label, visible, etc.
     * @param context The entire property bag available to control via Context Object; It contains values as set up by the customizer mapped to names defined in the manifest, as well as utility functions
     * @returns ReactElement root react element for the control
     */
    public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
        // updateView là điểm Power Apps đưa dataset mới vào; không tự gọi refresh tại đây để tránh render loop.
        this.pointDataSource.update(context.parameters.points);
        this.navigation?.update(context.navigation);

        const staleAfterMinutes = context.parameters.staleAfterMinutes.raw ?? DEFAULT_STALE_AFTER_MINUTES;
        const viewModel = this.pointGridService.createViewModel(
            this.pointDataSource.getSnapshot(),
            { now: new Date(), staleAfterMinutes }
        );

        return React.createElement(PointGrid, {
            model: viewModel,
            text: getText(context.userSettings.languageId),
            agentWebChatUrl: context.parameters.agentWebChatUrl.raw ?? undefined,
            onRefresh: () => this.pointDataSource.refresh(),
            onNextPage: () => this.pointDataSource.loadNextPage(),
            onPreviousPage: () => this.pointDataSource.loadPreviousPage(),
            onOpenPoint: (id: string) => this.navigation?.openPoint(id) ?? Promise.resolve()
        });
    }

    /**
     * It is called by the framework prior to a control receiving new data.
     * @returns an object based on nomenclature defined in manifest, expecting object[s] for property marked as "bound" or "output"
     */
    public getOutputs(): IOutputs {
        return {};
    }

    /**
     * Called when the control is to be removed from the DOM tree. Controls should use this call for cleanup.
     * i.e. cancelling any pending remote calls, removing listeners, etc.
     */
    public destroy(): void {
        // Adapter không tạo timer/listener; đặt undefined để instance không giữ context cũ sau khi control bị tháo.
        this.navigation = undefined;
    }
}
