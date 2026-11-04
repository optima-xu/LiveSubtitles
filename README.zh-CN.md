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

1. 前往 [Releases](https://github.com/optima-xu/LiveSubtitles/releases/latest)，下载 `LiveSubtitles-v0.3.0-windows-x64.zip` 并完整解压。
2. 双击 **`Install.cmd`**，等待自动下载模型并准备 Ollama；已有兼容版本时会直接复用。
3. 双击 **`Start.cmd`**，选择电脑输出设备、音频语言和翻译语言。
4. **先点击“开始”，等待模型就绪，再播放电脑音频。**

Windows 包已包含 .NET 运行环境，无需另行安装 Ollama、Python 或 WSL。

## Contributing

欢迎提交 Issue 和 Pull Request，详见 [贡献指南](CONTRIBUTING.md)。

## License

本项目采用 [MIT 许可证](LICENSE)。模型与第三方依赖遵循各自许可证，详见 [第三方声明](THIRD_PARTY_NOTICES.md)。
