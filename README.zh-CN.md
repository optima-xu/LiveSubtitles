<p align="center"><img src="docs/assets/livesubtitles.png" width="88" alt="LiveSubtitles 图标"></p>

# LiveSubtitles · 本地实时翻译字幕

[English](README.md) · **简体中文**

接收 Windows 正在播放的电脑音频，在置顶悬浮窗中显示原文和翻译。适合看视频、听课程，以及阅读会议播放声音的字幕。

界面采用简洁的传统桌面控件，语音识别和翻译统一使用 **Windows 原生 Ollama**。安装器自动下载模型并准备 Ollama，无需 WSL、Python、Docker 或 CUDA Toolkit。安装完成后，默认本地配置可以离线使用。

[下载 Windows 版](https://github.com/optima-xu/LiveSubtitles/releases/latest) · [构建状态](https://github.com/optima-xu/LiveSubtitles/actions/workflows/ci.yml) · [验证记录](TESTING.md)

## 安装与开始使用

1. 打开 [Releases](https://github.com/optima-xu/LiveSubtitles/releases/latest)，下载 `LiveSubtitles-v0.3.0-windows-x64.zip`，将**整个压缩包**解压到有写入权限的文件夹。
2. 双击 **`Install.cmd`**，等待安装结束。它会校验下载文件并导入模型；已有 Ollama 0.32.0 或更新版时直接复用，否则自动下载固定版本的 Windows 便携运行环境。
3. 双击 **`Start.cmd`**，选择电脑输出设备、音频语言和字幕语言。
4. **先点击“开始”，等待模型就绪，再播放电脑音频。** 初次预热需要一些时间。

应用界面目前为中文。Windows 发布包已经包含 .NET 运行环境，普通用户不需要安装 .NET SDK。GitHub 的 Source code 压缩包用于开发，需要自行构建应用。

**首次安装需要联网。** 模型下载量约 2.93 GB；未安装 Ollama 时，另下载约 1.50 GB 便携运行环境压缩包。建议预留至少 12 GB 磁盘空间，用于下载缓存、解压和模型导入。安装支持断点续传，通过 SHA256 校验的已有文件会复用。

自动准备发生在 `Install.cmd` 中，`Start.cmd` 负责启动已安装的应用。首次需要先运行安装器；安装完成后，还需自行双击 `Start.cmd` 打开程序。

### 运行要求

| 项目 | 当前范围 |
|---|---|
| 系统 | Windows x64；已在 Windows 11 验证 |
| 显卡 | 实测为 NVIDIA RTX 5060 Laptop GPU、8 GB 显存，已安装 NVIDIA 驱动 |
| 其他显卡 / 纯 CPU | 可以尝试通过 Ollama 运行，尚未验证实时性能 |
| 首次下载 | 需要访问 GitHub 和 Hugging Face |
| 日常使用 | 默认本地模型和地址下可离线使用 |

把运行环境与模型放到其他磁盘：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -RuntimeDirectory 'D:\LiveSubtitlesRuntime'
```

可选参数：`-OllamaExecutable 'C:\path\to\ollama.exe'` 指定已有程序，`-OllamaPort 11436` 设置本地端口。默认使用独立端口和项目模型目录。兼容的已有服务可以复用；安装器会检查选定的运行目录确实包含导入的模型。

## 功能

- **电脑声音采集**：跟随系统默认输出，也可选择具体耳机或扬声器；目前不提供麦克风采集。
- **悬浮字幕**：置顶、拖动、字号、背景透明度、单双语显示，以及鼠标穿透。
- **持续更新**：说话时更新识别草稿，翻译逐步显示，停顿后定稿。
- **多语言**：23 种明确音频语言及自动识别，38 个译文选项；选择会保存。尚未逐语种验证真实音频效果。
- **术语与上下文**：高级设置可填写术语表，例如 `gradient descent = 梯度下降`。
- **历史与导出**：复制字幕，导出 TXT / SRT；未定稿和音频缺失会明确标注。
- **托盘与快捷键**：可最小化到系统托盘，使用键盘控制字幕窗口。

| 操作 | 默认快捷键 |
|---|---|
| 开始 / 暂停 | Ctrl+Alt+Space |
| 锁定 / 解锁字幕 | Ctrl+Alt+L |
| 显示 / 隐藏字幕 | Ctrl+Alt+H |

快捷键可在高级设置中修改，重启后生效。暂停会丢弃待处理草稿，恢复时创建新识别会话。“停止”和“退出”关闭由本应用启动的服务；原本已运行的外部服务仍由原来的程序管理。

## 模型与流式方式

| 阶段 | 默认实现 |
|---|---|
| 音频采集 | NAudio / WASAPI loopback；16 kHz 单声道 PCM16 |
| 语音分段 | Silero VAD 6.2，通过 ONNX Runtime 在 CPU 上运行 |
| 语音识别 | Qwen3-ASR-0.6B Q8_0 及 Q8_0 音频编码器，通过 Ollama 运行 |
| 翻译 | HY-MT2-1.8B Q8_0，通过同一个 Ollama 服务运行 |
| 桌面界面 | WPF / .NET 9 |

音频按约 100 ms 持续进入应用。每增加约 1 秒音频，提交当前滚动窗口进行识别；停顿约 0.6 秒后定稿。连续长句在约 12 秒处切窗，并保留边界重叠。待处理草稿会合并，队列有上限；处理速度落后导致丢弃时会显示缺失提示。

这是**滚动窗口的准实时识别 + 流式翻译**。Ollama 每次重新识别当前窗口，不是带持久声学缓存的逐帧识别服务。识别以完整假设替换草稿，翻译则逐步返回文字；会话和修订检查防止旧结果覆盖新字幕。

模型版本、大小及 SHA256 固定在 [resources/models.lock.json](resources/models.lock.json)。识别权重来自 [ggml-org 的 Qwen3-ASR 转换](https://huggingface.co/ggml-org/Qwen3-ASR-0.6B-GGUF)，翻译权重来自 [腾讯官方 HY-MT2 GGUF](https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF)。安装器创建 `qwen3-asr:0.6b-q8_0` 和 `hy-mt2:1.8b-q8_0` 两个本地模型名，不需要手动拉取模型。

识别适配器依赖 Ollama 的 Qwen3-ASR 音频编码器支持，使用多模态 `images` 字段传输 WAV。固定模板支持指定语言和自动识别；普通图片或文本对话模型不能直接替代此识别模型。

## 验证与限制

本机验证环境为 Windows 11、Ollama 0.32.0 和 RTX 5060 Laptop GPU。预热后，按真实播放速度输入一段 28.189 秒英语测试音频，重复两次：

| 项目 | 实测结果 |
|---|---|
| 识别草稿 / 定稿 | 42 次 / 10 段 |
| 完成定稿翻译 | 10 / 10 |
| 首次译文 | 开始输入音频后 2.18 秒 |
| 最终译文延迟中位数 | 相对该段最后检测到的语音末尾 1.56 秒 |
| 错误 / 缺失提示 | 0 / 0 |

这些时间包含文件音频输入、分段、模型推理和翻译事件，**不包含模型冷启动、实际扬声器采集链路或屏幕绘制**。采集和悬浮窗布局另行验证。无需模型的回归检查共 37 项通过。完整范围及复现命令见 [TESTING.md](TESTING.md)，结构化结果见 [docs/validation-v0.3.0.json](docs/validation-v0.3.0.json)。

上述结果是小样例的功能验证，不是识别准确率基准，也不保证其他电脑具有相同速度。便携 Ollama 下载分支尚未在全新电脑完整实测；其他语种、数小时运行、多显示器及不同硬件仍需进一步验证。独占全屏应用可能覆盖悬浮字幕，遇到这种情况可使用无边框窗口模式。

## 数据与升级

默认配置在本机处理音频和文字。如果在高级设置中指定远程 Ollama 地址，识别和翻译请求会发送到该地址。用户设置位于 `data`，诊断日志位于 `data/diagnostics.log`。字幕历史保存在内存中，可手动导出到自己选择的位置；共享日志前请先检查内容。

仓库和发布包不包含模型权重、运行缓存、个人设置或字幕记录。下载缓存保留在选择的运行目录内，重装时可以复用已校验的文件。

升级时，将新版本解压到新目录；如需保留偏好，可将旧版 `data/settings.json` 复制到新目录的 `data` 下，再运行 `Install.cmd`。安装器保留语言、样式、字幕位置和快捷键，并应用原生模型配置。已有 HY-MT2 下载可以复用。项目已移除旧版 Linux 后端；安装器不会卸载其他软件可能使用的系统组件。

## 排错

| 问题 | 检查方法 |
|---|---|
| 提示缺模型或首次启动失败 | 在完整解压后的目录先运行 `Install.cmd` |
| Ollama 版本过低 | 更新该端口上的服务，或换一个 `-OllamaPort` 重新安装 |
| 没有字幕 | 先启动、等就绪、再播放；确认输出设备并观察音量条 |
| 字幕跟不上 | 检查其他 GPU 程序负载，使用默认的 0.6B 识别模型 |
| 下载或文件校验失败 | 重试 `Install.cmd`；不完整或校验失败的文件不会导入 |
| 需要诊断 | 运行 `scripts/Diagnose.ps1` 并查看本地诊断日志 |

受保护内容可能无法通过系统回环采集。

## 从源码构建

开发需要 Windows 和 **.NET 9 SDK**。

```powershell
git clone https://github.com/optima-xu/LiveSubtitles.git
cd LiveSubtitles
dotnet run --project tests/CaptionChecks -c Release
.\scripts\Build.ps1 -SelfContained
```

构建产物位于 `dist/LiveSubtitles`，进入该目录运行 `Install.cmd` 和 `Start.cmd`。Windows 构建包含中英文两份 README。

开发检查见 [CONTRIBUTING.md](CONTRIBUTING.md)，流程设计见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)，发布步骤见 [docs/PUBLISHING.md](docs/PUBLISHING.md)。GitHub Actions 构建和检查 Windows 包，不下载模型，也不需要 GPU。

## 许可证

应用代码采用 [MIT](LICENSE) 许可证。模型和第三方组件保留上游许可证，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 及 `resources/` 下的许可证文本。
