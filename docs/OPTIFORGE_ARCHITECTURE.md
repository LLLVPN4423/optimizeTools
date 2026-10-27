# OptiForge Architecture (0.5.0)

```
OptiForge.App (WPF shell)
  ├── ConversationStore / ResourceGovernor (Core)
  ├── LocalAiService / LlamaServerHost / ProjectAgent / QuotaSaver (AI)
  ├── SystemMonitor / Clean / RAM / QoS / Startup / HostCleanup (SysMon)
  └── AutoUpdater (Update)
```

**UI:** Sidebar navigates hidden `TabControl` pages. Chat is default workspace.  
**Persistence:** JSON conversations under `App/data/conversations/` (schema v1).  
**Backends:** Builtin llama-server or LM Studio OpenAI-compatible API.
