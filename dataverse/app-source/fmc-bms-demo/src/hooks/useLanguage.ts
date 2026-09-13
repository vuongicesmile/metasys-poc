import { createContext, useContext, useEffect, useState } from "react";
import { LANGUAGE_KEY, readLanguage, translate } from "../services/localization";
import type { Language } from "../services/localization";

export const LanguageContext = createContext<Language>("en");
export function useTranslation() {
    const language = useContext(LanguageContext);
    return (text: string) => translate(text, language);
}
export function useLanguagePreference() {
    const [language, setLanguage] = useState<Language>(readLanguage);
    const changeLanguage = (value: string) => {
        const next: Language = value === "vi" ? "vi" : "en";
        setLanguage(next);
        try { window.localStorage.setItem(LANGUAGE_KEY, next); } catch { /* Session-only if storage is blocked. */ }
    };
    useEffect(() => {
        const onStorage = (event: StorageEvent) => {
            if (event.key === LANGUAGE_KEY || event.key === null) setLanguage(readLanguage());
        };
        window.addEventListener("storage", onStorage);
        return () => window.removeEventListener("storage", onStorage);
    }, []);
    return { language, changeLanguage };
}
