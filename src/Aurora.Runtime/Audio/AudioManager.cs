using Aurora.Runtime.Assets;
using NVorbis;
using Silk.NET.OpenAL;

namespace Aurora.Runtime.Audio;

/// <summary>
/// Gerencia contexto OpenAL, pool de fontes e cache de clips.
/// Inicializa graciosamente se o dispositivo de áudio não estiver disponível.
/// <para>SFX são decodificados inteiros e cacheados (são curtos e tocam repetido). Música em
/// OGG é decodificada aos poucos, em streaming — ver <see cref="PlayMusic"/>.</para>
/// </summary>
public sealed class AudioManager : IDisposable
{
    private const int SfxPoolSize = 16;

    /// <summary>Buffers na fila de streaming. Quatro dão folga suficiente pra um hitch de
    /// frame não esvaziar a fila antes do próximo <see cref="Update"/>.</summary>
    private const int MusicBufferCount = 4;

    /// <summary>Amostras (contando canais) por buffer de streaming: ~0,19 s em 44,1 kHz
    /// estéreo. Os quatro juntos somam ~140 KB de PCM, contra a faixa inteira em memória.</summary>
    private const int MusicBufferSamples = 16384;

    private readonly IAssetSource _source;
    private readonly AL? _al;
    private readonly ALContext? _alc;
    private readonly Dictionary<string, AudioClip> _clips = new(StringComparer.OrdinalIgnoreCase);
    private readonly uint[] _sfxPool = new uint[SfxPoolSize];
    private uint _musicSource;
    private nint _device;
    private nint _context;

    private float _masterVolume = 1f;
    private float _musicVolume = 1f;
    private float _sfxVolume = 1f;

    // ---- Estado do canal de música ----

    private readonly uint[] _musicBuffers = new uint[MusicBufferCount];
    private bool _musicBuffersCreated;

    private VorbisReader? _musicReader;
    private VorbisPcmSource? _musicDecoder;

    /// <summary>Bytes COMPRIMIDOS do ogg. O stream do <see cref="IAssetSource"/> não é
    /// necessariamente seekável (asset dentro do APK no Android não é) e o decodificador
    /// precisa voltar ao início pra dar loop — então o arquivo comprimido é copiado uma vez
    /// pra memória e a decodificação corre por cima dessa cópia. Uma faixa de 3 min ocupa
    /// alguns MB aqui, contra ~30 MB se fosse guardada em PCM.</summary>
    private MemoryStream? _musicBytes;

    private float[]? _musicFloatBuffer;
    private short[]? _musicPcmBuffer;
    private BufferFormat _musicFormat;
    private int _musicSampleRate;
    private bool _musicLooping;
    private bool _musicPaused;
    private bool _musicStreaming;

    /// <summary>Volume pedido na chamada de <see cref="PlayMusic"/>, antes do barramento —
    /// guardado pra poder reaplicar o ganho quando <see cref="MusicVolume"/> mudar no meio
    /// da faixa (menu de opções com a música tocando atrás).</summary>
    private float _musicRequestedVolume = 1f;

    /// <summary>False quando nenhum dispositivo de áudio foi encontrado. Todas as chamadas são no-op.</summary>
    public bool IsAvailable { get; }

    /// <summary>Volume geral (0..1), multiplica música e SFX — é o ganho do listener OpenAL.</summary>
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

    /// <summary>Barramento de música (0..1). Aplica na hora, inclusive na faixa que já está
    /// tocando.</summary>
    public float MusicVolume
    {
        get => _musicVolume;
        set
        {
            _musicVolume = Math.Clamp(value, 0f, 1f);
            if (IsAvailable)
                ApplyMusicGain();
        }
    }

