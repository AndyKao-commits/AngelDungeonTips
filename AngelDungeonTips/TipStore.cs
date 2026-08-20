using System.Net.Http;
using System.Text.Json;

namespace AngelDungeonTips;

public enum CatalogSyncStatus
{
    /// <summary>Remote fetch applied (or already up to date).</summary>
    Updated,
    /// <summary>Remote fetch failed; local cache / packaged file used.</summary>
    FetchFailed
}

public static class TipStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HttpClient Http = CreateHttp();

    public static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AngelDungeonTips");

    public static string SettingsPath => Path.Combine(DataFolder, "settings.json");

    public static string PackagedDungeonsPath =>
        Path.Combine(AppContext.BaseDirectory, "Data", "dungeons.json");

    public static string UserDungeonsPath => Path.Combine(DataFolder, "dungeons.json");

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("AngelDungeonTips/1.0");
        return c;
    }

    /// <summary>
    /// Pull remote catalog into Documents cache. On any failure, leaves cache as-is.
    /// </summary>
    public static async Task<CatalogSyncStatus> SyncCatalogAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(DataFolder);
        EnsureUserCopy();

        string url = ResolveCatalogUrl();
        if (string.IsNullOrWhiteSpace(url))
            return CatalogSyncStatus.FetchFailed;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return CatalogSyncStatus.FetchFailed;

            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return CatalogSyncStatus.FetchFailed;

            var catalog = JsonSerializer.Deserialize<DungeonCatalog>(json, JsonOptions);
            if (catalog == null || catalog.Dungeons == null || catalog.Dungeons.Count == 0)
                return CatalogSyncStatus.FetchFailed;

            // Normalize / pretty-write so local cache is stable
            string normalized = JsonSerializer.Serialize(catalog, JsonOptions);
            string temp = UserDungeonsPath + ".tmp";
            await File.WriteAllTextAsync(temp, normalized, ct).ConfigureAwait(false);
            File.Copy(temp, UserDungeonsPath, overwrite: true);
            try { File.Delete(temp); } catch { /* ignore */ }

            return CatalogSyncStatus.Updated;
        }
        catch (OperationCanceledException)
        {
            return CatalogSyncStatus.FetchFailed;
        }
        catch
        {
            return CatalogSyncStatus.FetchFailed;
        }
    }

    public static DungeonCatalog LoadCatalog()
    {
        Directory.CreateDirectory(DataFolder);
        EnsureUserCopy();

        string path = File.Exists(UserDungeonsPath) ? UserDungeonsPath : PackagedDungeonsPath;
        if (!File.Exists(path))
            return new DungeonCatalog();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<DungeonCatalog>(json, JsonOptions)
                   ?? new DungeonCatalog();
        }
        catch
        {
            return new DungeonCatalog();
        }
    }

    public static AppSettings LoadSettings()
    {
        Directory.CreateDirectory(DataFolder);
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        try
        {
            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(DataFolder);
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }

    private static void EnsureUserCopy()
    {
        if (File.Exists(UserDungeonsPath)) return;
        if (!File.Exists(PackagedDungeonsPath)) return;
        File.Copy(PackagedDungeonsPath, UserDungeonsPath, overwrite: false);
    }

    /// <summary>
    /// Prefer Data/catalog.url next to the exe (one line, no UI). Else RemoteConfig constant.
    /// </summary>
    private static string ResolveCatalogUrl()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "catalog.url");
            if (File.Exists(path))
            {
                string? line = File.ReadAllLines(path)
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#'));
                if (!string.IsNullOrWhiteSpace(line) &&
                    !line.Contains("OWNER/REPO", StringComparison.OrdinalIgnoreCase))
                    return line;
            }
        }
        catch { /* ignore */ }

        string fallback = RemoteConfig.CatalogUrl?.Trim() ?? "";
        if (fallback.Contains("OWNER/REPO", StringComparison.OrdinalIgnoreCase))
            return "";
        return fallback;
    }
}
