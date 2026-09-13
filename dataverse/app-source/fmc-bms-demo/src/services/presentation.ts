

export function getFormattedValue(row: Record<string, unknown>, column: string): string | undefined {
    const value = row[`${column}@OData.Community.Display.V1.FormattedValue`];
    return typeof value === "string" && value.length > 0 ? value : undefined;
}
export function normalizeSearch(value: string): string {
    return value.trim().toLocaleLowerCase("vi-VN");
}

export function matchesSearch(values: Array<string | number | undefined>, search: string): boolean {
    if (!search) return true;
    return values.some((value) => String(value ?? "").toLocaleLowerCase("vi-VN").includes(search));
}