    /// <summary>Barramento de efeitos (0..1). Vale a partir do próximo <see cref="Play"/> —
    /// os SFX já tocando terminam com o ganho que tinham (duram frações de segundo).</summary>
    public float SfxVolume
    {
        get => _sfxVolume;
        set => _sfxVolume = Math.Clamp(value, 0f, 1f);
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

    /// <summary>Toca um som em modo one-shot (SFX). Volume em 0..1 (multiplicado por
    /// <see cref="SfxVolume"/>), pitch em 0.1..4.</summary>
    public void Play(string path, float volume = 1f, float pitch = 1f)
    {
        if (!IsAvailable || LoadClip(path) is not { } clip)
            return;

        uint source = FindFreeSource();
        ConfigureSource(source, clip.BufferId, volume * _sfxVolume, pitch, loop: false);
        _al!.SourcePlay(source);
    }

    /// <summary>
    /// Toca uma trilha no canal de música (substitui a anterior). Loop ligado por padrão,
    /// volume multiplicado por <see cref="MusicVolume"/>.
    /// <para>Arquivos <c>.ogg</c> tocam em streaming: só alguns décimos de segundo ficam
    /// decodificados por vez, e <see cref="Update"/> precisa ser chamado a cada frame pra
    /// realimentar a fila (o <see cref="Game"/> já faz isso). WAV continua sendo carregado
    /// inteiro — o formato não comprime, então música longa em WAV é cara de qualquer jeito.</para>
    /// </summary>
    public void PlayMusic(string path, bool loop = true, float volume = 1f)
    {
        if (!IsAvailable)
            return;

        StopMusic();

        _musicRequestedVolume = Math.Clamp(volume, 0f, 1f);
        _musicLooping = loop;
        _musicPaused = false;

        if (Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            if (StartMusicStream(path))
                return;

            // Falhou abrir/decodificar: StartMusicStream já logou e limpou. Não cai pro
            // caminho estático porque ele leria o mesmo arquivo quebrado.
            return;
        }

        if (LoadClip(path) is not { } clip)
            return;

        ConfigureSource(_musicSource, clip.BufferId, _musicRequestedVolume * _musicVolume, pitch: 1f, _musicLooping);
        _al!.SourcePlay(_musicSource);
    }

    /// <summary>Para a música e libera o que estava aberto pro streaming.</summary>
    public void StopMusic()
    {
        if (!IsAvailable)
            return;

        _al!.SourceStop(_musicSource);
        _musicPaused = false;

        if (_musicStreaming)
            TeardownMusicStream();

        // Solta o buffer estático (WAV) pra fonte poder ser reusada em streaming.
        _al.SetSourceProperty(_musicSource, SourceInteger.Buffer, 0);
    }

    public void PauseMusic()
    {
        if (!IsAvailable)
            return;

        _musicPaused = true;
        _al!.SourcePause(_musicSource);
    }

    public void ResumeMusic()
    {
        if (!IsAvailable)
            return;

        _musicPaused = false;
        _al!.SourcePlay(_musicSource);
    }

    /// <summary>
    /// Realimenta a fila de streaming da música. Chamado pelo <see cref="Game"/> a cada frame;
    /// no-op quando não há música em streaming tocando.
    /// </summary>
    public unsafe void Update()
    {
        if (!IsAvailable || !_musicStreaming || _musicPaused)
            return;

        _al!.GetSourceProperty(_musicSource, GetSourceInteger.BuffersProcessed, out int processed);

        while (processed-- > 0)
        {
            uint buffer;
            _al.SourceUnqueueBuffers(_musicSource, 1, &buffer);

            // Buffer que não enche = fim da faixa sem loop: fica fora da fila e a música
            // termina naturalmente quando o que já está enfileirado acabar de tocar.
            if (FillMusicBuffer(buffer))
                _al.SourceQueueBuffers(_musicSource, 1, &buffer);
        }

        _al.GetSourceProperty(_musicSource, GetSourceInteger.SourceState, out int state);
        if ((SourceState)state == SourceState.Playing)
            return;

        // Parou sozinha: ou a fila secou num hitch de I/O (ainda há buffer enfileirado —
        // retoma) ou a faixa acabou de verdade (libera tudo).
        _al.GetSourceProperty(_musicSource, GetSourceInteger.BuffersQueued, out int queued);
        if (queued > 0)
            _al.SourcePlay(_musicSource);
        else
            StopMusic();
    }

    // ---- Streaming ----

    private unsafe bool StartMusicStream(string path)
    {
        try
        {
            _musicBytes = new MemoryStream();
            using (var stream = _source.Open(path))
                stream.CopyTo(_musicBytes);
            _musicBytes.Position = 0;

            _musicReader = new VorbisReader(_musicBytes);
            _musicDecoder = new VorbisPcmSource(_musicReader);
            _musicSampleRate = _musicReader.SampleRate;
            _musicFormat = _musicReader.Channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16;

            _musicFloatBuffer ??= new float[MusicBufferSamples];
            _musicPcmBuffer ??= new short[MusicBufferSamples];

            if (!_musicBuffersCreated)
            {
                for (int i = 0; i < MusicBufferCount; i++)
                    _musicBuffers[i] = _al!.GenBuffer();
                _musicBuffersCreated = true;
            }

            _al!.SetSourceProperty(_musicSource, SourceInteger.Buffer, 0);
            // Looping do OpenAL repetiria só o que está na fila; o loop da faixa é feito
            // rebobinando o decodificador em FillMusicBuffer.
            _al.SetSourceProperty(_musicSource, SourceBoolean.Looping, false);
            _al.SetSourceProperty(_musicSource, SourceFloat.Pitch, 1f);
            ApplyMusicGain();

            int enfileirados = 0;
            for (int i = 0; i < MusicBufferCount; i++)
            {
                uint buffer = _musicBuffers[i];
                if (!FillMusicBuffer(buffer))
                    break;

                _al.SourceQueueBuffers(_musicSource, 1, &buffer);
                enfileirados++;
            }

            if (enfileirados == 0)
            {
                TeardownMusicStream();
                return false;
            }

            _musicStreaming = true;
            _al.SourcePlay(_musicSource);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Aurora.Audio] Falha ao abrir streaming de '{path}': {ex.Message}");
            TeardownMusicStream();
            return false;
        }
    }

