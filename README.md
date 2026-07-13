# YouTube Downloader

Независимая WPF-оболочка над `yt-dlp` для Windows. Она не содержит ключей, не использует сторонние .NET-обёртки и передаёт аргументы через `ProcessStartInfo.ArgumentList`.

## Первый запуск

1. В PowerShell из папки проекта выполните `powershell -ExecutionPolicy Bypass -File .\scripts\Get-Dependencies.ps1`.
2. Соберите приложение: `dotnet publish -c Release -r win-x64 --self-contained false`.
3. Скопируйте каталог `tools` рядом с опубликованным `YoutubeDownloader.exe`.

Кнопка «Обновить yt-dlp» переключает официальный бинарник на nightly-канал: этот канал yt-dlp рекомендует пользователям при поломках на стороне сайтов. Используйте программу только для материалов, на скачивание которых у вас есть права.
