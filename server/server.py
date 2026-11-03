"""Local single-stream WebSocket ASR service; no cloud inference."""
from __future__ import annotations
import argparse
import asyncio
from contextlib import asynccontextmanager
import json
import logging
import math
import os
from pathlib import Path
import secrets
import signal
import struct
import threading
import time

from fastapi import FastAPI, HTTPException, Request, WebSocket, WebSocketDisconnect
import uvicorn
from streaming import QwenEngine, SileroDetector, StreamProcessor
from model_integrity import validate_safetensors

log = logging.getLogger('live-subtitles')
engine = None
detector = None
state = {'status': 'loading', 'message': '正在加载语音模型'}
busy = False
model_name = ''

def load(model, memory):
    global engine, detector
    try:
        detector = SileroDetector()
        local = Path(os.environ['APP_MODEL_DIRECTORY']) / model.rsplit('/', 1)[-1]
        weights = list(local.glob('*.safetensors'))
        if not weights: raise ValueError('ASR model weights missing. Rerun Install.ps1.')
        for file in weights: validate_safetensors(file)
        engine = QwenEngine(str(local), memory)
        state.update(message='语音模型已加载，正在预热推理')
        import numpy as np
        warm = engine.begin('English', 1.0)
        engine.decode(np.zeros(16000, dtype=np.float32), warm, True)
        state.update(status='ready', message='语音识别已就绪')
    except Exception as exc:
        log.exception('Cannot load ASR')
        state.update(status='error', message=str(exc))

@asynccontextmanager
async def lifespan(app):
    threading.Thread(target=load, args=(model_name, float(os.getenv('ASR_GPU_MEMORY', '0.30'))), daemon=True).start()
    yield

app = FastAPI(lifespan=lifespan, docs_url=None, redoc_url=None)

@app.get('/health')
async def health():
    return dict(state, model=model_name, busy=busy, protocol=1)

@app.post('/shutdown')
async def shutdown(request: Request):
    token = os.getenv('LIVE_SUBTITLES_CONTROL_TOKEN', '')
    if not token or not secrets.compare_digest(request.headers.get('X-Control-Token', ''), token):
        raise HTTPException(status_code=403)
    # The entry point creates a dedicated process group; only this service and its workers exit.
    asyncio.get_running_loop().call_later(0.2, lambda: os.killpg(os.getpgrp(), signal.SIGKILL))
    return {'status': 'stopping'}

@app.websocket('/asr')
async def asr(ws: WebSocket):
    global busy
    await ws.accept()
    if busy or state['status'] != 'ready':
        await ws.send_json(dict(type='error', message='语音服务正忙或尚未就绪'))
        await ws.close(code=1013)
        return
    busy = True
    receiver = None
    try:
        hello = await asyncio.wait_for(ws.receive_json(), timeout=10)
        if hello.get('type') != 'start' or not isinstance(hello.get('session_id'), str):
            await ws.close(code=1008); return
        session = hello['session_id'][:80]
        chunk = min(2.0, max(0.5, float(hello.get('chunk_seconds', 1))))
        processor = StreamProcessor(engine, detector, session, hello.get('language'), chunk)
        queue = asyncio.Queue(maxsize=20)
        overrun = False
        ended = False
        last_end = 0.0
        expected = 0
        expected_samples = 0

        async def receive():
            nonlocal overrun, ended
            try:
                while True:
                    packet = await ws.receive()
                    if packet['type'] == 'websocket.disconnect': break
                    if packet.get('text'):
                        control = json.loads(packet['text'])
                        if control.get('type') == 'stop': break
                        continue
                    data = packet.get('bytes')
                    if data is None: continue
                    if len(data) < 24 or len(data) > 64024 or (len(data) - 24) % 2:
                        raise ValueError('Invalid PCM frame')
                    if queue.full():
                        overrun = True
                        while not queue.empty(): queue.get_nowait()
                    queue.put_nowait(data)
            finally:
                ended = True

        receiver = asyncio.create_task(receive())
        await ws.send_json(dict(type='ready', session_id=session, message='识别流已连接'))
        while not ended or not queue.empty():
            try: data = await asyncio.wait_for(queue.get(), timeout=0.25)
            except asyncio.TimeoutError: continue
            seq, samples, start = struct.unpack('<QQd', data[:24])
            if not math.isfinite(start) or start < 0: raise ValueError('Invalid timestamp')
            if seq != expected or samples != expected_samples or overrun:
                for event in await asyncio.to_thread(processor.discontinuity, last_end, start, '处理积压或音频中断，已跳过此区间'):
                    await ws.send_json(event)
                overrun = False
            expected = seq + 1
            expected_samples = samples + (len(data) - 24) // 2
            last_end = start + (len(data) - 24) / 32000
            t = time.perf_counter()
            events = await asyncio.to_thread(processor.push, data[24:], start)
            for event in events:
                event['processing_ms'] = round((time.perf_counter() - t) * 1000)
                event['queue_ms'] = queue.qsize() * 100
                await ws.send_json(event)
        if receiver.done(): await receiver
        for event in await asyncio.to_thread(processor.finish): await ws.send_json(event)
    except WebSocketDisconnect:
        pass
    except Exception as exc:
        log.exception('ASR stream failed')
        try: await ws.send_json(dict(type='error', message=str(exc)))
        except Exception: pass
    finally:
        if receiver:
            receiver.cancel()
            try: await receiver
            except BaseException: pass
        busy = False
        try: await ws.close()
        except Exception: pass

if __name__ == '__main__':
    if os.getpgrp() != os.getpid(): os.setpgrp()
    parser = argparse.ArgumentParser()
    parser.add_argument('--model', default='Qwen/Qwen3-ASR-0.6B')
    parser.add_argument('--runtime', required=True)
    parser.add_argument('--port', type=int, default=17861)
    parser.add_argument('--gpu-memory', type=float, default=0.30)
    args = parser.parse_args()
    runtime = Path(args.runtime).resolve()
    os.environ.setdefault('HF_HOME', str(runtime / 'huggingface'))
    os.environ.setdefault('XDG_CACHE_HOME', str(runtime / 'cache'))
    os.environ.setdefault('VLLM_CACHE_ROOT', str(runtime / 'vllm-cache'))
    os.environ['APP_MODEL_DIRECTORY'] = str(runtime / 'models')
    os.environ['VLLM_NO_USAGE_STATS'] = '1'
    os.environ['DO_NOT_TRACK'] = '1'
    os.environ['ASR_GPU_MEMORY'] = str(args.gpu_memory)
    os.environ['TOKENIZERS_PARALLELISM'] = 'false'
    model_name = args.model
    uvicorn.run(app, host='127.0.0.1', port=args.port, log_level='info')
