import json
from pathlib import Path
import struct

def validate_safetensors(path):
    path=Path(path)
    with path.open('rb') as f:
        header_size=struct.unpack('<Q',f.read(8))[0]
        if header_size>100_000_000: raise ValueError(f'Invalid model header: {path.name}')
        header=json.loads(f.read(header_size))
    extent=max(v['data_offsets'][1] for k,v in header.items() if k!='__metadata__')
    expected=8+header_size+extent
    if path.stat().st_size!=expected:
        raise ValueError(f'Model download incomplete: {path.name}; expected {expected} bytes, got {path.stat().st_size}. Rerun Install.ps1.')
    return expected
