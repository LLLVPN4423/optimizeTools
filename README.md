# OptiForge (`optimizeTools`)

Portable Windows toolkit for an **external drive**: monitor · safe disk clean · RAM trim · local AI (llama.cpp) · offline tips.

**GitHub:** https://github.com/LLLVPN4423/optimizeTools  
**Version:** 0.1.0

## Requirements

- Windows 10/11 x64  
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download) (framework-dependent publish)  
- Optional AI: [llama.cpp `llama-server` release](https://github.com/ggml-org/llama.cpp/releases) + one GGUF (~2 GB Q4_K_M)

## Quick start

```bat
scripts\publish-portable.bat
Launch.bat
```

Dev build:

```bat
dotnet build src\OptiForge.slnx -c Release
Launch.bat
```

## Portable layout (≤10 GB with models)

```
optimizeTools/
  App/                 published exe + data/
  Runtime/llama/       llama-server.exe
  Models/fast/         default GGUF (~2GB)
  Models/quality/      optional 7–8B GGUF
  Knowledge/playbooks/ offline tips JSON
  Launch.bat
  src/                 source
```

Drive letter can change — all paths are relative / `OPTIFORGE_ROOT`.

## Features

| Tab | What it does |
|-----|----------------|
| Dashboard | CPU / RAM / Disk live |
| Clean | Safe temp scan + dry-run / delete |
| RAM | Empty working sets; standby purge if admin |
| AI | Start llama-server, chat, prompt cache |
| Tips | Offline playbooks |
| Settings | Save prefs + GitHub release check |

## AI setup

1. Download Windows `llama-server` → `Runtime/llama/`  
2. Download e.g. **Qwen2.5 3B Instruct Q4_K_M** `.gguf` → `Models/fast/`  
3. App → **AI** → **Start server** → chat  

Prefer USB 3 / portable SSD for model load speed.

## Status

| Phase | Feature | Status |
|-------|---------|--------|
| 0 | Portable layout + Launch.bat | done |
| 1 | WPF skeleton + settings | done |
| 2 | Dashboard CPU/RAM/Disk | done |
| 3 | Disk clean + dry-run | done |
| 4 | RAM trim | done |
| 5 | llama.cpp chat + cache | done |
| 6 | Knowledge playbooks UI | done |
| 7 | GitHub Releases update check | done |
| 8 | Bundle polish | done |

## Git note (external / no-ownership drives)

```bat
git -c safe.directory=D:/repos/optimizeTools status
```

## License

Use and modify for personal / educational purposes. Third-party runtimes (llama.cpp) keep their own licenses.
