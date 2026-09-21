import { IPointNavigation } from "../../Business/Abstractions/IPointNavigation";

/** Adapter bao bọc navigation API để UI không phụ thuộc URL hay organization ID. */
export class PcfPointNavigationAdapter implements IPointNavigation {
    private navigation: ComponentFramework.Navigation;

    public constructor(navigation: ComponentFramework.Navigation) {
        this.navigation = navigation;
    }

    /** Power Apps có thể thay context giữa các lần updateView nên luôn nhận navigation mới nhất. */
    public update(navigation: ComponentFramework.Navigation): void {
        this.navigation = navigation;
    }

    public async openPoint(id: string): Promise<void> {
        if (!id) {
            return;
        }

        await this.navigation.openForm({ entityName: "fmc_bmspoint", entityId: id });
    }
}
