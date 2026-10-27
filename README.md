# OptiForge (optimizeTools)

Portable Windows toolkit: **system monitor / clean / RAM trim / local AI** — designed to live on an external drive (≤10 GB with models).

**Repo:** https://github.com/LLLVPN4423/optimizeTools  
**Local root:** `D:\repos\optimizeTools` (or any drive letter — paths are relative)

## Quick start (dev)

```bat
dotnet build src\OptiForge.slnx -c Release
Launch.bat
```

Publish into `App\`:

```bat
scripts\publish-portable.bat
Launch.bat
```

## Layout

```
OptiForge/
  App/                 published exe + data/
  Runtime/llama/       llama-server.exe
  Models/fast/         default GGUF (~2GB)
  Models/quality/      optional larger GGUF
  Knowledge/           offline playbooks
  Launch.bat
  src/                 .NET solution
```

## Status

| Phase | Feature | Status |
|-------|---------|--------|
| 0 | Portable layout + Launch.bat | done |
| 1 | WPF skeleton + settings | done |
| 2 | Dashboard CPU/RAM/Disk | done (basic) |
| 3 | Disk clean + dry-run | done |
| 4 | RAM trim | done |
| 5 | llama.cpp chat + cache | planned |
| 6 | Knowledge playbooks UI | planned |
| 7 | GitHub self-update | stub |
| 8 | Bundle polish ≤10GB | planned |

## Requirements

- Windows 10/11 x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download) (or publish self-contained later)
- Optional: llama-server + GGUF for local AI

## Note on git safe.directory

If this folder is on a drive that does not record ownership, git may warn about dubious ownership. Use:

```bat
git -c safe.directory=D:/repos/optimizeTools status
```

(or add a *local* safe.directory for this repo only if you prefer).
