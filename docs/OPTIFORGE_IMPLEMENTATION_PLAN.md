# OptiForge 3.0 Implementation Plan

## Strategy
Incremental migration on **WPF/.NET 10** — no framework rewrite. Chat-first shell wraps existing services.

## Checkpoint
- Git tag: `checkpoint-v0.4.0`
- Stash: `checkpoint-before-optiforge-3.0` (LM Studio notes)

## Phase order (this pass)
| Phase | Scope | Status |
|-------|--------|--------|
| 0 | Inspect + docs + baseline | Done |
| 1 | Design tokens + sidebar shell + nav | In progress |
| 2 | Conversation persistence + chat workspace | In progress |
| 3 | Hardware governor profiles (Balanced/LowMem/…) | Stub + wire |
| 4–8 | Model library polish, agent harden, diagnostics, tests | Follow-up |

## Preserve
All existing tabs/services: Clean, RAM, Network, Startup, Quota, Agent, Tips, Safe eject, LM Studio bridge.

## Rollback
```bat
git checkout checkpoint-v0.4.0
scripts\publish-portable.bat
```
