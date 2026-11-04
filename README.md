<p align="center"><img src="docs/assets/livesubtitles.png" width="88" alt="LiveSubtitles icon"></p>

# LiveSubtitles

**English** · [简体中文](README.zh-CN.md)

Local live translated subtitles for audio playing on Windows. Listen to a video, lecture, or meeting through your usual speakers or headphones, and read the original text and translation in an always-on-top subtitle window.

LiveSubtitles uses a simple desktop interface and runs both speech recognition and translation through **native Windows Ollama**. The installer downloads the models and prepares Ollama automatically. No WSL, Python, Docker, or CUDA Toolkit is required. Once installed, the default local configuration works offline.

[Download for Windows](https://github.com/optima-xu/LiveSubtitles/releases/latest) · [Build status](https://github.com/optima-xu/LiveSubtitles/actions/workflows/ci.yml) · [Validation report](TESTING.md)

## Install and start

1. Open [Releases](https://github.com/optima-xu/LiveSubtitles/releases/latest), download `LiveSubtitles-v0.3.0-windows-x64.zip`, and extract the **entire** archive to a writable folder.
2. Double-click **`Install.cmd`** and wait for installation to finish. It checks the downloads, imports both models, and reuses Ollama 0.32.0 or newer when available. Otherwise, it downloads a pinned portable Windows version into the application's runtime folder.
3. Double-click **`Start.cmd`**. Choose the playback device, audio language, and translation language.
4. Click **Start / 开始**, wait until the models are ready, **then play the audio**.

The interface is currently in Chinese. The instructions here include the relevant button labels. The Windows release includes the .NET runtime; users do not need the .NET SDK. GitHub's source archives are for development and require building the application.

**First installation needs internet access.** Model downloads total about 2.93 GB; the portable Ollama archive adds about 1.50 GB if required. Allow at least 12 GB of free disk space for download caches, extracted runtime files, and imported models. The installer supports resuming downloads and reuses files that pass its SHA256 checks.

Automatic setup happens in `Install.cmd`; `Start.cmd` launches the installed application. Run the installer once before starting. Installation completes without opening the application automatically.

### Requirements

| Item | Current scope |
|---|---|
| System | Windows x64; tested on Windows 11 |
| Graphics | Tested with an NVIDIA RTX 5060 Laptop GPU, 8 GB VRAM, and an installed NVIDIA driver |
| Other GPUs / CPU | May work through Ollama, but real-time performance has not been validated |
| First-run downloads | Access to GitHub and Hugging Face |
| Daily use | Offline with the default local models and endpoint |

To keep the runtime and models on another drive:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -RuntimeDirectory 'D:\LiveSubtitlesRuntime'
```

Optional installer arguments: `-OllamaExecutable 'C:\path\to\ollama.exe'` and `-OllamaPort 11436`. By default, the application uses a dedicated local port and model store. A compatible existing service can be reused; the installer checks that the selected runtime contains the imported models.

## Features

- **System playback capture:** use the default output device or select specific speakers/headphones. Microphone capture is not included.
- **Floating subtitles:** always on top, draggable, with adjustable font size, background opacity, bilingual display, and mouse click-through.
- **Incremental updates:** source drafts update while speech continues; translations arrive progressively; pauses finalize the segment.
- **Multiple languages:** 23 explicit audio-language choices plus automatic detection, and 38 translation choices. Language selections persist across restarts. Not every language has been tested with real audio.
- **Terminology and context:** add glossary entries in Advanced settings / 高级设置, such as `gradient descent = 梯度下降`.
- **History and export:** copy subtitles or export TXT and SRT, with unfinished text and missing audio clearly marked.
- **Tray and keyboard controls:** minimize to the notification area and control the overlay with shortcuts.

| Action | Default shortcut |
|---|---|
| Start / pause | Ctrl+Alt+Space |
| Lock / unlock subtitle window | Ctrl+Alt+L |
| Show / hide subtitle window | Ctrl+Alt+H |

Shortcuts can be changed in Advanced settings; restart to apply them. Pause discards queued drafts and starts a new recognition session on resume. Stop and Exit close services started by the application. Services that were already running remain managed by their original owner.

## Models and streaming behavior

| Stage | Default implementation |
|---|---|
| Capture | NAudio / WASAPI loopback; mono PCM16 at 16 kHz |
| Speech detection | Silero VAD 6.2 through ONNX Runtime on the CPU |
| Recognition | Qwen3-ASR-0.6B Q8_0 with its Q8_0 audio encoder, through Ollama |
| Translation | HY-MT2-1.8B Q8_0, through the same Ollama service |
| Desktop UI | WPF / .NET 9 |

Audio is received in roughly 100 ms packets. The application submits a rolling recognition window about once per additional second of audio, finalizes it after roughly 0.6 seconds of silence, and splits continuous speech at about 12 seconds with overlap. Pending drafts are coalesced, queues are bounded, and dropped audio is reported.

This is **rolling-window near-real-time recognition with streaming translation**. Ollama reprocesses the current audio window; it is not a frame-by-frame ASR service with a persistent acoustic cache. Recognition replaces the current hypothesis, while translation streams incremental text. Session and revision checks prevent old results from replacing newer captions.

Model revisions, sizes, and SHA256 hashes are pinned in [resources/models.lock.json](resources/models.lock.json). Recognition weights come from [ggml-org's Qwen3-ASR conversion](https://huggingface.co/ggml-org/Qwen3-ASR-0.6B-GGUF); translation weights come from [Tencent's official HY-MT2 GGUF](https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF). The installer creates the local names `qwen3-asr:0.6b-q8_0` and `hy-mt2:1.8b-q8_0`; manual model pulls are unnecessary.

The ASR adapter relies on Ollama's Qwen3-ASR audio-projector support and transports WAV data through the multimodal `images` field. The pinned template supports explicit language selection and automatic detection. Arbitrary image or text chat models cannot replace this recognition model.

## Validation and limitations

The local validation used Windows 11, Ollama 0.32.0, and an RTX 5060 Laptop GPU. A 28.189-second English fixture was fed at playback speed twice after model warm-up:

| Result | Measurement |
|---|---|
| Recognition drafts / final segments | 42 / 10 |
| Final segments translated | 10 / 10 |
| First translation | 2.18 seconds after audio input started |
| Median final-translation delay | 1.56 seconds after the segment's last detected speech |
| Errors / reported audio gaps | 0 / 0 |

These measurements cover file-fed audio, segmentation, model inference, and translation events. They **exclude model cold start, the live speaker-capture path, and screen painting**. Capture and overlay layout were checked separately. The model-free suite contains 37 passing checks. Full details and reproduction commands are in [TESTING.md](TESTING.md); machine-readable results are in [docs/validation-v0.3.0.json](docs/validation-v0.3.0.json).

This is a small functional sample, not an accuracy benchmark or a guarantee of latency on other machines. The portable-Ollama download path has not been fully exercised on a clean computer. Other audio languages, hours-long sessions, multiple-monitor behavior, and performance on other hardware still need broader validation. Exclusive-fullscreen applications may cover the overlay; borderless window mode is preferable for those applications.

## Data and upgrades

The default configuration processes audio and text locally. Selecting a remote Ollama endpoint in Advanced settings sends recognition and translation requests to that endpoint. Settings are stored under `data`, and diagnostics are written to `data/diagnostics.log`. Subtitle history stays in memory until you export it to a location you choose. Review logs before sharing them.

Repository contents and release archives exclude model weights, runtime caches, personal settings, and transcripts. Download caches remain in the selected runtime directory so reinstalling can reuse verified files.

To upgrade, extract the new release into a new folder, copy your old `data/settings.json` into its `data` folder if you want to retain preferences, and run `Install.cmd`. It preserves language, appearance, position, and shortcut settings while applying the native model configuration. Existing HY-MT2 downloads can be reused. The old Linux backend is no longer part of the project; the installer does not uninstall system components used by other software.

## Troubleshooting

| Symptom | What to check |
|---|---|
| Missing models or first launch fails | Run `Install.cmd` from the fully extracted release directory |
| Ollama version is too old | Update the service using that port, or reinstall with another `-OllamaPort` |
| No subtitles | Start first, wait for readiness, then play audio; check the output device and level meter |
| Subtitles fall behind | Check other GPU workloads and use the default 0.6B ASR model |
| Download or integrity error | Rerun `Install.cmd`; incomplete or invalid files are not imported |
| Need diagnostics | Run `scripts/Diagnose.ps1` and inspect the local diagnostic log |

Protected content may not be available through system loopback capture.

## Build from source

Development requires Windows and the **.NET 9 SDK**.

```powershell
git clone https://github.com/optima-xu/LiveSubtitles.git
cd LiveSubtitles
dotnet run --project tests/CaptionChecks -c Release
.\scripts\Build.ps1 -SelfContained
```

The application is built into `dist/LiveSubtitles`. Run its `Install.cmd` and then `Start.cmd`. Both language versions of this README are included in the Windows build.

See [CONTRIBUTING.md](CONTRIBUTING.md) for checks, [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the pipeline, and [docs/PUBLISHING.md](docs/PUBLISHING.md) for release steps. GitHub Actions builds and tests the Windows package without downloading models or requiring a GPU.

## License

Application code is licensed under [MIT](LICENSE). Models and dependencies retain their upstream licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and the license texts in `resources/`.
