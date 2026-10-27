# Changelog

## 0.3.0

- Honest hybrid AI: local 3B for routine work; cloud for polish (not claiming Claude-level quality)
- **Quota Saver** tab: stats, cloud brief, batch template, open ChatGPT/Gemini/Claude/Perplexity
- AI streaming replies + smarter assistant system prompt + Cloud brief from AI tab
- Playbook: keo dai free tier

## 0.2.2

- AI UX: chế độ sẵn (hỏi đáp, giải thích, dịch, tóm tắt, viết lại, tối ưu PC, code nhỏ, checklist)
- AI chat: transcript, Ctrl+Enter, hội thoại ngắn, badge sẵn sàng, quick chips, copy/clear
- API: system prompt + temperature/max_tokens theo mode; playbook khả năng local AI

## 0.2.1

- Host impact: analyze + **Safe eject** cleanup (stop llama from this drive, remove all `OptiForge_*` QoS, clear portable update temp)
- Auto cleanup on exit (default on); optional restore startups on exit
- Network: Remove ALL OptiForge QoS; Startup: Restore all from backup + drop empty OptiForge registry key
- Playbook: Safe eject — dọn dấu vết host

## 0.2.0

- AI: in-app **Download AI runtime+model** (llama-server + Qwen2.5 3B GGUF ~2GB)
- Publish: `scripts/publish-selfcontained.bat` (no .NET install on target PC)
- Update: **Download + apply update** from GitHub Releases zip + restart
- Network: per-process QoS throttle (admin)
- Startup: safe HKCU Run enable/disable with backup + dry-run

## 0.1.0

- Portable layout, dashboard, disk clean, RAM trim
- Local AI host/chat/cache (manual runtime/model)
- Tips playbooks, release check
