import { IInputs, IOutputs } from "./generated/ManifestTypes";

/** Một nút neo tối giản: tìm vùng có scrollbar gần control nhất và cuộn xuống cuối. */
export class ScrollDownButton implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private container?: HTMLDivElement;
    private button?: HTMLButtonElement;

    public init(
        context: ComponentFramework.Context<IInputs>,
        _notifyOutputChanged: () => void,
        _state: ComponentFramework.Dictionary,
        container: HTMLDivElement
    ): void {
        this.container = container;
        this.button = document.createElement("button");
        this.button.type = "button";
        this.button.className = "fmc-scroll-down-button";
        this.button.textContent = "↓";
        this.button.addEventListener("click", this.scrollDown);
        container.appendChild(this.button);
        this.updateLabel(context.userSettings.languageId);
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this.updateLabel(context.userSettings.languageId);
    }

    public getOutputs(): IOutputs { return {}; }

    public destroy(): void {
        this.button?.removeEventListener("click", this.scrollDown);
        this.button = undefined;
        this.container = undefined;
    }

    private readonly scrollDown = (): void => {
        const target = this.findScrollableContainer();
        const reduceMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
        target.scrollTo({ top: target.scrollHeight, behavior: reduceMotion ? "auto" : "smooth" });
    };

    private findScrollableContainer(): Element {
        let current = this.container?.parentElement ?? null;
        while (current) {
            const overflowY = window.getComputedStyle(current).overflowY;
            if (/(auto|scroll|overlay)/.test(overflowY) && current.scrollHeight > current.clientHeight + 1) return current;
            current = current.parentElement;
        }
        return document.scrollingElement ?? document.documentElement;
    }

    private updateLabel(languageId: number | undefined): void {
        if (!this.button) return;
        const label = languageId === 1066 ? "Cuộn xuống cuối" : "Scroll to bottom";
        this.button.setAttribute("aria-label", label);
        this.button.title = label;
    }
}
