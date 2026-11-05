# Publishing a release / 发布版本

Repository: [optima-xu/LiveSubtitles](https://github.com/optima-xu/LiveSubtitles).

`README.md` is the English landing page and `README.zh-CN.md` is the Chinese guide. Both describe the setup wizard and ship with the application.

## Maintainer workflow

1. Update the application version in `src/LiveSubtitles/LiveSubtitles.csproj` and the release references in both READMEs.
2. Run the model-free checks and build the Windows package. Run the real-model probes when changing capture, segmentation, model adapters, or scheduling, and record the actual scope in `TESTING.md`.
3. Commit the source and documentation, push `main`, and inspect the Windows workflow results.
4. Create a version tag matching the application version, then push it:

```powershell
git tag v0.4.2
git push origin v0.4.2
```

The release workflow checks the tag/version match, builds the self-contained app and installer, then tests the installer, shortcuts, repeat installation and uninstall on a clean Windows runner. A successful run creates a **draft** GitHub Release containing:

- `LiveSubtitles-Setup-v0.4.2-windows-x64.exe`
- `LiveSubtitles-v0.4.2-windows-x64.zip`
- `SHA256SUMS.txt`

Run `scripts/Build-Installer.ps1` to make the installer locally. It builds the app and uses a pinned portable Inno Setup compiler under `dist/build-tools`. The normal CI build is model-free. The tagged release test downloads models through the production installer. Check both asset hashes before publishing the draft.

## 中文说明

先推送源码并检查 Windows 构建，再推送与程序版本一致的标签。发布流程在干净的 Windows 环境中实际安装、启动、覆盖安装并卸载，检查桌面和开始菜单快捷方式。Release 草稿包含图形安装包、便携 ZIP 及二者的 SHA256 校验值。核对后即可发布。
