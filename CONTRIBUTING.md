# Contributing

Use Windows x64 and the .NET 9 SDK. Runtime releases are self-contained; development needs no Python environment.

```powershell
dotnet run --project tests/CaptionChecks -c Release
dotnet run --project tests/LayoutProbe -c Release -- artifacts/layout
.\scripts\Build.ps1 -SelfContained
.\scripts\Build-Installer.ps1 -PayloadDirectory .\dist\LiveSubtitles
```

The first command checks session/revision ordering, export, language prompts, WAV transport, VAD-driven segmentation, rollover, gaps and bounded queues without downloading models. `IntegrationProbe` uses real installed models and supplied audio; see TESTING.md. CPU state tests and successful builds are not evidence of GPU inference quality or speed.

Keep capture, segmentation, recognition, translation and display lifecycles separate. Never block the audio callback on model requests, append separate ASR hypotheses as token deltas, silently drop finals, or let old sessions overwrite current captions. Keep state and queues bounded and display missing audio explicitly. Preserve comments and unrelated changes.

Pin model/runtime revisions and checksums in `resources/models.lock.json`, retain upstream license notices, and test the real request path when changing adapters. Do not commit model weights, downloaded runtimes, local settings, transcripts or diagnostic logs. CI runs model-free Windows checks; GPU integration remains a separate manual verification step.
