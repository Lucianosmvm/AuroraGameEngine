using Aurora.Runtime.Assets;
using NVorbis;
using Silk.NET.OpenAL;

namespace Aurora.Runtime.Audio;

/// <summary>
/// Gerencia contexto OpenAL, pool de fontes e cache de clips.
/// Inicializa graciosamente se o dispositivo de áudio não estiver disponível.
/// </summary>
public sealed class AudioManager : IDisposable
{
    private const int SfxPoolSize = 16;

    private readonly IAssetSource _source;
    private readonly AL? _al;
    private readonly ALContext? _alc;
    private readonly Dictionary<string, AudioClip> _clips = new(StringComparer.OrdinalIgnoreCase);
    private readonly uint[] _sfxPool = new uint[SfxPoolSize];
    private uint _musicSource;
    private nint _device;
    private nint _context;
    private float _masterVolume = 1f;

    /// <summary>False quando nenhum dispositivo de áudio foi encontrado. Todas as chamadas são no-op.</summary>
    public bool IsAvailable { get; }

    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Math.Clamp(value, 0f, 1f);
            if (IsAvailable)
                _al!.SetListenerProperty(ListenerFloat.Gain, _masterVolume);
        }
    }

    public unsafe AudioManager(IAssetSource source)
    {
        _source = source;

        try
        {
            _alc = ALContext.GetApi();
            _al = AL.GetApi();

            Device* device = _alc.OpenDevice(null);
            if (device == null)
            {
                Cleanup();
                return;
            }

            Context* context = _alc.CreateContext(device, (int*)null);
            _alc.MakeContextCurrent(context);
            _device = (nint)device;
            _context = (nint)context;

            for (int i = 0; i < SfxPoolSize; i++)
                _sfxPool[i] = _al.GenSource();
            _musicSource = _al.GenSource();

            IsAvailable = true;
        }
        catch
        {
            Cleanup();
        }
    }

    /// <summary>Pré-carrega um clip sem tocá-lo (evita hitch no primeiro Play).</summary>
    public AudioClip? Preload(string path)
        => IsAvailable ? LoadClip(path) : null;

    /// <summary>Toca um som em modo one-shot (SFX). Volume em 0..1, pitch em 0.1..4.</summary>
    public void Play(string path, float volume = 1f, float pitch = 1f)
    {
        if (!IsAvailable || LoadClip(path) is not { } clip)
            return;

        uint source = FindFreeSource();
        ConfigureSource(source, clip.BufferId, volume, pitch, loop: false);
        _al!.SourcePlay(source);
    }

    /// <summary>Toca uma trilha no canal de música (substitui a anterior). Loop ligado por padrão.</summary>
    public void PlayMusic(string path, bool loop = true, float volume = 1f)
    {
        if (!IsAvailable || LoadClip(path) is not { } clip)
            return;

        _al!.SourceStop(_musicSource);
        ConfigureSource(_musicSource, clip.BufferId, volume, pitch: 1f, loop);
        _al.SourcePlay(_musicSource);
    }

    public void StopMusic()
    {
        if (IsAvailable)
            _al!.SourceStop(_musicSource);
    }

    public void PauseMusic()
    {
        if (IsAvailable)
            _al!.SourcePause(_musicSource);
    }

    public void ResumeMusic()
    {
        if (IsAvailable)
            _al!.SourcePlay(_musicSource);
    }

    private AudioClip? LoadClip(string path)
    {
        if (_clips.TryGetValue(path, out var cached))
            return cached;

        try
        {
            using var stream = _source.Open(path);
            var clip = Path.GetExtension(path).ToLowerInvariant() == ".ogg"
                ? LoadOgg(stream)
                : LoadWav(stream);

            _clips[path] = clip;
            return clip;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Aurora.Audio] Falha ao carregar '{path}': {ex.Message}");
            return null;
        }
    }

    private AudioClip LoadWav(Stream stream)
    {
        var (samples, channels, sampleRate) = WavReader.Read(stream);
        var format = channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16;
        uint buffer = _al!.GenBuffer();
        _al.BufferData<short>(buffer, format, samples, sampleRate);
        return new AudioClip(buffer);
    }

    private AudioClip LoadOgg(Stream stream)
    {
        using var reader = new VorbisReader(stream);
        int channels = reader.Channels;
        int sampleRate = reader.SampleRate;

        var floatBuf = new float[channels * 4096];
        var samples = new List<short>();
        int read;
        while ((read = reader.ReadSamples(floatBuf, 0, floatBuf.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
                samples.Add((short)Math.Clamp((int)(floatBuf[i] * 32767f), short.MinValue, short.MaxValue));
        }

        var format = channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16;
        uint buffer = _al!.GenBuffer();
        _al.BufferData<short>(buffer, format, samples.ToArray(), sampleRate);
        return new AudioClip(buffer);
    }

    private void ConfigureSource(uint source, uint bufferId, float volume, float pitch, bool loop)
    {
        _al!.SetSourceProperty(source, SourceInteger.Buffer, (int)bufferId);
        _al.SetSourceProperty(source, SourceFloat.Gain, Math.Clamp(volume, 0f, 1f));
        _al.SetSourceProperty(source, SourceFloat.Pitch, Math.Clamp(pitch, 0.1f, 4f));
        _al.SetSourceProperty(source, SourceBoolean.Looping, loop);
    }

    private uint FindFreeSource()
    {
        foreach (var source in _sfxPool)
        {
            _al!.GetSourceProperty(source, GetSourceInteger.SourceState, out int state);
            if ((SourceState)state != SourceState.Playing)
                return source;
        }
        // Pool cheio: rouba a primeira fonte (a mais antiga).
        return _sfxPool[0];
    }

    private void Cleanup()
    {
        _alc?.Dispose();
        _al?.Dispose();
    }

    public unsafe void Dispose()
    {
        if (!IsAvailable)
        {
            Cleanup();
            return;
        }

        _al!.SourceStop(_musicSource);
        _al.DeleteSource(_musicSource);

        foreach (var s in _sfxPool)
            _al.DeleteSource(s);

        foreach (var clip in _clips.Values)
            _al.DeleteBuffer(clip.BufferId);
        _clips.Clear();

        _alc!.MakeContextCurrent((Context*)null);
        _alc.DestroyContext((Context*)_context);
        _alc.CloseDevice((Device*)_device);

        _al.Dispose();
        _alc.Dispose();
    }
}
