$ErrorActionPreference = 'Stop'
$tools = Join-Path $PSScriptRoot '..\tools'
New-Item -ItemType Directory -Force -Path $tools | Out-Null

# Official nightly asset. YouTube changes frequently, and yt-dlp recommends this
# channel for regular users because stable releases can become outdated quickly.
Invoke-WebRequest 'https://github.com/yt-dlp/yt-dlp-nightly-builds/releases/latest/download/yt-dlp.exe' -OutFile (Join-Path $tools 'yt-dlp.exe')

$denoArchive = Join-Path $env:TEMP 'deno-x86_64-pc-windows-msvc.zip'
$denoUnpack = Join-Path $env:TEMP ('deno-' + [Guid]::NewGuid())
try {
    Invoke-WebRequest 'https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip' -OutFile $denoArchive
    Expand-Archive -LiteralPath $denoArchive -DestinationPath $denoUnpack -Force
    Copy-Item -LiteralPath (Join-Path $denoUnpack 'deno.exe') -Destination (Join-Path $tools 'deno.exe') -Force
}
finally {
    if (Test-Path -LiteralPath $denoArchive) { Remove-Item -LiteralPath $denoArchive -Force }
    if (Test-Path -LiteralPath $denoUnpack) { Remove-Item -LiteralPath $denoUnpack -Recurse -Force }
}

$archive = Join-Path $env:TEMP 'yt-dlp-ffmpeg-win64.zip'
Invoke-WebRequest 'https://github.com/yt-dlp/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip' -OutFile $archive
$unpack = Join-Path $env:TEMP ('yt-dlp-ffmpeg-' + [Guid]::NewGuid())
Expand-Archive -Path $archive -DestinationPath $unpack -Force
Get-ChildItem -Path $unpack -Recurse -Filter ffmpeg.exe | Select-Object -First 1 | Copy-Item -Destination (Join-Path $tools 'ffmpeg.exe') -Force
Get-ChildItem -Path $unpack -Recurse -Filter ffprobe.exe | Select-Object -First 1 | Copy-Item -Destination (Join-Path $tools 'ffprobe.exe') -Force
Remove-Item -Path $archive,$unpack -Recurse -Force

Write-Host "yt-dlp, Deno, ffmpeg and ffprobe downloaded to $tools"
