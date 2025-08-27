1) Download a release for your architecture
2) Run setup

eowkit install

- Prompts for directories: downloads, ZIM storage, models (OLLAMA_MODELS)
- Lets you choose the Wikipedia snapshot and LLM model
- Warns if RAM is too low or target disk is nearly full (90% rule)
- Offers to enable the reranker and auto-download ONNX + vocab

3) (Optional) Put the big files on another drive

Edit configs/eowkit.toml — TOML, not YAML.

[paths]
downloads_dir = "/mnt/downloads"          # where large downloads land
zim_dir       = "/mnt/wiki"               # where .zim files live
models_dir    = "/mnt/ollama/models"      # where Ollama stores models
library_xml   = "/mnt/wiki/library.xml"   # optional: serve many ZIMs

[wiki]
zim = "wikipedia_en_all_nopic_2025-08.zim"  # resolved under paths.zim_dir
kiwix_port = 8080
bind = "127.0.0.1"

To serve many ZIMs:

kiwix-manage /mnt/wiki/library.xml add /mnt/wiki/*.zim

Set paths.library_xml and leave wiki.zim as-is (it’ll be ignored when a library is present).

5) First run

eowkit run
# starts kiwix-serve on your ZIM (or library), ensures Ollama + model, drops you into a prompt
# type questions; Ctrl-C to exit

4) (Optional) Enable the reranker anytime

eowkit install-reranker

This downloads a cross-encoder ONNX model + vocab into models_dir and flips TOML:

```
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

```
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