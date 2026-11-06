<p align="center"><img src="docs/assets/livesubtitles.png" width="88" alt="LiveSubtitles 图标"></p>

# LiveSubtitles

[English](README.md) · **简体中文**

为 Windows 电脑正在播放的声音生成实时字幕和翻译，通过简洁的悬浮窗口显示。

## 功能

- 电脑声音实时识别与流式翻译，支持多种语言。
- 置顶双语悬浮字幕，可调整位置、字号、透明度和鼠标穿透。
- 支持术语表、字幕历史、TXT / SRT 导出及快捷键。
- 基于 Windows 原生 Ollama 本地运行，安装后可离线使用。

## 安装方法

需要 **64 位 Windows**，首次安装需联网，并预留至少 **12 GB 磁盘空间**。

1. 从 [Releases](https://github.com/optima-xu/LiveSubtitles/releases/latest) 下载并打开 `LiveSubtitles-Setup-v0.4.3-windows-x64.exe`。
2. 选择安装目录，例如 `D:\Apps\LiveSubtitles`。全新安装时模型也会下载到该目录。桌面快捷方式默认勾选。
3. 点击“安装”，向导会显示每个模型的下载进度并准备 Ollama。中途可以取消，下次选择同一目录即可续传。完成后，从开始菜单或桌面打开 **LiveSubtitles**。
4. 选择输出设备和语言。**先点击“开始”，等待模型就绪，再播放电脑音频。**

在 Windows“设置 → 应用 → 已安装的应用”中卸载时，可以选择保留或删除本次安装的模型、设置和日志。已导出的字幕及单独安装的 Ollama 都会保留。

## Contributing

欢迎提交 Issue 和 Pull Request，详见 [贡献指南](CONTRIBUTING.md)。

## License

本项目采用 [MIT 许可证](LICENSE)。模型与第三方依赖遵循各自许可证，详见 [第三方声明](THIRD_PARTY_NOTICES.md)。
