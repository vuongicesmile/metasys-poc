// Use the supported model-driven app host API. A Copilot Studio bot ID alone is
// not an M365 agent binding, so this opens the native pane without claiming that
// BMS Operations Assistant is the active agent.
type CopilotHost = {
    isM365CopilotEnabled?: () => Promise<boolean>;
    openM365CopilotPanel?: () => Promise<void>;
};

export async function openAgentChat(): Promise<"opened" | "unavailable"> {
    const copilot = (window as unknown as { Xrm?: { Copilot?: CopilotHost } }).Xrm?.Copilot;
    if (!copilot?.isM365CopilotEnabled || !copilot.openM365CopilotPanel) return "unavailable";
    if (!(await copilot.isM365CopilotEnabled())) return "unavailable";
    await copilot.openM365CopilotPanel();
    return "opened";
}
