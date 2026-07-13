$ErrorActionPreference = 'Stop'
$tools = Join-Path $PSScriptRoot '..\tools'
New-Item -ItemType Directory -Force -Path $tools | Out-Null

# Official yt-dlp release asset. The application can subsequently update it itself.
Invoke-WebRequest 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' -OutFile (Join-Path $tools 'yt-dlp.exe')

$archive = Join-Path $env:TEMP 'yt-dlp-ffmpeg-win64.zip'
Invoke-WebRequest 'https://github.com/yt-dlp/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip' -OutFile $archive
$unpack = Join-Path $env:TEMP ('yt-dlp-ffmpeg-' + [Guid]::NewGuid())
Expand-Archive -Path $archive -DestinationPath $unpack -Force
Get-ChildItem -Path $unpack -Recurse -Filter ffmpeg.exe | Select-Object -First 1 | Copy-Item -Destination (Join-Path $tools 'ffmpeg.exe') -Force
Get-ChildItem -Path $unpack -Recurse -Filter ffprobe.exe | Select-Object -First 1 | Copy-Item -Destination (Join-Path $tools 'ffprobe.exe') -Force
Remove-Item -Path $archive,$unpack -Recurse -Force

Write-Host "yt-dlp, ffmpeg and ffprobe downloaded to $tools"
