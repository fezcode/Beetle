using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Beetle.Core;

/// <summary>Something that listens to audio and keeps a <see cref="MusicAnalyzer"/> fed.</summary>
public interface IAudioSource : IDisposable
{
    MusicAnalyzer Analyzer { get; }
    void Start();
    void Stop();
}

/// <summary>
/// Listens to whatever the PC is playing, through WASAPI loopback on the default output device:
/// music, video and game audio, never the microphone. Each captured chunk is mixed to mono and fed to
/// <see cref="Analyzer"/> on NAudio's capture thread. While nothing plays Windows delivers no data,
/// which the analyzer reads as silence.
/// </summary>
public sealed class LoopbackAudioSource : IAudioSource
{
    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT: the shared-mode mix format Windows usually hands loopback capture.
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private WasapiLoopbackCapture? _capture;
    private float[] _mono = [];

    public MusicAnalyzer Analyzer { get; } = new();

    public void Start()
    {
        if (_capture is not null) return;
        var capture = new WasapiLoopbackCapture();
        capture.DataAvailable += OnData;
        capture.StartRecording();
        _capture = capture;
    }

    public void Stop()
    {
        var capture = _capture;
        _capture = null;
        if (capture is null) return;
        capture.DataAvailable -= OnData;
        try { capture.StopRecording(); } catch (InvalidOperationException) { /* already stopped */ }
        capture.Dispose();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiLoopbackCapture capture || e.BytesRecorded <= 0) return;
        var format = capture.WaveFormat;
        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample <= 0) return;
        var frames = e.BytesRecorded / (bytesPerSample * channels);
        if (_mono.Length < frames) _mono = new float[frames];

        var data = e.Buffer.AsSpan(0, e.BytesRecorded);
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormat);
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var offset = (f * channels + c) * bytesPerSample;
                sum += isFloat && bytesPerSample == 4
                    ? BitConverter.ToSingle(data.Slice(offset, 4))
                    : bytesPerSample == 2 ? BitConverter.ToInt16(data.Slice(offset, 2)) / 32768f : 0;
            }
            _mono[f] = sum / channels;
        }
        Analyzer.Feed(_mono.AsSpan(0, frames), format.SampleRate);
    }

    public void Dispose() => Stop();
}
