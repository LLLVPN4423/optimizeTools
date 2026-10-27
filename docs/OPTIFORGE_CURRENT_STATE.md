# OptiForge Current State (v0.4.0)

**Inspected:** 2026-10-28 · Root `D:\repos\optimizeTools`

## Stack
- **.NET 10 / WPF** (`OptiForge.App` + Core / AI / SysMon / Update)
- Portable layout: `App/`, `Runtime/llama/`, `Models/`, `Knowledge/`, `Projects/`, `Launch.bat`
- UI: single `MainWindow` TabControl (Dashboard, Clean, RAM, AI, Agent, Quota, Network, Startup, Tips, Settings)

## Working features (verified in code / prior use)
| Area | Status |
|------|--------|
| Dashboard CPU/RAM/Disk | Working (`SystemMonitor` P/Invoke) |
| Clean dry-run + delete | Working |
| RAM trim / standby | Working (admin for standby) |
| AI builtin llama-server | Working (port 8081) |
| AI modes + streaming | Working |
| Prompt cache | Working |
| Agent project scaffold | Working (`Projects/`) |
| LM Studio backend probe | Working (OpenAI compat `1234/v1`) |
| Quota / cloud brief | Working |
| QoS network throttle | Working (admin) |
| Startup HKCU backup | Working |
| Safe eject host cleanup | Working |
| Playbooks / updates | Working |

## Gaps vs OptiForge 3.0 vision
- No persistent multi-conversation workspace (chat history lost on close except prompt-cache files)
- Tab-centric UI, not ChatGPT/Claude-style sidebar workspace
- No hardware resource governor / profiles
- No model library UI beyond bootstrap download
- Agent is template-first, not full coding agent
- No automated test project
- Docs sparse

## Backends
- Builtin: `http://127.0.0.1:{AiPort}/v1` via `LlamaServerHost`
- LM Studio: `http://127.0.0.1:1234/v1` via `OpenAiCompatClient` / Agent
- Model: Qwen2.5-3B Q4_K_M under `Models/fast` and optionally LM Studio home on `D:\AppData\LM Studio`

## Host sample (this machine)
- Acer Aspire A715-42G · Ryzen 5 5500U · ~7.3 GB RAM usable · GTX 1650 4GB
