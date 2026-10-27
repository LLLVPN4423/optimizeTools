# OptiForge (`optimizeTools`)

Portable Windows toolkit for an **external drive**: monitor · clean · RAM · local AI · network QoS · startup · tips · auto-update.

**GitHub:** https://github.com/LLLVPN4423/optimizeTools  
**Version:** 0.2.2

## Requirements

- Windows 10/11 x64  
- **Self-contained build:** no .NET install needed (`scripts\publish-selfcontained.bat`)  
- **Framework build:** needs [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download)  
- AI download needs internet once (~2GB model + llama-server)

## Quick start

```bat
scripts\publish-selfcontained.bat
Launch.bat
```

Then in app → **AI** → **Download AI runtime+model** → **Start server** → chat.

## Features

| Tab | Capability |
|-----|------------|
| Dashboard | CPU / RAM / Disk live |
| Clean | Safe temp clean + dry-run |
| RAM | Working-set trim + standby (admin) |
| AI | Local Qwen2.5-3B: chế độ dịch/tóm tắt/tối ưu PC…, chat, cache |
| Network | Per-process KB/s limit via Windows QoS (admin) |
| Startup | HKCU Run disable/enable with backup (debloat-lite) |
| Tips | Offline playbooks |
| Settings | Host impact scan / **Safe eject**, prefs, GitHub update |

## Portable layout (≤10 GB with model)

```
optimizeTools/
  App/                 exe (+ runtime if self-contained)
  Runtime/llama/       llama-server (downloaded)
  Models/fast/         GGUF ~2GB (downloaded)
  Knowledge/playbooks/
  Launch.bat
  src/
```

## Auto-update

1. Publish a GitHub Release with asset named like `OptiForge-win-x64.zip` (contents = `App\` files).  
2. In Settings → **Download + apply update**.

## Notes

- Debloat is **startup-only** (no aggressive registry cleaner).  
- Network throttle uses **QoS**, not WinDivert.  
- Before unplugging: **Safe eject** (or close app with auto-cleanup) — stops llama from this drive, removes `OptiForge_*` QoS, clears portable update temp.  
- Models/binaries are gitignored — stay on your drive.

## License

Personal / educational use. llama.cpp and model licenses apply separately.
