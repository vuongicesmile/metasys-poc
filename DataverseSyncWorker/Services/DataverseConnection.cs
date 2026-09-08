using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using DataverseSyncWorker.Models;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace DataverseSyncWorker.Services;

public sealed class DataverseConnection(SyncOptions options) : IDisposable
{
    private ServiceClient? _client;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _developerToken;
    private DateTimeOffset _developerTokenValidUntil;
    public ServiceClient Get()
    {
        if (_client is { IsReady: true }) return _client;
        if (!options.HasCredentials) throw new InvalidOperationException("Dataverse credentials are not configured. Set ClientId and ClientSecret or CertificateThumbprint.");
        _client?.Dispose();
        if (options.UsesDeveloperToken)
        {
            _client = new ServiceClient(new Uri(options.Url), GetDeveloperToken, true, null);
        }
        else
        {
            var cs = new DbConnectionStringBuilder
            {
                ["Url"] = options.Url,
                ["ClientId"] = options.ClientId,
                ["RequireNewInstance"] = true
            };
            if (!string.IsNullOrWhiteSpace(options.CertificateThumbprint))
            {
                cs["AuthType"] = "Certificate";
                cs["Thumbprint"] = options.CertificateThumbprint;
                cs["StoreName"] = "My";
                cs["StoreLocation"] = options.CertificateStoreLocation;
            }
            else { cs["AuthType"] = "ClientSecret"; cs["ClientSecret"] = options.ClientSecret; }
            _client = new ServiceClient(cs.ConnectionString);
        }
        if (!_client.IsReady)
        {
            _client.Dispose(); _client = null;
            throw new InvalidOperationException("Dataverse authentication failed. Check app credentials, expiry and application-user role.");
        }
        var who = (WhoAmIResponse)_client.Execute(new WhoAmIRequest());
        if (who.OrganizationId != options.ExpectedOrganizationId)
        {
            _client.Dispose(); _client = null;
            throw new InvalidOperationException("Dataverse organization differs from ExpectedOrganizationId. No writes were made.");
        }
        return _client;
    }

    private async Task<string> GetDeveloperToken(string _)
    {
        if (_developerToken is not null && _developerTokenValidUntil > DateTimeOffset.UtcNow.AddMinutes(5))
            return _developerToken;
        await _tokenGate.WaitAsync();
        try
        {
            if (_developerToken is not null && _developerTokenValidUntil > DateTimeOffset.UtcNow.AddMinutes(5))
                return _developerToken;
            var start = new ProcessStartInfo
            {
                FileName = options.DeveloperTokenPython,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(options.DeveloperTokenScript);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Dataverse developer token helper.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var token = (await stdout).Trim();
            _ = await stderr;
            if (process.ExitCode != 0 || token.Count(c => c == '.') != 2)
                throw new InvalidOperationException("Dataverse developer token acquisition failed. Re-authenticate the rmit-fm-data Azure CLI session.");
            _developerToken = token;
            _developerTokenValidUntil = ReadExpiry(token);
            return token;
        }
        finally { _tokenGate.Release(); }
    }

    private static DateTimeOffset ReadExpiry(string token)
    {
        try
        {
            var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return DateTimeOffset.FromUnixTimeSeconds(json.RootElement.GetProperty("exp").GetInt64());
        }
        catch { return DateTimeOffset.UtcNow.AddMinutes(30); }
    }
    public void Dispose()
    {
        _client?.Dispose();
        _tokenGate.Dispose();
    }
}
