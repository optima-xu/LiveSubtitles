#!/usr/bin/env bash
set -euo pipefail
server_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
runtime="$1"
model="${2:-Qwen/Qwen3-ASR-0.6B}"
linux_runtime="${3:-/opt/live-subtitles}"
port="${4:-17861}"
export LIVE_SUBTITLES_CONTROL_TOKEN="${5:-}"
export HF_HOME="$linux_runtime/huggingface"
export XDG_CACHE_HOME="$linux_runtime/cache"
export VLLM_CACHE_ROOT="$linux_runtime/vllm-cache"
export VLLM_NO_USAGE_STATS=1
export DO_NOT_TRACK=1
export TOKENIZERS_PARALLELISM=false
export HF_HUB_OFFLINE=1
export TRANSFORMERS_OFFLINE=1
exec "$linux_runtime/asr-venv/bin/python" "$server_dir/server.py" --runtime "$runtime" --model "$model" --port "$port"
