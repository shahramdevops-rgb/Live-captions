# Builds the double-clickable app into .\publish and puts a shortcut on the desktop.
# Run again after changing the code. Usage (from anywhere):
#   powershell -ExecutionPolicy Bypass -File D:\path\to\LiveCaptionsGemini\publish.ps1

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'publish'
$exe = Join-Path $out 'LiveCaptionsGemini.exe'

# The exe can't be replaced while it runs.
$running = Get-Process LiveCaptionsGemini -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($running) { throw 'LiveCaptions -> Gemini is running from the publish folder. Close it (the X button) and run this again.' }

# Framework-dependent single file: one small exe, uses the installed .NET 8 Desktop Runtime.
# ReadyToRun = precompiled, so it starts faster.
dotnet publish (Join-Path $root 'src\LiveCaptionsGemini\LiveCaptionsGemini.csproj') `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:DebugType=none `
    -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# config.json (with your API key) lives next to the exe. Copied once from the repo root;
# an existing one is never overwritten, so your saved settings survive a rebuild.
$config = Join-Path $out 'config.json'
if (-not (Test-Path $config) -and (Test-Path (Join-Path $root 'config.json'))) {
    Copy-Item (Join-Path $root 'config.json') $config
    Write-Host 'Copied config.json next to the exe.'
}

$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'LiveCaptions Gemini.lnk'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $out
$link.Description = 'LiveCaptions -> Gemini overlay'
$link.Save()

Write-Host ''
Write-Host "App:      $exe"
Write-Host "Shortcut: $shortcut"
