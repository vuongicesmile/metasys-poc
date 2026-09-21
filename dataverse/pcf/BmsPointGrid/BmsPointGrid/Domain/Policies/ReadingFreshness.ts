/** Chỉ là nhãn trình bày; không phải alarm hay trạng thái vận hành của thiết bị. */
export type ReadingFreshness = "fresh" | "stale" | "unknown";

/**
 * So sánh bằng UTC để kết quả không thay đổi theo timezone máy người dùng.
 * Reading ở tương lai được xem là fresh thay vì stale do clock lệch.
 */
export function classifyReadingFreshness(
    lastReadingTimeUtc: Date | undefined,
    now: Date,
    staleAfterMinutes: number
): ReadingFreshness {
    if (!lastReadingTimeUtc || Number.isNaN(lastReadingTimeUtc.getTime())) {
        return "unknown";
    }

    const safeMinutes = Number.isFinite(staleAfterMinutes) && staleAfterMinutes > 0
        ? staleAfterMinutes
        : 30;
    const ageMilliseconds = now.getTime() - lastReadingTimeUtc.getTime();
    return ageMilliseconds >= safeMinutes * 60_000 ? "stale" : "fresh";
}
