
// Đọc label đã format bởi OData cho choice/status field.
export function getFormattedValue(row: Record<string, unknown>, column: string): string | undefined {
    const value = row[`${column}@OData.Community.Display.V1.FormattedValue`];
    return typeof value === "string" && value.length > 0 ? value : undefined;
}
// Chuẩn hóa search theo locale tiếng Việt để tìm kiếm không phân biệt hoa thường.
export function normalizeSearch(value: string): string {
    return value.trim().toLocaleLowerCase("vi-VN");
}

// Kiểm tra một row có khớp ít nhất một field đang được hiển thị hay không.
export function matchesSearch(values: Array<string | number | undefined>, search: string): boolean {
    if (!search) return true;
    return values.some((value) => String(value ?? "").toLocaleLowerCase("vi-VN").includes(search));
}
