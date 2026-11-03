# Architecture and protocol

The Windows client uses WPF and NAudio. Audio capture and resampling run independently from inference. A bounded queue sends 16 kHz mono PCM16 little-endian audio to a local WebSocket server. The Python service performs Silero VAD and Qwen online recognition. The client sends source hypotheses to Ollama and renders actual streamed text.

## WebSocket

Default endpoint: `ws://127.0.0.1:17861/asr`.

Start with JSON:

```json
{"type":"start","session_id":"unique-per-start","language":"English","chunk_seconds":1.0}
```

Wait for `ready`. Each binary audio packet has a 24-byte header followed by PCM16:

| Offset | Type | Meaning |
|---|---|---|
| 0 | uint64 LE | Sequence number, starting at zero |
| 8 | uint64 LE | Cumulative sample index |
| 16 | float64 LE | Start time in seconds relative to capture session |
| 24 | PCM16 LE | Audio samples, commonly 100 ms |

A discontinuity in sequence or sample position resets recognition state and emits a visible `gap` event. Queue overflow is never reported as lossless processing.

Result:

```json
{"type":"asr","session_id":"s","segment_id":1,"revision":3,"text":"The model works.","final":false,"start":0.1,"end":2.4}
```

The source text is a complete hypothesis for its current segment, not an append-only delta. Translation requests capture the source revision. A result for an incompatible source revision, stopped session or older rendered translation is discarded. Compatible source-prefix translations may remain visible until replaced.

`{"type":"stop"}` flushes the server. Closing the socket cancels that stream. Client pause/clear invalidates the session before any late results can enter the new session.

## Resources and startup

One ASR stream and one translation request are processed at a time. ASR state rolls at silence or a 12-second bound. Long-running audio does not accumulate into an unbounded Qwen input. vLLM uses a fixed 256 MiB KV cache, a single-audio profiling limit and eager Triton attention. The 30% initialization setting is not a total GPU memory cap: explicit KV allocation bypasses automatic cache sizing. Model weights and temporary tensors need additional memory. This was selected after testing both models together on an 8 GiB laptop GPU.

The ASR health endpoint reports `loading`, `ready` or `error`. The app connects only after readiness. Models are prewarmed before capture. Runtime dependencies are pinned in `server/requirements-linux.lock.txt`; Windows dependencies use `packages.lock.json`.

Local ASR startup uses offline Hugging Face mode and requires installed weights. The owning client generates a random control token. On stop/exit, a token-protected loopback endpoint terminates only the ASR service's dedicated Linux process group; the client also closes only Windows services it started. Reused external services remain running.

## Timestamps

Audio timestamps originate on Windows. ASR and translation results carry those positions back. UI latency compares them with the same Windows monotonic clock; timestamps from separate operating systems are not directly subtracted. VAD determines approximate source segment bounds. SRT is sentence-level, not word-aligned.

## Privacy and storage

Services bind to loopback. Runtime/model paths are per-installation configuration and are not tracked by Git. Transcript history is held in memory until the user exports it. Diagnostic logs contain status/errors and timing, not transcript text. External endpoint configuration is an explicit advanced setting; local defaults do not upload audio to a cloud model.
