# Third-party components

The application code in this repository is MIT licensed. Dependencies and model weights keep their own licenses and are downloaded separately.

| Component | Upstream | License |
|---|---|---|
| NAudio | https://github.com/naudio/NAudio | MIT |
| WPF / .NET | https://github.com/dotnet/wpf | MIT |
| Qwen3-ASR | https://github.com/QwenLM/Qwen3-ASR | Apache-2.0 |
| HY-MT2-1.8B (official Q8_0 GGUF and chat template) | https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF | Apache-2.0 |
| ONNX Runtime | https://github.com/microsoft/onnxruntime | MIT |
| Silero VAD | https://github.com/snakers4/silero-vad | MIT |
| Ollama | https://github.com/ollama/ollama | MIT |

ASR GGUF conversion is maintained by ggml-org: https://huggingface.co/ggml-org/Qwen3-ASR-0.6B-GGUF . The Ollama audio transport convention was checked against the model packager's example at https://ollama.com/frozenlab/qwen3-asr ; installation downloads pinned ggml-org weights directly rather than depending on that mutable tag.

Full model and Silero license texts are in `resources/`. See each upstream distribution for its notices and transitive dependency licenses. No model weights are committed to this repository or included in the application ZIP. The optional portable Ollama distribution is downloaded separately from its official GitHub release, with the release SHA256 verified before extraction.
