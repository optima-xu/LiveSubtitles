"""Create the app's isolated Linux runtime; run with WSL python3."""
import os, pathlib, subprocess, sys, urllib.request
root = pathlib.Path(sys.argv[1]).resolve()
root.mkdir(parents=True, exist_ok=True)
venv = root / 'asr-venv'
os.environ['PIP_CACHE_DIR'] = str(root / 'pip-cache')
os.environ['UV_CACHE_DIR'] = str(root / 'uv-cache')
os.environ['HF_HOME'] = str(root / 'huggingface')
subprocess.run([sys.executable, '-m', 'venv', '--without-pip', str(venv)], check=True)
python = str(venv / 'bin/python')
uv = root / 'uv'
if not uv.exists():
    pip = root / 'pip.pyz'
    if not pip.exists(): urllib.request.urlretrieve('https://bootstrap.pypa.io/pip/pip.pyz', pip)
    subprocess.run([python, str(pip), 'install', 'uv==0.9.26'], check=True)
    uv = venv / 'bin/uv'
index = ['--index-url', os.environ['ASR_PIP_INDEX_URL']] if os.getenv('ASR_PIP_INDEX_URL') else []
lock = pathlib.Path(__file__).with_name('requirements-linux.lock.txt')
packages = ['-r', str(lock)] if lock.exists() else [
    'qwen-asr[vllm]==0.0.6', 'fastapi', 'uvicorn', 'websockets', 'silero-vad==6.2.0', 'onnxruntime', 'pytest', 'httpx']
subprocess.run([str(uv), 'pip', 'install', '--python', python, *index, *packages], check=True)
with (root / 'requirements-resolved.txt').open('w') as f:
    subprocess.run([str(uv), 'pip', 'freeze', '--python', python], stdout=f, check=True)
print('Runtime installed', flush=True)
