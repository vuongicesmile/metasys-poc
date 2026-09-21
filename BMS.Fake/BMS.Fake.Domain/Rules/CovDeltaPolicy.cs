namespace BMS.Fake.Domain.Rules;

/// <summary>Rule thuần chuyển random roll của simulator thành point delta.</summary>
public static class CovDeltaPolicy
{
    /// <summary>
    /// Temperature thay đổi theo phần mười; các loại khác thay đổi theo phần trăm.
    /// Hàm không đọc hoặc sửa state nên có thể kiểm tra độc lập.
    /// </summary>
    public static decimal Calculate(string objectType, int randomRoll) =>
        // So sánh không phân biệt hoa thường để source không phụ thuộc format chữ.
        string.Equals(objectType, "Temperature", StringComparison.OrdinalIgnoreCase)
            // Ví dụ roll 2 thành delta 0.2 độ.
            ? randomRoll / 10m
            // Ví dụ roll 15 thành delta 0.15 đơn vị.
            : randomRoll / 100m;
}
