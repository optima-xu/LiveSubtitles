"""Bounded streaming speech sessions. Capture timestamps are supplied by Windows."""
from __future__ import annotations
from collections import deque
import os
import re
import numpy as np

RATE = 16000
FRAME = 512

def remove_overlap(previous: str, current: str) -> str:
    pattern = r"[\u3400-\u9fff]|[^\W_]+(?:['’-][^\W_]+)*"
    a = list(re.finditer(pattern, previous.lower()))
    b = list(re.finditer(pattern, current.lower()))
    for n in range(min(len(a), len(b), 16), 1, -1):
        if [m.group() for m in a[-n:]] == [m.group() for m in b[:n]]:
            return current[b[n - 1].end():].lstrip(' ,，.。:：;；!?！？')
    return current

class QwenEngine:
    def __init__(self, model_path: str, memory: float):
        from qwen_asr import Qwen3ASRModel
        self.model = Qwen3ASRModel.LLM(model=model_path, gpu_memory_utilization=memory,
            max_model_len=2048, max_num_seqs=1, max_num_batched_tokens=2048,
            kv_cache_memory_bytes=256 * 1024 * 1024,
            limit_mm_per_prompt={'audio': {'count': 1, 'length': 208000}},
            attention_backend=os.getenv('ASR_ATTENTION_BACKEND', 'TRITON_ATTN'),
            enforce_eager=True, max_new_tokens=128)

    def begin(self, language, chunk):
        return self.model.init_streaming_state(language=language, chunk_size_sec=chunk,
                                               unfixed_chunk_num=2, unfixed_token_num=5)

    def decode(self, samples, state, final):
        self.model.streaming_transcribe(samples, state)
        if final: self.model.finish_streaming_transcribe(state)
        return state.text

class SileroDetector:
    def __init__(self):
        import torch
        from silero_vad import load_silero_vad
        self.torch = torch
        self.model = load_silero_vad(onnx=True)

    def reset(self): self.model.reset_states()

    def __call__(self, samples):
        return float(self.model(self.torch.from_numpy(samples.copy()), RATE).item())

class StreamProcessor:
    def __init__(self, engine, vad, session: str, language=None, chunk=1.0):
        self.engine, self.vad, self.session = engine, vad, session
        self.language, self.chunk = language, chunk
        self.segment = 0
        self.revision = 0
        self.state = None
        self.preroll = deque(maxlen=9)
        self.tail = deque(maxlen=25)
        self.pending = []
        self.pending_count = 0
        self.remainder = np.zeros(0, np.float32)
        self.remainder_time = 0.0
        self.last_text = ''
        self.previous = ''
        self.overlap = False
        self.last_voice = 0.0
        self.start = 0.0
        self.last_time = 0.0
        self.vad.reset()

    def _event(self, text, final):
        if self.overlap: text = remove_overlap(self.previous, text)
        if text == self.last_text and not final: return []
        self.last_text = text
        self.revision += 1
        if not text.strip(): return []
        return [dict(type='asr', session_id=self.session, segment_id=self.segment,
            revision=self.revision, text=text, final=final, start=self.start,
            end=max(self.start, self.last_voice))]

    def _begin(self, frames, overlap=False):
        self.segment += 1
        self.revision = 0
        self.last_text = ''
        self.overlap = overlap
        self.start = frames[0][0]
        self.last_voice = frames[-1][0] + len(frames[-1][1]) / RATE
        self.state = self.engine.begin(self.language, self.chunk)
        self.pending = [x.copy() for _, x in frames]
        self.pending_count = sum(len(x) for x in self.pending)

    def _decode(self, final=False):
        if self.state is None: return []
        audio = np.concatenate(self.pending) if self.pending else np.zeros(0, np.float32)
        self.pending, self.pending_count = [], 0
        text = self.engine.decode(audio, self.state, final)
        return self._event(text, final)

    def push(self, pcm: bytes, start: float):
        audio = np.frombuffer(pcm, dtype='<i2').astype(np.float32) / 32768.0
        events = []
        if self.remainder.size:
            audio = np.concatenate((self.remainder, audio))
            start = self.remainder_time
        consumed = 0
        while consumed + FRAME <= len(audio):
            frame = audio[consumed:consumed + FRAME]
            now = start + consumed / RATE
            consumed += FRAME
            self.last_time = now + FRAME / RATE
            voice = self.vad(frame) >= 0.5
            self.preroll.append((now, frame.copy()))
            self.tail.append((now, frame.copy()))
            if self.state is None:
                if voice: self._begin(list(self.preroll))
            else:
                self.pending.append(frame.copy())
                self.pending_count += len(frame)
                if voice: self.last_voice = self.last_time
                if self.last_time - self.last_voice >= 0.608:
                    events += self._decode(True)
                    self.previous = self.last_text
                    self.state = None
                    self.pending = []
                    self.pending_count = 0
                elif self.last_time - self.start >= 12.0:
                    events += self._decode(True)
                    self.previous = self.last_text
                    self._begin(list(self.tail), overlap=True)
                elif self.pending_count >= int(self.chunk * RATE):
                    events += self._decode()
        self.remainder = audio[consumed:].copy()
        self.remainder_time = start + consumed / RATE
        return events

    def finish(self):
        if self.state is not None and self.remainder.size:
            self.pending.append(self.remainder)
            self.pending_count += len(self.remainder)
        result = self._decode(True)
        self.state = None
        self.remainder = np.zeros(0, np.float32)
        return result

    def discontinuity(self, start, end, message):
        self.state = None
        self.pending, self.pending_count = [], 0
        self.preroll.clear(); self.tail.clear()
        self.remainder = np.zeros(0, np.float32)
        self.vad.reset()
        self.segment += 1
        return [dict(type='gap', session_id=self.session, segment_id=self.segment,
            start=start, end=end, message=message, final=True)]
