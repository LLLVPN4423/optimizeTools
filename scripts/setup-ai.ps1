# Downloads llama-server + default GGUF into portable folders (same as in-app button).
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Write-Host "Root: $Root"

$exe = Join-Path $Root "App\OptiForge.exe"
if (-not (Test-Path $exe)) { $exe = Join-Path $Root "App\OptiForge.App.exe" }
if (-not (Test-Path $exe)) {
  Write-Host "Publish first: scripts\publish-selfcontained.bat"
  Write-Host "Then use in-app button: Download AI runtime+model"
  exit 1
}

Write-Host "Open OptiForge and click 'Download AI runtime+model' on the AI tab."
Write-Host "Or wait — launching app..."
Start-Process $exe
