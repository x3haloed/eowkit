# End-of-the-World Kit
When your internet connection is censored or disabled, you will want to learn things like how to effectively grow your own food.

<img width="1170" height="740" alt="image" src="https://github.com/user-attachments/assets/fcc20dd4-dea8-4090-b586-b4d5b107c559" />

EoWKit downloads a copy of Wikipedia and an offline LLM to your computer so you can still get answers to hard problems, even when you've been cut off from the internet.

## Project Status

Tested  Feature

✅  macOS, wikipedia_en_all_mini_2025-06.zim, phi3:mini, reranker=NO

❌  Windows

❌  Linux

## Getting Started

### 1) Download a release for your architecture

Download [the latest release](https://github.com/x3haloed/eowkit/releases) for your OS under Assets.

### 2) Run setup
```
eowkit install
```

Unsigned builds (macOS and Windows):

- macOS (Gatekeeper)
  - On first run you may see: "'eowkit' cannot be opened because it is from an unidentified developer."
  - Do one of the following once:
    - Finder: right‑click `eowkit` → Open → Open
    - System Settings → Privacy & Security → Open Anyway for the blocked app
    - Terminal:
      ```
      xattr -dr com.apple.quarantine /path/to/eowkit
      ```
  - After the first successful open, it should run normally.

- Windows (SmartScreen/Defender)
  - You may see: "Windows protected your PC" (SmartScreen).
  - Click More info → Run anyway. If the file shows as blocked:
    - Right‑click `eowkit.exe` → Properties → check "Unblock" → Apply → OK
  - After that, it should launch normally.

- Prompts for directories: downloads, ZIM storage, models (OLLAMA_MODELS)
- Lets you choose the Wikipedia snapshot and LLM model
- Warns if RAM is too low or target disk is nearly full (90% rule)
- Offers to enable the reranker and auto-download ONNX + vocab

### 3) (Optional) Put the big files on another drive

Edit configs/eowkit.toml
```toml
[paths]
downloads_dir = "/mnt/downloads"          # where large downloads land
zim_dir       = "/mnt/wiki"               # where .zim files live
models_dir    = "/mnt/ollama/models"      # where Ollama stores models
library_xml   = "/mnt/wiki/library.xml"   # optional: serve many ZIMs

[wiki]
zim = "wikipedia_en_all_nopic_2025-08.zim"  # resolved under paths.zim_dir
kiwix_port = 8080
bind = "127.0.0.1"
```
To serve many ZIMs:
```
kiwix-manage /mnt/wiki/library.xml add /mnt/wiki/*.zim
```

Set paths.library_xml and leave wiki.zim as-is (it’ll be ignored when a library is present).

### 4) First run
```
eowkit run
```
- starts kiwix-serve on your ZIM (or library), ensures Ollama + model, drops you into a prompt
- type questions; Ctrl-C to exit

### 5) (Optional) Enable the reranker anytime

eowkit install-reranker

This downloads a cross-encoder ONNX model + vocab into models_dir and flips TOML:

```toml
[reranker]
enabled = true
onnx_model = "models/ce-minilm-l6.onnx"
tokenizer_vocab = "models/vocab.txt"
max_seq_len = 256
```

### Hardware detection
- Probes RAM, CPU AVX2 support, logical cores, and GPU backends (CUDA/OpenCL/Metal)
- Installer prints a hint for model sizing and sets a sane default thread count
- Override threads as needed:

```toml
[llm_runtime]
num_threads = 8
```

### Disk checks and paths
- Free space checks are done on the target drive for ZIM and models. If an item would exceed ~90% of free space, a red warning is shown.
- Downloads stage into `downloads_dir` and then are copied to `zim_dir`.
- Ollama models honor `paths.models_dir` via OLLAMA_MODELS when the daemon is spawned.

### Commands
- install: guided setup (paths, wiki, model, optional reranker)
- install-reranker: download ONNX + vocab and enable reranking
- run: start kiwix-serve, ensure Ollama + model, interactive prompt
- probe: show RAM, disk, CPU/GPU capabilities
- sum <path>: SHA-256 of a file
- fetch-sum <url>: try to fetch a .sha256/.sha256sum for a URL

What happens:
	•	kiwix-serve exposes the offline Wikipedia.
	•	Ollama is started (honors paths.models_dir via OLLAMA_MODELS).
	•	The runner retrieves top articles and answers with citations (titles). If the reranker is enabled and its files exist, results are re-ranked with the cross-encoder.
