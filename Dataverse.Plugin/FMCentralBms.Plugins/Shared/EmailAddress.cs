using System;

namespace FMCentralBms.Plugins
{
    internal static class EmailAddress
    {
        // Đây là kiểm tra tối thiểu trước khi tạo outbox; validation sâu hơn thuộc connector/email service.
        public static bool IsValid(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= 320 &&
                   value.Contains("@") && value.IndexOfAny(new[] { ' ', '\r', '\n' }) < 0;
        }
    }
}
