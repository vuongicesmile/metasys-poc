import { translate } from "./localization";
import type { Language } from "./localization";

export type NavigationApi = {
    Navigation?: {
        navigateTo?: (input: Record<string, unknown>, options?: { target?: 1 | 2 }) => Promise<unknown>;
    };
    Utility?: {
        getGlobalContext?: () => { userSettings?: { userName?: string } };
    };
};
export function getSignedInUserName(language: Language): string {
    const t = (text: string) => translate(text, language);
    try {
        const xrm = (window as unknown as { Xrm?: NavigationApi }).Xrm;
        return xrm?.Utility?.getGlobalContext?.().userSettings?.userName || t("Người dùng Microsoft Entra");
    } catch {
        return t("Người dùng Microsoft Entra");
    }
}
export async function openAppItem(input: Record<string, unknown>): Promise<void> {
    const xrm = (window as unknown as { Xrm?: NavigationApi }).Xrm;
    if (!xrm?.Navigation?.navigateTo) throw new Error("Navigation unavailable");
    await xrm.Navigation.navigateTo(input);
}
