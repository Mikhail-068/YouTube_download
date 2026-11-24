using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YoutubeDownloader;

internal static class YoutubeSessionSettings
{
    private sealed record StoredSelection(string? Browser, string? ProfilePath, string? CookieFilePath);

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YoutubeDownloader", "browser-session.json");

    public static (BrowserSession? Browser, string? CookieFile) Load(string path)
    {
        try
        {
            var selection = JsonSerializer.Deserialize<StoredSelection>(File.ReadAllText(path));
            if (selection?.CookieFilePath is { } file && Path.IsPathFullyQualified(file))
                return (null, file);

            if (selection?.Browser is { } browser && selection.ProfilePath is { } profile &&
                BrowserSession.Browsers.Any(item => item.Id == browser) && Path.IsPathFullyQualified(profile))
                return (new BrowserSession(browser, profile), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unavailable settings leave the app in guest mode.
        }

        return (null, null);
    }

    public static void Save(string path, BrowserSession? browser, string? cookieFile)
    {
        if (browser is null && cookieFile is null)
        {
            try { File.Delete(path); }
            catch (DirectoryNotFoundException) { }
            return;
        }

        var selection = new StoredSelection(browser?.Browser, browser?.ProfilePath, cookieFile);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(selection, WriteOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
