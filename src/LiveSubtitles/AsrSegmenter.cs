using System;
using System.Collections.Generic;
using System.Linq;

namespace LiveSubtitles;

public sealed record AsrWindow(long Segment, int Revision, float[] Audio, bool Final, double Start, double End, long OverlapSegment = 0);

// Segmentation runs independently of model inference, so a slow request never
// stops audio capture. Every window contains the complete current hypothesis.
public sealed class AsrSegmenter
{
    public const int FrameSamples = 512;
    private readonly Queue<(float[] Audio, double Start)> preRoll = new();
    private readonly List<float> audio = new();
    private long segment, overlapSegment;
    private int revision, lastSnapshot;
    private double start, lastVoice;
    public event Action<AsrWindow>? Window;

    public void Push(float[] frame, double time, bool voice)
    {
        if (frame.Length != FrameSamples)
            throw new ArgumentException("VAD frames must contain 512 samples.");
        preRoll.Enqueue((frame, time));
        while (preRoll.Count > 9)
            preRoll.Dequeue();
        if (audio.Count == 0)
        {
            if (!voice)
                return;
            segment++;
            revision = 0;
            overlapSegment = 0;
            start = preRoll.Peek().Start;
            foreach (var item in preRoll)
                audio.AddRange(item.Audio);
            lastSnapshot = 0;
        }
        else
            audio.AddRange(frame);
        if (voice)
            lastVoice = time + FrameSamples / 16000.0;

        if (time + FrameSamples / 16000.0 - lastVoice >= 0.608)
        {
            Emit(true);
            audio.Clear();
            preRoll.Clear();
        }
        else if (audio.Count >= 12 * AsrProtocol.SampleRate)
        {
            Emit(true);
            var tail = audio.Skip(audio.Count - 25 * FrameSamples).ToArray();
            start += (audio.Count - tail.Length) / 16000.0;
            audio.Clear();
            audio.AddRange(tail);
            overlapSegment = segment++;
            revision = 0;
            lastSnapshot = audio.Count;
        }
        else if (audio.Count - lastSnapshot >= AsrProtocol.SampleRate)
        {
            Emit(false);
            lastSnapshot = audio.Count;
        }
    }

    private void Emit(bool final) => Window?.Invoke(new(segment, ++revision, audio.ToArray(), final, start, Math.Max(start, lastVoice), overlapSegment));

    public void Reset()
    {
        audio.Clear();
        preRoll.Clear();
        overlapSegment = 0;
        lastSnapshot = 0;
    }
}

// Older drafts of the same segment are replaceable. Finals stay ordered and
// cannot be overwritten by a later draft. Overflow is explicit to the caller.
public sealed class AsrWorkQueue
{
    private readonly LinkedList<AsrWindow> jobs = new();
    private readonly int capacity;
    public AsrWorkQueue(int capacity = 8) => this.capacity = capacity;
    public int Count => jobs.Count;
    public bool Add(AsrWindow job)
    {
        for (var node = jobs.First; node != null; node = node.Next)
        {
            if (node.Value.Segment != job.Segment)
                continue;
            if (job.Revision > node.Value.Revision && !node.Value.Final)
                node.Value = job;
            return true;
        }
        if (jobs.Count >= capacity)
            return false;
        jobs.AddLast(job);
        return true;
    }
    public AsrWindow? Take()
    {
        if (jobs.First == null)
            return null;
        var job = jobs.First.Value;
        jobs.RemoveFirst();
        return job;
    }
    public void Clear() => jobs.Clear();
}
