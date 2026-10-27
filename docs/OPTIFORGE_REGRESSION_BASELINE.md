# Regression Baseline — pre 3.0

**Commit:** `ba6628f` (v0.4.0) · **Tag:** `checkpoint-v0.4.0`

## Smoke checklist (manual)
| Check | Pre-3.0 |
|-------|---------|
| Launch.bat opens UI | Pass (prior) |
| Dashboard metrics update | Pass |
| Clean dry-run | Pass |
| AI Start builtin / chat | Pass |
| Agent Plan dry-run | Pass |
| LM Studio Probe when server up | Pass when 1234 up |
| Safe eject | Pass |
| Automated unit tests | **None present** |

## Performance snapshot (host)
- Physical RAM ~7.3 GB
- OptiForge model file ~1.96 GB GGUF on portable Models/fast
- No tok/s benchmark recorded yet

## Known limitations retained
- 3B ≠ Claude/GPT quality
- Agent is scaffold-only
- No conversation DB yet (added in Phase 2)
