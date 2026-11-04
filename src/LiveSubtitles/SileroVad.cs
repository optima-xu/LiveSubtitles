using System;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace LiveSubtitles;

public sealed class SileroVad : IDisposable
{
    private readonly InferenceSession session;
    private float[] state = new float[256];
    private readonly float[] context = new float[64];
    public SileroVad(string path)
    {
        using var options = new SessionOptions { InterOpNumThreads = 1, IntraOpNumThreads = 1 };
        session = new InferenceSession(path, options);
    }
    public bool IsSpeech(float[] frame)
    {
        if (frame.Length != 512)
            throw new ArgumentException("Silero VAD requires 512 samples at 16 kHz.");
        var input = new float[576];
        context.CopyTo(input, 0);
        frame.CopyTo(input, 64);
        var values = new[]
        {
            NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(input, new[] { 1, 576 })),
            NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(state, new[] { 2, 1, 128 })),
            NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new long[] { 16000 }, Array.Empty<int>()))
        };
        using var result = session.Run(values);
        float probability = result.First(x => x.Name == "output").AsTensor<float>().First();
        state = result.First(x => x.Name == "stateN").AsTensor<float>().ToArray();
        Array.Copy(frame, frame.Length - context.Length, context, 0, context.Length);
        return probability >= 0.5f;
    }
    public void Reset()
    {
        Array.Clear(state);
        Array.Clear(context);
    }
    public void Dispose() => session.Dispose();
}
