import { createContext, useContext, useEffect, useState } from "react";
import { LANGUAGE_KEY, readLanguage, translate } from "../services/localization";
import type { Language } from "../services/localization";

// Context truyền ngôn ngữ xuống các component mà không cần truyền prop qua từng lớp.
export const LanguageContext = createContext<Language>("en");
export function useTranslation() {
    // Trả về hàm dịch ngắn gọn để component chỉ cần gọi t("text").
    const language = useContext(LanguageContext);
    return (text: string) => translate(text, language);
}
export function useLanguagePreference() {
    // Khởi tạo từ localStorage; nếu host chặn storage, localization tự fallback English.
    const [language, setLanguage] = useState<Language>(readLanguage);
    const changeLanguage = (value: string) => {
        // Chỉ chấp nhận vi/en để state không rơi vào ngôn ngữ không được hỗ trợ.
        const next: Language = value === "vi" ? "vi" : "en";
        setLanguage(next);
        try { window.localStorage.setItem(LANGUAGE_KEY, next); } catch { /* Session-only if storage is blocked. */ }
    };
    useEffect(() => {
        // Đồng bộ lựa chọn nếu user đổi ngôn ngữ ở một tab khác.
        const onStorage = (event: StorageEvent) => {
            if (event.key === LANGUAGE_KEY || event.key === null) setLanguage(readLanguage());
        };
        window.addEventListener("storage", onStorage);
        return () => window.removeEventListener("storage", onStorage);
    }, []);
    return { language, changeLanguage };
}
