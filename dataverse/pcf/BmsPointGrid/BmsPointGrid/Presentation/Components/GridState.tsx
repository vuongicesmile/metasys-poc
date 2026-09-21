import * as React from "react";
import { Spinner } from "@fluentui/react-components";

/** Trạng thái load/empty/error được tách để table chính chỉ lo hiển thị rows. */
export function GridState(props: {
    readonly isLoading: boolean;
    readonly errorMessage?: string;
    readonly loadingText: string;
    readonly emptyText: string;
    readonly errorPrefix: string;
}): React.ReactElement | null {
    if (props.isLoading) {
        return <div className="fmc-point-grid__state" role="status"><Spinner size="small" /> {props.loadingText}</div>;
    }

    if (props.errorMessage) {
        return <div className="fmc-point-grid__state fmc-point-grid__state--error" role="alert">{props.errorPrefix}: {props.errorMessage}</div>;
    }

    return <div className="fmc-point-grid__state">{props.emptyText}</div>;
}
