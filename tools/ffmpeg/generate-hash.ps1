#Requires -Version 5.1

$ErrorActionPreference = "Stop"

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$executablePath = Join-Path $scriptDirectory "ffmpeg.exe"
$hashFilePath = Join-Path $scriptDirectory "ffmpeg.exe.sha256"

if (-not (Test-Path $executablePath)) {
    throw "ffmpeg.exe not found at '$executablePath'. Place a Windows build of ffmpeg.exe in tools/ffmpeg/ before running this script."
}

$hash = (Get-FileHash -Path $executablePath -Algorithm SHA256).Hash.ToLowerInvariant()

Set-Content -Path $hashFilePath -Value $hash -NoNewline -Encoding ascii

Write-Host "SHA-256 hash written to '$hashFilePath':"
Write-Host $hash
