import json
from pathlib import Path
import struct
import sys

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'server'))
from model_integrity import validate_safetensors


def fixture_bytes():
    header = json.dumps({'weight': {'dtype': 'F32', 'shape': [2], 'data_offsets': [0, 8]}}).encode()
    return struct.pack('<Q', len(header)) + header + bytes(8)


def test_complete_tensor_file(tmp_path):
    data = fixture_bytes()
    file = tmp_path / 'model.safetensors'
    file.write_bytes(data)
    assert validate_safetensors(file) == len(data)


def test_truncated_download_is_rejected(tmp_path):
    file = tmp_path / 'model.safetensors'
    file.write_bytes(fixture_bytes()[:-1])
    with pytest.raises(ValueError, match='incomplete'):
        validate_safetensors(file)


def test_extra_bytes_are_rejected(tmp_path):
    file = tmp_path / 'model.safetensors'
    file.write_bytes(fixture_bytes() + b'x')
    with pytest.raises(ValueError, match='incomplete'):
        validate_safetensors(file)
