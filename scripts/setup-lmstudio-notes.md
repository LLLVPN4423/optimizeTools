# LM Studio on this PC (Aspire A715-42G)

**Install:** `D:\AppData\LM Studio`  
**Home pointer:** `%USERPROFILE%\.lmstudio-home-pointer` → `D:\AppData\LM Studio`  
**Model ready:** `D:\AppData\LM Studio\models\lmstudio-community\Qwen2.5-3B-Instruct-GGUF\Qwen2.5-3B-Instruct-Q4_K_M.gguf` (~1.84 GB)

## Hardware targets
- Ryzen 5 5500U / 8 GB RAM / GTX 1650 4 GB
- Prefer **CUDA** GPU offload, context **2048**, one model at a time
- Optional later: Qwen2.5-7B Q4 (heavier; plug in AC)

## Wire to OptiForge
1. Run `D:\AppData\LM Studio\START-FOR-OPTIFORGE.bat`
2. Load model → Developer → Start Server (`1234`)
3. OptiForge → Agent → Backend **LM Studio** → Probe
