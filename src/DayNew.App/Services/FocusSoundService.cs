using System.IO;
using System.Media;

namespace DayNew.Services;

/// <summary>
/// 专注计时轻提示音效服务：内存合成柔和正弦波提示音，无外部音频资源依赖。
/// </summary>
public static class FocusSoundService
{
    private static readonly Lazy<byte[]> StartChimeWav = new(() =>
        SynthesizeChime([(523.25, 0.13), (659.25, 0.19)], amplitude: 0.16));

    private static readonly Lazy<byte[]> CompleteChimeWav = new(() =>
        SynthesizeChime([(523.25, 0.14), (659.25, 0.14), (783.99, 0.28)], amplitude: 0.20));

    public static void PlayStart() => PlayBytesAsync(StartChimeWav.Value);

    public static void PlayComplete() => PlayBytesAsync(CompleteChimeWav.Value);

    private static void PlayBytesAsync(byte[] wavBytes)
    {
        _ = Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(wavBytes, writable: false);
                using var player = new SoundPlayer(ms);
                player.PlaySync();
            }
            catch
            {
                // 无音频设备或播放异常时静默忽略，绝不影响计时流程
            }
        });
    }

    private static byte[] SynthesizeChime((double FreqHz, double DurationSec)[] notes, double amplitude)
    {
        const int sampleRate = 44100;
        const short channels = 1;
        const short bitsPerSample = 16;

        var totalSamples = 0;
        foreach (var (_, sec) in notes)
            totalSamples += (int)(sampleRate * sec);

        var dataBytes = totalSamples * (bitsPerSample / 8);
        using var ms = new MemoryStream(44 + dataBytes);
        using var bw = new BinaryWriter(ms);

        // RIFF / WAVE header
        bw.Write("RIFF"u8);
        bw.Write(36 + dataBytes);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * (bitsPerSample / 8));
        bw.Write((short)(channels * (bitsPerSample / 8)));
        bw.Write(bitsPerSample);
        bw.Write("data"u8);
        bw.Write(dataBytes);

        foreach (var (freq, sec) in notes)
        {
            var count = (int)(sampleRate * sec);
            var attackSamples = Math.Max(1, (int)(sampleRate * 0.015));

            for (var i = 0; i < count; i++)
            {
                var t = i / (double)sampleRate;
                var env = i < attackSamples
                    ? i / (double)attackSamples
                    : Math.Exp(-6.0 * (i - attackSamples) / Math.Max(1, count - attackSamples));

                // 基频 + 微弱二次谐波，音色温润不刺耳
                var wave = Math.Sin(2.0 * Math.PI * freq * t) * 0.85
                         + Math.Sin(2.0 * Math.PI * (freq * 2.0) * t) * 0.15;
                var sample = (short)Math.Clamp(wave * env * amplitude * short.MaxValue, short.MinValue, short.MaxValue);
                bw.Write(sample);
            }
        }

        bw.Flush();
        return ms.ToArray();
    }
}
