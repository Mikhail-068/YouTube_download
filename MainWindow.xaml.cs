using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;

namespace YoutubeDownloader;

public partial class MainWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    private Process? _activeProcess;
    private bool _cancelRequested;
    private bool _usingYoutubeSession;
    private bool _closeRequested;
    private string? _youtubeCookiesPath;
    private BrowserSession? _browserSession;
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
        (_browserSession, _youtubeCookiesPath) = YoutubeSessionSettings.Load(YoutubeSessionSettings.SettingsPath);
        UpdateSessionStatus();
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

    private void SessionButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SessionButton, Placement = PlacementMode.Bottom };
        if (!SystemParameters.HighContrast)
            menu.Style = (Style)FindResource("SessionContextMenu");

        foreach (var browser in BrowserSession.Browsers)
        {
            var browserMenu = new MenuItem { Header = browser.Name };
            var root = BrowserSession.GetProfileRoot(browser.Id);
            var profiles = BrowserSession.FindProfiles(browser.Id, root);
            foreach (var profile in profiles)
            {
                var session = new BrowserSession(browser.Id, profile.Path);
                var item = new MenuItem
                {
                    Header = profile.Name.Replace("_", "__", StringComparison.Ordinal),
                    ToolTip = $"{profile.Name}\n{Path.GetFileName(profile.Path)}",
                    IsCheckable = true,
                    IsChecked = session == _browserSession
                };
                item.Click += (_, _) => SelectSession(session, null);
                browserMenu.Items.Add(item);
            }
            if (profiles.Count > 0) browserMenu.Items.Add(new Separator());
            var browse = new MenuItem { Header = "Выбрать папку профиля…" };
            browse.Click += (_, _) => ChooseBrowserProfile(browser.Id, root);
            browserMenu.Items.Add(browse);
            menu.Items.Add(browserMenu);
        }

        menu.Items.Add(new Separator());
        var file = new MenuItem { Header = "Выбрать файл cookies.txt…" };
        file.Click += (_, _) => ChooseCookieFile();
        menu.Items.Add(file);

        var disconnect = new MenuItem
        {
            Header = "Отключить сессию",
            IsEnabled = _browserSession is not null || _youtubeCookiesPath is not null
        };
        disconnect.Click += (_, _) => SelectSession(null, null);
        menu.Items.Add(disconnect);
        menu.IsOpen = true;
    }

    private void ChooseBrowserProfile(string browser, string root)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Выберите папку профиля браузера" };
        if (Directory.Exists(root)) dialog.InitialDirectory = root;
        if (dialog.ShowDialog(this) != true) return;

        if (BrowserSession.IsProfile(browser, dialog.FolderName))
            SelectSession(new BrowserSession(browser, dialog.FolderName), null);
        else
            ShowError("Выберите папку профиля, в котором вы вошли в YouTube.");
    }

    private void ChooseCookieFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите файл cookies.txt с сессией YouTube",
            Filter = "Файлы cookies (*.txt)|*.txt|Все файлы (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            YoutubeCookies.Read(dialog.FileName);
            SelectSession(null, dialog.FileName);
        }
        catch (InvalidDataException ex) { ShowError(ex.Message); }
        catch (IOException) { ShowError("Не удалось прочитать файл сессии YouTube."); }
        catch (UnauthorizedAccessException) { ShowError("Не удалось открыть файл сессии YouTube."); }
    }

    private void SelectSession(BrowserSession? browser, string? cookieFile)
    {
        try
        {
            YoutubeSessionSettings.Save(YoutubeSessionSettings.SettingsPath, browser, cookieFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("Не удалось сохранить выбор сессии YouTube. Проверьте доступ к настройкам приложения.");
            return;
        }

        _browserSession = browser;
        _youtubeCookiesPath = cookieFile;
        UpdateSessionStatus();
    }

    private void UpdateSessionStatus()
    {
        SessionStatusTextBlock.Text = _browserSession is { } browser
            ? $"Профиль {browser.BrowserName} выбран"
            : _youtubeCookiesPath is { } path && !File.Exists(path)
                ? "Файл сессии недоступен"
                : "Файл сессии подключён";
        SessionStatusTextBlock.Visibility = _browserSession is not null || _youtubeCookiesPath is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void InfoButton_Click(object sender, RoutedEventArgs e)
    {
        var url = GetUrl();
        if (url is null || !EnsureYtDlp()) return;

        try
        {
            SetBusy(true, "Получаю сведения о видео…");
            var output = await RunForUrlAsync(url, new[] { "--dump-single-json", "--no-playlist", "--no-warnings" }, logOutput: false);
            if (_closeRequested) return;
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
            await RunForUrlAsync(url, arguments, logOutput: true);
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

    private async Task<string> RunForUrlAsync(string url, IEnumerable<string> arguments, bool logOutput)
    {
        if (!YoutubeCookies.SupportsUrl(url))
            return await RunAsync(arguments.Append(url), logOutput);

        if (_browserSession is { } browser)
        {
            if (!BrowserSession.IsProfile(browser.Browser, browser.ProfilePath))
                throw new InvalidOperationException("Профиль браузера больше недоступен. Выберите другой профиль в меню «Сессия YouTube».");

            var sessionArguments = new[]
            {
                "--ignore-config", "--no-cookies", "--use-extractors", "(?i)^youtube.*$",
                "--cookies-from-browser", browser.Argument
            };
            _usingYoutubeSession = true;
            try
            {
                return await RunAsync(sessionArguments.Concat(arguments).Append(url), logOutput, usingYoutubeSession: true, browser: browser);
            }
            finally { _usingYoutubeSession = false; }
        }

        if (_youtubeCookiesPath is not { } sourcePath)
            return await RunAsync(arguments.Append(url), logOutput);

        string[] cookies;
        try { cookies = YoutubeCookies.Read(sourcePath); }
        catch (IOException) { throw new InvalidOperationException("Файл сессии YouTube недоступен. Выберите файл заново в меню «Сессия YouTube»."); }
        catch (UnauthorizedAccessException) { throw new InvalidOperationException("Доступ к файлу сессии YouTube закрыт. Выберите файл заново в меню «Сессия YouTube»."); }

        var directory = Directory.CreateTempSubdirectory("youtube-downloader-");
        var cookiePath = Path.Combine(directory.FullName, "cookies.txt");
        try
        {
            File.WriteAllLines(cookiePath, cookies);
            _usingYoutubeSession = true;
            var sessionArguments = new[] { "--no-cookies-from-browser", "--cookies", cookiePath };
            return await RunAsync(sessionArguments.Concat(arguments).Append(url), logOutput, usingYoutubeSession: true);
        }
        finally
        {
            _usingYoutubeSession = false;
            try
            {
                File.Delete(cookiePath);
                directory.Delete();
            }
            catch (IOException) { AppendLog("Не удалось удалить временный файл сессии YouTube."); }
            catch (UnauthorizedAccessException) { AppendLog("Не удалось удалить временный файл сессии YouTube."); }
        }
    }

    private async Task<string> RunAsync(IEnumerable<string> arguments, bool logOutput, bool usingYoutubeSession = false, BrowserSession? browser = null)
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
            throw new InvalidOperationException(CreateYtDlpErrorMessage(process.ExitCode, stderr.ToString(), usingYoutubeSession, browser));

        return stdout.ToString();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel || !_usingYoutubeSession) return;

        e.Cancel = true;
        _closeRequested = true;
        CancelActiveProcess();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => CancelActiveProcess();

    private void CancelActiveProcess()
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

    private static string CreateYtDlpErrorMessage(int exitCode, string errorOutput, bool usingYoutubeSession, BrowserSession? browser)
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

        if (browser is not null &&
            (errorOutput.Contains("cookie database", StringComparison.OrdinalIgnoreCase) ||
             errorOutput.Contains("cookies database", StringComparison.OrdinalIgnoreCase) ||
             errorOutput.Contains("failed to load cookies", StringComparison.OrdinalIgnoreCase) ||
             errorOutput.Contains("failed to decrypt", StringComparison.OrdinalIgnoreCase)))
        {
            return $"Не удалось прочитать сессию из {browser.BrowserName}. Можно выбрать другой браузер или файл cookies.txt в меню «Сессия YouTube».";
        }

        if (errorLines.Any(line => line.Contains("[youtube", StringComparison.OrdinalIgnoreCase) &&
                                   line.Contains("confirm you", StringComparison.OrdinalIgnoreCase) &&
                                   line.Contains("not a bot", StringComparison.OrdinalIgnoreCase)))
        {
            if (browser is not null)
                return $"YouTube запросил проверку «вы не бот». Откройте ролик в выбранном профиле {browser.BrowserName} и убедитесь, что вошли в аккаунт.";

            return usingYoutubeSession
                ? "YouTube запросил проверку «вы не бот», хотя файл сессии подключён. Проверьте доступ в браузере или выберите свежий файл cookies.txt."
                : "YouTube запросил проверку «вы не бот». Выберите браузер, в котором вы вошли в аккаунт, в меню «Сессия YouTube».";
        }

        if (errorOutput.Contains("sign in", StringComparison.OrdinalIgnoreCase) &&
            errorOutput.Contains("[youtube", StringComparison.OrdinalIgnoreCase))
        {
            if (browser is not null)
                return $"YouTube запросил вход. Проверьте доступ к ролику в выбранном профиле {browser.BrowserName}.";

            return usingYoutubeSession
                ? "YouTube запросил вход. Проверьте, что видео доступно этому аккаунту, и при необходимости выберите свежий файл cookies.txt."
                : "YouTube запросил вход. Подключите сессию YouTube, если ролик доступен вашему аккаунту.";
        }

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
        SessionButton.IsEnabled = !busy;
        StatusTextBlock.Text = status;
        ActivityProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy && _closeRequested) Close();
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
    private void ShowError(string message)
    {
        if (_closeRequested) return;
        StatusTextBlock.Text = "Ошибка";
        AppendLog(message);
        System.Windows.MessageBox.Show(message, "YouTube Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
