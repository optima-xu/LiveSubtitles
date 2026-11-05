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

1. Download and open `LiveSubtitles-Setup-v0.4.2-windows-x64.exe` from [Releases](https://github.com/optima-xu/LiveSubtitles/releases/latest).
2. Choose an installation folder, for example `D:\Apps\LiveSubtitles`. On a fresh installation, the models also go into this folder. The desktop shortcut is selected by default.
3. Click **Install**. The wizard shows each model’s download progress and prepares Ollama. You can cancel and resume later in the same folder. After setup, open **LiveSubtitles** from the Start menu or your desktop.
4. Select the output device and languages. **Click Start / 开始, wait until ready, then play the audio.**

The app interface is in Chinese. Uninstall from **Settings → Apps → Installed apps**. Choose whether to keep or delete this installation’s models, settings and logs. Exported subtitles and separately installed Ollama are kept.

## Contributing

Bug reports and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE). Models and dependencies retain their own licenses; see [third-party notices](THIRD_PARTY_NOTICES.md).