    /// <summary>Decodifica o próximo pedaço pro buffer. False quando não veio amostra
    /// nenhuma — fim da faixa sem loop, ou arquivo vazio.</summary>
    private unsafe bool FillMusicBuffer(uint buffer)
    {
        if (_musicDecoder is null || _musicFloatBuffer is null || _musicPcmBuffer is null)
            return false;

        int total = PcmFiller.Fill(_musicDecoder, _musicFloatBuffer, _musicPcmBuffer, _musicLooping);
        if (total == 0)
            return false;

        // Overload de ponteiro em vez do genérico: o array é reusado entre refills e só a
        // primeira parte dele está preenchida, então o tamanho vai explícito em bytes —
        // recortar num array novo do tamanho certo alocaria a cada buffer, várias vezes por
        // segundo, durante a música inteira.
        fixed (short* pcm = _musicPcmBuffer)
            _al!.BufferData(buffer, _musicFormat, pcm, total * sizeof(short), _musicSampleRate);

        return true;
    }

    /// <summary>Adapta o decodificador do NVorbis pra interface que o <see cref="PcmFiller"/> usa.</summary>
    private sealed class VorbisPcmSource : IPcmSource
    {
        private readonly VorbisReader _reader;

        public VorbisPcmSource(VorbisReader reader) => _reader = reader;

        public int Read(float[] buffer, int offset, int count) => _reader.ReadSamples(buffer, offset, count);

        public void Rewind() => _reader.SeekTo(TimeSpan.Zero);
    }

    private unsafe void TeardownMusicStream()
    {
        if (IsAvailable)
        {
            // Desenfileira o que sobrou; buffer ainda na fila não pode ser reescrito nem
            // apagado, e esses quatro são reaproveitados na próxima faixa.
            _al!.GetSourceProperty(_musicSource, GetSourceInteger.BuffersQueued, out int queued);
            while (queued-- > 0)
            {
                uint buffer;
                _al.SourceUnqueueBuffers(_musicSource, 1, &buffer);
            }
        }

        _musicReader?.Dispose();
        _musicReader = null;
        _musicDecoder = null;
        _musicBytes?.Dispose();
        _musicBytes = null;
        _musicStreaming = false;
    }

    private void ApplyMusicGain()
        => _al!.SetSourceProperty(_musicSource, SourceFloat.Gain,
            Math.Clamp(_musicRequestedVolume * _musicVolume, 0f, 1f));

    // ---- Carregamento estático (SFX e música em WAV) ----

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
                samples.Add(PcmFiller.ToPcm16(floatBuf[i]));
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

        StopMusic();
        _al!.DeleteSource(_musicSource);

        if (_musicBuffersCreated)
        {
            foreach (var buffer in _musicBuffers)
                _al.DeleteBuffer(buffer);
            _musicBuffersCreated = false;
        }

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
