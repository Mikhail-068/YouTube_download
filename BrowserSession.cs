using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YoutubeDownloader;

internal sealed record BrowserProfile(string Path, string Name);

internal sealed record BrowserSession(string Browser, string ProfilePath)
{
    public static IReadOnlyList<(string Id, string Name)> Browsers { get; } =
        new[] { ("firefox", "Firefox"), ("chrome", "Google Chrome"), ("edge", "Microsoft Edge") };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YoutubeDownloader", "browser-session.json");

    [JsonIgnore]
    public string BrowserName => Browsers.First(browser => browser.Id == Browser).Name;

    [JsonIgnore]
    public string Argument => $"{Browser}:{ProfilePath}";

    public static BrowserSession? Load(string path)
    {
        try
        {
            var session = JsonSerializer.Deserialize<BrowserSession>(File.ReadAllText(path));
            return session is not null &&
                   Browsers.Any(browser => browser.Id == session.Browser) &&
                   Path.IsPathFullyQualified(session.ProfilePath ?? string.Empty) ? session : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (JsonException) { return null; }
    }

    public static void Save(string path, BrowserSession? session)
    {
        if (session is null)
        {
            File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(session));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public static string GetProfileRoot(string browser)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return browser switch
        {
            "firefox" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                      "Mozilla", "Firefox", "Profiles"),
            "chrome" => Path.Combine(local, "Google", "Chrome", "User Data"),
            "edge" => Path.Combine(local, "Microsoft", "Edge", "User Data"),
            _ => throw new ArgumentOutOfRangeException(nameof(browser))
        };
    }

    public static IReadOnlyList<BrowserProfile> FindProfiles(string browser, string root)
    {
        try
        {
            var names = ReadProfileNames(browser, root);
            var profiles = Directory.EnumerateDirectories(root)
                .Where(path => IsProfile(browser, path))
                .Select(path => new BrowserProfile(path, names.GetValueOrDefault(path) ?? Path.GetFileName(path)))
                .ToArray();
            return profiles.GroupBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .SelectMany(group => group.Count() == 1
                    ? group
                    : group.Select(profile => profile with { Name = $"{profile.Name} ({Path.GetFileName(profile.Path)})" }))
                .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (IOException) { return Array.Empty<BrowserProfile>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<BrowserProfile>(); }
    }

    public static bool IsProfile(string browser, string path)
    {
        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        if (browser != "firefox" &&
            (folder.Equals("System Profile", StringComparison.OrdinalIgnoreCase) ||
             folder.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase)))
            return false;

        return File.Exists(Path.Combine(path, browser == "firefox" ? "prefs.js" : "Preferences"));
    }

    private static Dictionary<string, string> ReadProfileNames(string browser, string root)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (browser == "firefox")
                ReadFirefoxNames(root, names);
            else
                ReadChromiumNames(root, names);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Folder names remain usable if the browser metadata is missing or unreadable.
        }
        return names;
    }

    private static void ReadChromiumNames(string root, Dictionary<string, string> names)
    {
        using var stream = File.Open(Path.Combine(root, "Local State"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var state = JsonDocument.Parse(stream);
        if (!TryReadProperty(state.RootElement, "profile", out var profile) ||
            !TryReadProperty(profile, "info_cache", out var entries) ||
            entries.ValueKind != JsonValueKind.Object)
            return;

        foreach (var entry in entries.EnumerateObject())
        {
            if (entry.Name is "." or ".." || entry.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                continue;

            var localName = ReadName(entry.Value, "enterprise_label") ?? ReadName(entry.Value, "name");
            var accountName = ReadName(entry.Value, "gaia_given_name") ?? ReadName(entry.Value, "gaia_name");
            var usingDefaultName = TryReadProperty(entry.Value, "is_using_default_name", out var flag) &&
                                   flag.ValueKind == JsonValueKind.True;
            var displayName = accountName ?? localName;
            if (accountName is not null && localName is not null && !usingDefaultName &&
                !accountName.Equals(localName, StringComparison.CurrentCultureIgnoreCase))
                displayName = $"{accountName} ({localName})";

            if (displayName is not null)
                names[Path.Combine(root, entry.Name)] = displayName;
        }
    }

    private static bool TryReadProperty(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out value);
    }

    private static string? ReadName(JsonElement element, string property)
    {
        if (!TryReadProperty(element, property, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        var name = value.GetString()?.Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    private static void ReadFirefoxNames(string root, Dictionary<string, string> names)
    {
        var catalogRoot = Path.GetDirectoryName(root)!;
        var isProfile = false;
        string? name = null;
        string? path = null;

        void RecordProfile()
        {
            if (!isProfile || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
                return;

            try { names[Path.GetFullPath(path, catalogRoot)] = name; }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
        }

        foreach (var entry in File.ReadLines(Path.Combine(catalogRoot, "profiles.ini")))
        {
            var line = entry.Trim();
            if (line.StartsWith('['))
            {
                RecordProfile();
                isProfile = line.StartsWith("[Profile", StringComparison.OrdinalIgnoreCase);
                name = null;
                path = null;
            }
            else if (line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                name = line[5..].Trim();
            else if (line.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
                path = line[5..].Trim();
        }
        RecordProfile();
    }
}
