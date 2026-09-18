using System;

namespace FMCentralBms.Plugins
{
    internal static class EmailAddress
    {
        public static bool IsValid(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Length <= 320 &&
                   value.Contains("@") && value.IndexOfAny(new[] { ' ', '\r', '\n' }) < 0;
        }
    }
}
