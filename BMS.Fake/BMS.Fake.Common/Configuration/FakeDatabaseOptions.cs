namespace BMS.Fake.Common.Configuration;

/// <summary>Cấu hình kết nối tới database riêng của BMS Fake.</summary>
public sealed record FakeDatabaseOptions(
    // Connection string không chứa secret thật trong source control.
    string ConnectionString);
