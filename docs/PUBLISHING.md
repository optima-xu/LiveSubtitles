# Publishing a release / 发布版本

Repository: [optima-xu/LiveSubtitles](https://github.com/optima-xu/LiveSubtitles).

`README.md` is the English landing page; `README.zh-CN.md` is the Chinese guide. Keep the installation steps, model names, supported-language counts, validation boundaries, and download links consistent. `scripts/Build.ps1` includes both documents in the Windows package.

## Maintainer workflow

1. Update the application version in `src/LiveSubtitles/LiveSubtitles.csproj` and the release references in both READMEs.
2. Run the model-free checks and build the Windows package. Run the real-model probes when changing capture, segmentation, model adapters, or scheduling, and record the actual scope in `TESTING.md`.
3. Commit the source and documentation, push `main`, and inspect the Windows workflow results.
4. Create a version tag matching the application version, then push it:

```powershell
git tag v0.3.0
git push origin v0.3.0
```

The release workflow checks the tag/version match, runs subtitle and layout checks, builds a self-contained Windows x64 application, and creates a **draft** GitHub Release with:

- `LiveSubtitles-v0.3.0-windows-x64.zip`
- `SHA256SUMS.txt`

GitHub also provides source archives for the tag. Inspect the draft package and release notes, then publish it from GitHub Releases. Model weights and runtimes are downloaded by `Install.cmd`, not committed or bundled into the application ZIP. Cloud builds do not download models or test GPU inference.

## 中文说明

英文主页为 `README.md`，中文说明为 `README.zh-CN.md`，发布包同时包含两份文档。发布前保持安装步骤、模型名称、验证范围及链接一致。

先提交并推送源码，检查 Windows 构建，再推送与程序版本一致的标签。发布工作流会检查版本、运行字幕和布局测试，并创建含 Windows 安装包和 SHA256 校验文件的 Release 草稿。检查文件与说明后发布，用户即可从 Releases 下载。

接收者完整解压 Windows 包后，首次运行 `Install.cmd`，日常运行 `Start.cmd`。安装器自动下载模型，复用现有 Ollama 或下载 Windows 便携版，无需 WSL。源码仓库、自动构建产物和 Release 均不应包含个人配置、字幕、日志、模型缓存或认证信息。
