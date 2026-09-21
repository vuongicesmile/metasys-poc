/** Cổng điều hướng; UI chỉ biết mở Point theo ID, không tự ghép URL môi trường. */
export interface IPointNavigation {
    openPoint(id: string): Promise<void>;
}
