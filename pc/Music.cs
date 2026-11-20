using System.IO;
using System.Windows.Media;

namespace ConnectPC;

/// <summary>
/// Фоновая музыка без внешних файлов: приложение само синтезирует
/// спокойный эмбиент-луп (пады Am–F–C–G + лёгкая арпеджио-линия) в WAV
/// и крутит его через MediaPlayer. Громкость регулируется из шапки.
/// </summary>
public sealed class MusicPlayer : IDisposable
{
    private readonly MediaPlayer _player = new();
    private string _file = "";

    public bool IsPlaying { get; private set; }
    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0, 1);
    }

    public MusicPlayer()
    {
        _player.MediaEnded += (_, _) =>
        {
            if (!IsPlaying) return;
            _player.Position = TimeSpan.Zero;
            _player.Play();
        };
        _player.Volume = 0.35;
    }

    public void Toggle()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Play()
    {
        try
        {
            if (_file.Length == 0)
            {
                var dir = Path.Combine(Path.GetTempPath(), "Connect3D");
                Directory.CreateDirectory(dir);
                _file = Path.Combine(dir, "ambient-loop.wav");
                if (!File.Exists(_file) || new FileInfo(_file).Length < 100_000)
                    GenerateAmbientWav(_file);
                _player.Open(new Uri(_file));
            }
            _player.Play();
            IsPlaying = true;
        }
        catch
        {
            IsPlaying = false; // нет аудиоустройства — просто молчим
        }
    }

    public void Pause()
    {
        try { _player.Pause(); } catch { }
        IsPlaying = false;
    }

    public void Dispose()
    {
        try { _player.Stop(); _player.Close(); } catch { }
    }

    // ---- синтез лупа ----
    private static void GenerateAmbientWav(string path)
    {
        const int sampleRate = 22050;
        const int chordSeconds = 4;
        // Am, F, C, G — частоты нот (Гц)
        double[][] chords =
        {
            new[] { 110.00, 220.00, 261.63, 329.63 }, // Am: A2 A3 C4 E4
            new[] { 87.31, 174.61, 220.00, 261.63 },  // F:  F2 F3 A3 C4
            new[] { 130.81, 196.00, 261.63, 329.63 }, // C:  C3 G3 C4 E4
            new[] { 98.00, 196.00, 246.94, 293.66 },  // G:  G2 G3 B3 D4
        };
        double[] melodyScale = { 440.00, 523.25, 659.25, 783.99, 880.00 };
        int totalSeconds = chordSeconds * chords.Length;
        int total = sampleRate * totalSeconds;
        var samples = new double[total];
        var rnd = new Random(42);
        var melodyNotes = new List<(int start, double freq)>();
        for (int i = 0; i < totalSeconds * 2; i++)
        {
            if (rnd.NextDouble() < 0.72)
                melodyNotes.Add((i * sampleRate / 2, melodyScale[rnd.Next(melodyScale.Length)]));
        }
        for (int s = 0; s < total; s++)
        {
            double t = (double)s / sampleRate;
            int chordIdx = (int)(t / chordSeconds) % chords.Length;
            double inChord = t % chordSeconds;
            double env = Math.Min(1.0, inChord / 0.9) * Math.Min(1.0, (chordSeconds - inChord) / 0.9);
            double v = 0;
            foreach (var f in chords[chordIdx])
            {
                v += Math.Sin(2 * Math.PI * f * t) * 0.16;
                v += Math.Sin(2 * Math.PI * f * 2 * t) * 0.05;
            }
            v *= 0.55 + 0.45 * env;
            // бас
            v += Math.Sin(2 * Math.PI * chords[chordIdx][0] / 2.0 * t) * 0.20 * env;
            samples[s] = v;
        }
        foreach (var (start, freq) in melodyNotes)
        {
            int len = sampleRate / 2;
            for (int i = 0; i < len && start + i < total; i++)
            {
                double tt = (double)i / sampleRate;
                double e = Math.Exp(-tt * 5.0);
                samples[start + i] += Math.Sin(2 * Math.PI * freq * tt) * 0.10 * e;
            }
        }
        // мягкий общий фейд по краям, чтобы луп не щёлкал
        int fade = sampleRate / 4;
        for (int i = 0; i < fade; i++)
        {
            double k = (double)i / fade;
            samples[i] *= k;
            samples[total - 1 - i] *= k;
        }
        double peak = samples.Max(x => Math.Abs(x));
        if (peak > 0) for (int i = 0; i < total; i++) samples[i] = samples[i] / peak * 0.72;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        int byteRate = sampleRate * 2;
        bw.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
        bw.Write(36 + total * 2);
        bw.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
        bw.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)1); // mono
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
        bw.Write(total * 2);
        foreach (var v in samples)
            bw.Write((short)Math.Clamp(v * short.MaxValue, short.MinValue, short.MaxValue));
    }
}
