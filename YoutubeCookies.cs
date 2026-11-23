using System.Globalization;
using System.IO;

namespace YoutubeDownloader;

internal static class YoutubeCookies
{
    private const string Header = "# Netscape HTTP Cookie File";
    private const string HttpOnlyPrefix = "#HttpOnly_";

    public static bool SupportsUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort
            && (IsYoutubeHost(uri.IdnHost) || uri.IdnHost.Equals("youtu.be", StringComparison.OrdinalIgnoreCase));
    }

    public static string[] Read(string path)
    {
        using var lines = File.ReadLines(path).GetEnumerator();
        if (!lines.MoveNext() || lines.Current is not (Header or "# HTTP Cookie File"))
            throw new InvalidDataException("Выберите файл cookies.txt в формате Netscape.");

        var youtubeLines = new List<string> { Header };
        while (lines.MoveNext())
        {
            var line = lines.Current;
            var httpOnly = line.StartsWith(HttpOnlyPrefix, StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(line) || (line.StartsWith('#') && !httpOnly))
                continue;

            var fields = (httpOnly ? line[HttpOnlyPrefix.Length..] : line).Split('\t');
            var domain = fields[0];
            var isDomainCookie = domain.StartsWith('.');
            if (!IsYoutubeHost(isDomainCookie ? domain[1..] : domain))
                continue;

            if (fields.Length != 7
                || fields[1] != (isDomainCookie ? "TRUE" : "FALSE")
                || !fields[2].StartsWith('/')
                || fields[3] is not ("TRUE" or "FALSE")
                || (fields[4].Length != 0 && !long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                || string.IsNullOrEmpty(fields[5]))
            {
                throw new InvalidDataException("В файле есть некорректная запись YouTube. Экспортируйте cookies заново.");
            }

            youtubeLines.Add(line);
        }

        if (youtubeLines.Count == 1)
            throw new InvalidDataException("В файле нет cookies для youtube.com.");

        return youtubeLines.ToArray();
    }

    private static bool IsYoutubeHost(string host) =>
        host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
}
