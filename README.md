1) Download a release for your architecture
2) Run setup

eowkit install

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

What happens:
	•	kiwix-serve exposes the offline Wikipedia.
	•	Ollama is started (honors paths.models_dir via OLLAMA_MODELS).
	•	The runner retrieves top articles and answers with citations (titles).