# 发布到 GitHub

本目录可以作为独立 GitHub 仓库。客户端源码、安装器、模型依赖锁和自动构建流程都在仓库中，运行时模型与个人字幕数据不在仓库中。

1. 在 GitHub 新建空仓库，自行选择名称与可见性。
2. 将本地项目关联到你的仓库；下面 URL 中的账号与仓库名需替换。

```powershell
# 从纯源码 ZIP 开始时，先执行这三行；已经带有 Git 历史的目录可以跳过。
git init -b main
git add .
git commit -m "Initial LiveSubtitles application"

git remote add origin https://github.com/YOUR-ACCOUNT/LiveSubtitles.git
git push -u origin main
```

3. 在 Actions 中确认 Windows 构建、字幕测试和 Python 流式测试通过。
4. 需要发版本时，推送版本标签。发布工作流只创建草稿，检查安装包后再发布。

```powershell
git tag v0.1.0
git push origin v0.1.0
```

GitHub 构建不下载模型、不使用 GPU，也不包含本机配置。真正的识别翻译速度仍需在目标硬件验证。接收者解压 Windows ZIP，按 README 准备 WSL2 与 Ollama，然后运行 Install.cmd 和 Start.cmd。
