<p align="center"><img src="docs/assets/livesubtitles.png" width="88" alt="LiveSubtitles icon"></p>

# LiveSubtitles

**English** · [简体中文](README.zh-CN.md)

Live subtitles and translation for audio playing on your Windows PC, displayed in a simple floating window.

## Features

- Live speech recognition and streaming translation of system audio, with multilingual support.
- Always-on-top bilingual subtitles with adjustable position, font size, opacity, and mouse click-through.
- Custom glossary, subtitle history, TXT / SRT export, and keyboard shortcuts.
- Local processing through native Windows Ollama; works offline after setup.

## Installation

Requires **64-bit Windows**, internet access for initial setup, and at least **12 GB of free disk space**.

1. Download `LiveSubtitles-v0.3.0-windows-x64.zip` from [Releases](https://github.com/optima-xu/LiveSubtitles/releases/latest) and extract the entire archive.
2. Double-click **`Install.cmd`**. It automatically downloads the models and prepares Ollama, reusing a compatible installation when available.
3. Double-click **`Start.cmd`** and select your audio output device, source language, and translation language.
4. **Click Start / 开始, wait until the models are ready, then play the audio.**

The interface is currently in Chinese. The Windows package includes the .NET runtime; no separate Ollama, Python, or WSL installation is needed.

## Contributing

Bug reports and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE). Models and dependencies retain their own licenses; see [third-party notices](THIRD_PARTY_NOTICES.md).
