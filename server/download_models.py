import argparse
import hashlib
import json
import os
from pathlib import Path
os.environ.setdefault('HF_HUB_DISABLE_XET', '1')
from huggingface_hub import snapshot_download
from model_integrity import validate_safetensors
p=argparse.ArgumentParser()
p.add_argument('model', choices=['Qwen/Qwen3-ASR-0.6B','Qwen/Qwen3-ASR-1.7B'])
p.add_argument('destination')
a=p.parse_args()
target=Path(a.destination)/a.model.rsplit('/',1)[-1]
lock=json.loads(Path(__file__).with_name('models.lock.json').read_text())
spec=lock.get(a.model, {})
snapshot_download(a.model, local_dir=target, revision=spec.get('revision'),
    allow_patterns=['*.json','*.safetensors','*.txt'])
weights=list(target.glob('*.safetensors'))
if not weights: raise ValueError('Model weights are missing; retry installation.')
for file in weights:
    validate_safetensors(file)
    if file.name=='model.safetensors' and spec.get('safetensors_sha256'):
        with file.open('rb') as stream: actual=hashlib.file_digest(stream, 'sha256').hexdigest()
        if actual!=spec['safetensors_sha256']: raise ValueError('Model checksum mismatch; remove the corrupted model file and retry installation.')
print('Model files passed integrity checks.')
