# LiveSubtitles · 本地实时翻译字幕

Windows 桌面应用：持续接收电脑播放的声音，边听边识别、边译边显示双语悬浮字幕。界面采用传统 Windows 控件。

**状态：0.1 开发版。** 实际验证范围与限制见 [TESTING.md](TESTING.md)。不把设计延迟目标当成已实现的性能保证。

![字幕组件的实际渲染](docs/assets/subtitles.png)

## 功能

- WASAPI 系统音频采集：指定输出设备，或跟随系统默认耳机/扬声器。
- Qwen3-ASR 在线识别，在讲话尚未结束时输出草稿，停顿后定稿。
- Ollama + Qwen3.5 4B 文本翻译，增量显示译文；支持术语表与最近三条上下文。
- 双语 / 仅译文悬浮窗，置顶、不主动抢焦点，可拖动、调字号、调透明度、锁定和鼠标穿透。
- 托盘、全局快捷键、暂停 / 恢复、字幕历史、TXT / 句段级 SRT 导出。
- 版本号过滤过期译文、队列上限、音频缺失标记、模型错误提示。
- 推理在本机进行，默认不保存原始音频。首次安装会联网下载运行依赖和模型。

## 环境

- Windows 11 x64；WSL2 Ubuntu 24.04。
- 支持 CUDA 的 NVIDIA 显卡及可用于 WSL 的驱动。8GB 显存是本项目首测配置，不保证所有 8GB 显卡均达到相同速度。
- 安装 [Ollama for Windows](https://ollama.com/download/windows)。
- 建议至少预留 25GB 磁盘空间，首次安装下载量较大。
- 从源码构建需要 .NET 9 SDK；自包含发布包无需另装 .NET Runtime。

WSL 尚未安装时，在有相应系统权限的终端运行 `wsl --install -d Ubuntu-24.04`，完成 Windows 要求的重启和 Ubuntu 初始设置，再继续。

## 安装与启动

从 GitHub 克隆本仓库，或下载并解压 Windows 发布包，然后在项目目录运行：

也可以双击 `Install.cmd` 安装，再双击 `Start.cmd` 启动。请完整解压到一个当前用户可写的目录；不要只复制应用的 EXE 文件。

```powershell
.\Install.ps1
.\Start.ps1
```

如果通过源码安装，安装器会构建 `dist/LiveSubtitles`。应用的模型、运行日志和本机配置不会提交到 Git。

可指定目录和 Python 包镜像：

```powershell
.\Install.ps1 -RuntimeDirectory 'D:\LiveSubtitlesData' `
  -WslDistribution 'Ubuntu-24.04' `
  -PythonIndexUrl 'https://pypi.tuna.tsinghua.edu.cn/simple'
```

安装器会：

1. 检查现有 WSL 发行版。
2. 补齐 WSL 的编译组件，为当前 Linux 用户创建专用 `/opt/live-subtitles` 目录，安装锁定版本的 Python 环境。该目录使用 WSL 原生文件系统，避免大量依赖安装到 Windows 挂载盘时的性能问题。
3. 下载固定版本的 Qwen3-ASR 0.6B，检查权重结构及 SHA256；模型文件放到选定的 Windows 数据目录。
4. 在 `127.0.0.1:11436` 启动专用 Ollama 服务，下载 `qwen3.5:4b-q4_K_M`。
5. 生成本机设置；有源码而无可执行程序时构建客户端。

安装过程可重复执行。它不自动启用 Windows 系统功能，不删除已有 Ollama 模型。专用端口已被其他服务占用时，可用 `-OllamaPort` 指定其他端口。

## 使用

1. 选择“系统默认输出”或具体耳机/扬声器。
2. 选择识别语言和译文语言，点击“开始”。第一次模型预热会慢一些，状态栏会显示进度。
3. 正常播放课程、浏览器视频或会议声音。
4. 用“锁定 / 穿透”让字幕不挡住鼠标；需要移动时解除锁定。

默认快捷键：

| 操作 | 快捷键 |
|---|---|
| 开始 / 暂停 | Ctrl+Alt+Space |
| 锁定 / 解锁 | Ctrl+Alt+L |
| 显示 / 隐藏字幕 | Ctrl+Alt+H |

快捷键冲突时可在高级设置选择其他按键，重启生效。暂停会丢弃待处理草稿并创建新的识别会话，恢复后不会继续回放暂停前积压的字幕。未定稿和缺失内容在历史与导出中有明确标记。

“暂停”保留已经加载的模型以便快速恢复；“停止”和“退出”关闭由本应用启动的模型服务。连接到原本已运行的外部服务时，服务仍由其原来的程序管理。

术语表填写在高级设置中，例如：

```text
gradient descent = 梯度下降
information retrieval = 信息检索
```

## 模型与流式方式

默认采用 Qwen3-ASR 0.6B 与 Qwen3.5 4B Q4，保留 1.7B 识别模型配置。切换为 1.7B 前需要重新测试显存与速度，不能仅根据参数规模假设它在本机同样实时。

语音识别运行于 WSL2，Windows 负责音频采集、翻译调度和界面。Qwen 官方流式包装器会重算当前语音段的累计音频，本应用将状态限制在约 12 秒并处理边界重叠；它并不是只编码新增帧的缓存式声学模型。

程序连续发送 PCM，识别和译文都在讲话中更新。翻译采用子句草稿与修订策略，无法保证英语句子未说完时就给出永远不变的中文语序。

默认识别端使用 Triton attention、单音频上限和 256MiB KV 缓存。这是本机联调后的配置；它避免了实测遇到的默认 attention 预热卡住及双模型自动显存预估失败。模型冷启动与预热可能需要约 1–3 分钟，请等待“正在运行”后再开始需要完整字幕的内容。

## 从源码构建与测试

```powershell
dotnet run --project tests/CaptionChecks -c Release
.\scripts\Build.ps1 -SelfContained
```

流式状态测试只需要 Python、NumPy 和 pytest，不下载 GPU 模型：

```bash
python -m pip install numpy==2.2.6 pytest==8.3.5
python -m pytest tests -q
```

GitHub Actions 在 Windows 构建可分发程序，在 Linux 运行分段测试。推送 `v*` 标签会生成 Windows ZIP 的 **Draft Release**，需要维护者检查后发布。代码不包含机器用户名、运行目录、模型权重或字幕记录。

目录：

```text
src/LiveSubtitles/       WPF 客户端、音频、识别客户端、翻译与字幕状态
server/                 Python 流式识别服务、完整性检查、依赖锁
tests/                  分段、静音、滚动、丢帧和过期译文测试
scripts/                构建与诊断脚本
.github/workflows/      CI 与发布流程
```

通信约定和状态处理见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

完整开发与真实模型测试方法见 [CONTRIBUTING.md](CONTRIBUTING.md)。[GitHub 发布步骤](docs/PUBLISHING.md)说明如何关联仓库和生成版本草稿；模型权重不会随仓库上传。第一次发布建议先检查 Actions 构建，再推送版本标签。

## 诊断与限制

```powershell
.\scripts\Diagnose.ps1
```

客户端日志位于发布目录 `data/diagnostics.log`，仅记录运行错误与性能事件；原文和译文不主动写入该日志。首次运行识别模型需要加载和预热，不能用冷启动耗时代表正常字幕延迟。

- 第一版针对单路系统音频，尚不支持单个应用音频隔离、多人重叠语音分离和语音配音。
- 悬浮窗面向普通窗口和无边框全屏，独占全屏兼容性不作保证。
- 句段时间基于采集时间和 VAD，不是逐词强制对齐。
- 历史上限 5000 条；长会话请及时导出。
- GPU 过载、缺失语音与翻译超时会显示状态，不能当作无损转录。
- 更换识别模型、WSL 目录或服务端口后，需要重启相应本地服务。

## 许可

本仓库代码采用 MIT。模型与依赖保留各自许可，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
