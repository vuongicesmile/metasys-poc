using System.Text.Json;

namespace BmsIngestionApp.Models;

public sealed class AppSettings
{
    public MetasysSettings Metasys { get; init; } = new();
    public SqlSettings Sql { get; init; } = new();

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    public static AppSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The ingestion app configuration file was not found.", path);
        }

        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("appsettings.json is empty or invalid.");
    }
}

public sealed class MetasysSettings
{
    public string BaseUrl { get; init; } = "http://localhost:5100/";
}

public sealed class SqlSettings
{
    public bool Enabled { get; init; } = true;
    public string ConnectionString { get; init; } = "";
}
