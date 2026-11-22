using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace YoutubeDownloader;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    private Process? _activeProcess;
    private bool _cancelRequested;
    private static readonly HttpClient HttpClient = new();
    private readonly string _toolsDirectory;
    private readonly string _ytDlpPath;
    private readonly string _denoPath;

    public MainWindow()
    {
        InitializeComponent();
        _toolsDirectory = Path.Combine(AppContext.BaseDirectory, "tools");
        _ytDlpPath = Path.Combine(_toolsDirectory, "yt-dlp.exe");
        _denoPath = Path.Combine(_toolsDirectory, "deno.exe");
        FolderTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (SystemParameters.HighContrast)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        var enableDarkMode = 1;
        _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, in enableDarkMode, sizeof(int));

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // COLORREF encodes colors as 0x00BBGGRR.
            var caption = 0x00221B18; // #181B22
            var text = 0x00F2EEEE;    // #EEEEF2
            _ = DwmSetWindowAttribute(handle, DwmCaptionColor, in caption, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmTextColor, in text, sizeof(int));
        }
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, in int value, int valueSize);

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = FolderTextBox.Text, Description = "Выберите папку для файлов" };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            FolderTextBox.Text = dialog.SelectedPath;
    }

    private async void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        var url = GetUrl();
        if (url is null || !EnsureYtDlp()) return;

        try
        {
            SetBusy(true, "Получаю сведения о видео…");
            var output = await RunAsync(new[] { "--dump-single-json", "--no-playlist", "--no-warnings", url }, logOutput: false);
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() : "Без названия";
            var duration = root.TryGetProperty("duration_string", out var d) ? d.GetString() : null;
            var channel = root.TryGetProperty("channel", out var c) ? c.GetString() : null;
            var uploader = root.TryGetProperty("uploader", out var u) ? u.GetString() : null;
            var thumbnail = root.TryGetProperty("thumbnail", out var thumb) ? thumb.GetString() : null;
            VideoTitleTextBlock.Text = title ?? "Без названия";
            VideoDetailsTextBlock.Text = string.Join("  •  ", new[] { channel ?? uploader, duration }.Where(value => !string.IsNullOrWhiteSpace(value)));
            ThumbnailImage.Source = null;
            if (!string.IsNullOrWhiteSpace(thumbnail)) await LoadThumbnailAsync(thumbnail);
            StatusTextBlock.Text = $"{title}{(string.IsNullOrEmpty(channel) ? "" : $" — {channel}")}{(string.IsNullOrEmpty(duration) ? "" : $" ({duration})")}";
            AppendLog("Сведения получены.");
        }
        catch (OperationCanceledException) { StatusTextBlock.Text = "Отменено"; }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false, StatusTextBlock.Text); }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e) => await DownloadAsync(audioOnly: false);
    private async void AudioButton_Click(object sender, RoutedEventArgs e) => await DownloadAsync(audioOnly: true);

    private async Task DownloadAsync(bool audioOnly)
    {
        var url = GetUrl();
        if (url is null || !EnsureYtDlp()) return;
        if (string.IsNullOrWhiteSpace(FolderTextBox.Text)) { ShowError("Выберите папку сохранения."); return; }

        try
        {
            Directory.CreateDirectory(FolderTextBox.Text);
            SetBusy(true, audioOnly ? "Скачиваю аудио…" : "Скачиваю видео…");
            var arguments = new List<string>
            {
                "--newline", "--restrict-filenames", "--no-overwrites",
                "--ffmpeg-location", _toolsDirectory,
                "-o", Path.Combine(FolderTextBox.Text, "%(title)s [%(id)s].%(ext)s")
            };
            if (audioOnly)
                arguments.AddRange(new[] { "-x", "--audio-format", "mp3" });
            else
                arguments.AddRange(new[] { "-f", "bv*+ba/b" });
            arguments.Add(url);
            await RunAsync(arguments, logOutput: true);
            StatusTextBlock.Text = "Готово";
        }
        catch (OperationCanceledException) { StatusTextBlock.Text = "Отменено"; }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false, StatusTextBlock.Text); }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureYtDlp()) return;
        try
        {
            SetBusy(true, "Обновляю yt-dlp до nightly…");
            await RunAsync(new[] { "--update-to", "nightly" }, logOutput: true);
            StatusTextBlock.Text = "yt-dlp обновлён";
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { SetBusy(false, StatusTextBlock.Text); }
    }

    private async Task<string> RunAsync(IEnumerable<string> arguments, bool logOutput)
    {
        var startInfo = new ProcessStartInfo(_ytDlpPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in new[]
        {
            "--retries", "10",
            "--fragment-retries", "10",
            "--extractor-retries", "10",
            "--retry-sleep", "http:exp=1:10",
            "--retry-sleep", "extractor:exp=1:10",
            "--extractor-args", "youtube:player_client=default,web_embedded"
        })
            startInfo.ArgumentList.Add(argument);

        if (File.Exists(_denoPath))
        {
            startInfo.ArgumentList.Add("--js-runtimes");
            startInfo.ArgumentList.Add($"deno:{_denoPath}");
        }
        else
        {
            AppendLog("Предупреждение: tools\\deno.exe не найден. Некоторые видео YouTube могут быть недоступны.");
        }

        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _cancelRequested = false;
        _activeProcess = process;
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); if (logOutput) AppendLog(e.Data); } };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            AppendLog(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
        }
        finally
        {
            _activeProcess = null;
        }

        if (_cancelRequested)
            throw new OperationCanceledException();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(CreateYtDlpErrorMessage(process.ExitCode, stderr.ToString()));

        return stdout.ToString();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProcess is not { HasExited: false } process) return;

        try
        {
            process.Kill(entireProcessTree: true);
            _cancelRequested = true;
            AppendLog("Отмена по запросу пользователя…");
        }
        catch (InvalidOperationException)
        {
            // Процесс успел завершиться между проверкой HasExited и вызовом Kill.
        }
    }

    private static string CreateYtDlpErrorMessage(int exitCode, string errorOutput)
    {
        var errorLines = errorOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var detail = errorLines.LastOrDefault();

        if (errorOutput.Contains("Failed to resolve", StringComparison.OrdinalIgnoreCase) ||
            errorOutput.Contains("getaddrinfo failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Не удалось найти адрес YouTube через DNS. Проверьте подключение к интернету и повторите попытку. " +
                   "Приложение уже выполнило 10 повторов соединения.";
        }

        if (errorOutput.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            return "YouTube не ответил вовремя. Проверьте подключение к интернету и повторите попытку.";

        if (errorOutput.Contains("sign in", StringComparison.OrdinalIgnoreCase))
            return "YouTube разрешает доступ к этому видео только после входа в аккаунт. Приложение не использует cookies браузера автоматически.";

        if (errorOutput.Contains("video is not available", StringComparison.OrdinalIgnoreCase) ||
            errorOutput.Contains("video unavailable", StringComparison.OrdinalIgnoreCase))
            return "YouTube не предоставил доступ к этому видео ни через основной, ни через встроенный проигрыватель. Возможно, видео удалено или ограничено владельцем, возрастом либо регионом.";

        return string.IsNullOrWhiteSpace(detail)
            ? $"yt-dlp завершился с кодом {exitCode}. Подробности сохранены в журнале."
            : detail;
    }

    private string? GetUrl()
    {
        var value = UrlTextBox.Text.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) return value;
        ShowError("Введите корректную ссылку http или https.");
        return null;
    }

    private bool EnsureYtDlp()
    {
        if (File.Exists(_ytDlpPath)) return true;
        ShowError($"Не найден {_ytDlpPath}. Запустите scripts\\Get-Dependencies.ps1 или поместите yt-dlp.exe в папку tools.");
        return false;
    }

    private void SetBusy(bool busy, string status)
    {
        InfoButton.IsEnabled = !busy; DownloadButton.IsEnabled = !busy; AudioButton.IsEnabled = !busy; UpdateButton.IsEnabled = !busy; CancelButton.IsEnabled = busy;
        StatusTextBlock.Text = status;
        ActivityProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task LoadThumbnailAsync(string thumbnailUrl)
    {
        try
        {
            var bytes = await HttpClient.GetByteArrayAsync(thumbnailUrl);
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            ThumbnailImage.Source = bitmap;
        }
        catch
        {
            ThumbnailImage.Source = null;
        }
    }

    private void AppendLog(string text) => Dispatcher.Invoke(() => { LogTextBox.AppendText(text + Environment.NewLine); LogTextBox.ScrollToEnd(); });
    private void ShowError(string message) { StatusTextBlock.Text = "Ошибка"; AppendLog(message); System.Windows.MessageBox.Show(message, "YouTube Downloader", MessageBoxButton.OK, MessageBoxImage.Error); }
}
