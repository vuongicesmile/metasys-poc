import * as React from "react";

/** Hiển thị số đến bốn chữ số thập phân mà không làm tròn/ghi lại dữ liệu nguồn. */
export function ReadingValue(props: { readonly value?: number; readonly unit?: string }): React.ReactElement {
    if (props.value === undefined) {
        return <span>—</span>;
    }

    const formatted = new Intl.NumberFormat(undefined, { maximumFractionDigits: 4 }).format(props.value);
    return <span>{props.unit ? `${formatted} ${props.unit}` : formatted}</span>;
}
