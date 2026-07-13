using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Imaging;

namespace YoutubeDownloader;

public partial class MainWindow : Window
{
    private Process? _activeProcess;
    private static readonly HttpClient HttpClient = new();
    private readonly string _toolsDirectory;
    private readonly string _ytDlpPath;

    public MainWindow()
    {
        InitializeComponent();
        _toolsDirectory = Path.Combine(AppContext.BaseDirectory, "tools");
        _ytDlpPath = Path.Combine(_toolsDirectory, "yt-dlp.exe");
        FolderTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

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
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _activeProcess = process;
        var stdout = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); if (logOutput) AppendLog(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) AppendLog(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        _activeProcess = null;
        if (process.ExitCode != 0) throw new InvalidOperationException($"yt-dlp завершился с кодом {process.ExitCode}. Подробности — в журнале.");
        return stdout.ToString();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _activeProcess?.Kill(entireProcessTree: true);

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
