<h1 align="center">
  <img src="docs/banner.png" alt="YouTube Downloader" width="960">
</h1>

<div align="center">
  <p>Скачивайте видео с YouTube или сохраняйте только звук в MP3.</p>
  <img src="https://img.shields.io/badge/Windows-x64-2563eb" alt="Windows x64">
  <img src="https://img.shields.io/badge/WPF-.NET%208-7842db" alt="WPF на .NET 8">
  <img src="https://img.shields.io/badge/YouTube-%D0%B2%D0%B8%D0%B4%D0%B5%D0%BE%20%2B%20MP3-ff0033" alt="Видео и MP3">
  <p><a href="#как-пользоваться">Как пользоваться</a> · <a href="#сборка-для-windows">Сборка для Windows</a> · <a href="#если-загрузка-не-удалась">Помощь с загрузкой</a></p>
</div>

## Как пользоваться

1. Вставьте ссылку на ролик и нажмите **«Проверить»**. В карточке появятся название, обложка, канал и длительность, если они доступны.
2. Нажмите жёлтый значок папки и выберите, куда сохранить файл.
3. Нажмите **«Скачать видео»**, чтобы сохранить ролик, или **«Скачать MP3»**, чтобы получить звуковую дорожку.
4. Следите за ходом загрузки в журнале под кнопками. Активную операцию можно прервать кнопкой **«Отменить»**.

<p align="center">
  <img src="docs/screen.png" alt="Окно YouTube Downloader с карточкой видео, выбором папки и кнопками скачивания видео и MP3" width="840">
</p>

## Сборка для Windows

Готового установщика в GitHub Releases пока нет. Для сборки понадобятся Windows x64, Git и [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Откройте PowerShell, подключитесь к интернету и выполните команды.

```powershell
git clone https://github.com/Mikhail-068/YouTube_download.git
Set-Location YouTube_download

powershell -ExecutionPolicy Bypass -File .\scripts\Get-Dependencies.ps1
dotnet publish .\YoutubeDownloader.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish

New-Item -ItemType Directory -Path .\publish\tools -Force | Out-Null
Copy-Item .\tools\*.exe -Destination .\publish\tools -Force
.\publish\YoutubeDownloader.exe
```

Приложение находится в `publish` и запускается с включённой в сборку .NET. При переносе на другой компьютер скопируйте вместе с EXE папку `tools` с файлами `yt-dlp.exe`, `deno.exe`, `ffmpeg.exe` и `ffprobe.exe`.

## Если загрузка не удалась

- Если ролик доступен в браузере, нажмите **«Обновить yt-dlp»**, дождитесь завершения и повторите попытку. Кнопка устанавливает свежую сборку из канала nightly.
- Если приложение не находит утилиты, проверьте папку `tools` рядом с EXE. Команда скачивания утилит приведена в разделе сборки.
- Если ошибка повторяется, загляните в журнал внизу окна. В нём выводятся сообщения yt-dlp во время загрузки.

## Как устроено приложение

| Компонент | Что делает |
| --- | --- |
| WPF и .NET 8 | Показывают окно, карточку видео и журнал |
| [yt-dlp](https://github.com/yt-dlp/yt-dlp) | Получает сведения о ролике и скачивает медиа |
| [Deno](https://deno.com/) | Выполняет JavaScript, который требуется yt-dlp для обработки ссылок YouTube |
| [FFmpeg и FFprobe](https://ffmpeg.org/) | Обрабатывают медиапотоки, в том числе при сохранении звука в MP3 |

Подробности есть в [описании архитектуры](docs/architecture/README.md). Сборка установщика описана в [сценарии NSIS](installer/YoutubeDownloader.nsi).

Скачивайте материалы, на которые у вас есть права, и соблюдайте [условия использования YouTube](https://www.youtube.com/t/terms).

Автор проекта — [Mikhail-068](https://github.com/Mikhail-068).
