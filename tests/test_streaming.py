import sys
from pathlib import Path
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'server'))
from streaming import StreamProcessor, remove_overlap

class FakeEngine:
    def begin(self, language, chunk): return {'samples': 0, 'chunk': chunk}
    def decode(self, samples, state, final):
        state['samples'] += len(samples)
        words = max(1, state['samples'] // 1600)
        return ' '.join(['word'] * words)

class TestVad:
    def reset(self): pass
    def __call__(self, samples): return float(np.max(np.abs(samples)) > .1)

def audio(seconds, voiced=True):
    return (np.ones(int(16000*seconds), dtype='<i2') * (16000 if voiced else 0)).tobytes()

def test_partial_before_sentence_end_and_silence_final():
    p=StreamProcessor(FakeEngine(), TestVad(), 's')
    events=[]
    for i in range(30): events += p.push(audio(.1), i*.1)
    assert len(events)>=2 and not any(e['final'] for e in events)
    for i in range(30,40): events += p.push(audio(.1,False), i*.1)
    finals=[e for e in events if e['final']]
    assert len(finals)==1
    assert finals[0]['end'] < 3.1

def test_silence_does_not_invoke_asr():
    class NeverEngine:
        def begin(self,*args): raise AssertionError('ASR must not start on silence')
    p=StreamProcessor(NeverEngine(),TestVad(),'s')
    for i in range(100): assert p.push(audio(.1,False),i*.1)==[]

def test_long_continuous_audio_rolls_state():
    p=StreamProcessor(FakeEngine(),TestVad(),'s')
    events=[]
    for i in range(300): events += p.push(audio(.1),i*.1)
    assert len({e['segment_id'] for e in events})>=3
    assert sum(e['final'] for e in events)>=2
    assert p.last_time-p.start<=12.1

def test_gap_resets_decoder_without_faking_final():
    p=StreamProcessor(FakeEngine(),TestVad(),'s')
    p.push(audio(1.1),0)
    gap=p.discontinuity(1.1,2.5,'lost')
    assert p.state is None and p.pending_count==0
    assert gap[0]['type']=='gap' and gap[0]['start']==1.1 and gap[0]['end']==2.5

def test_overlap_only_removes_boundary_suffix():
    assert remove_overlap('We use a small model','small model for captions')=='for captions'
    assert remove_overlap('We use a small model','A different sentence')=='A different sentence'
    assert remove_overlap('The model works','The next model works too')=='The next model works too'

def test_partial_pcm_frames_are_retained():
    p=StreamProcessor(FakeEngine(),TestVad(),'s',chunk=.5)
    b=audio(1)
    events=[]
    for start in range(0,len(b),200): events += p.push(b[start:start+200],start/32000)
    events += p.finish()
    assert events and events[-1]['final']
