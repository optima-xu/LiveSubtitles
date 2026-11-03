# Contributing

Use Windows 11 x64 and .NET 9 SDK for client changes. Python streaming-state tests run on Windows or Linux with NumPy and pytest; GPU integration tests require the documented Windows / WSL2 / NVIDIA setup.

```powershell
dotnet run --project tests/CaptionChecks -c Release
dotnet run --project tests/LayoutProbe -c Release -- artifacts/layout
python -m pytest tests -q
.\scripts\Build.ps1 -SelfContained
```

`IntegrationProbe` is optional and runs actual model inference. It does not play sound. In managed mode it loads both models before audio is sent, and stops only services it started:

```powershell
dotnet run --project tests/IntegrationProbe -c Release -- --managed . dist/LiveSubtitles/data/settings.json sample.wav 3
```

Use the same client protocol and translation scheduler as the application. Record cold startup separately from subtitle latency. Report queue gaps, timeouts and unfinished translations; do not omit them from performance results. Keep models, audio, exported captions, logs and local configuration out of commits.

Before tagging a release, run both CI suites, inspect the UI on Windows, and update TESTING.md with the hardware, model versions, real test duration and known limits. The release workflow produces a draft; the maintainer reviews the ZIP before publication.
