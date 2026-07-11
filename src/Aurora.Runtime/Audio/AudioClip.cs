namespace Aurora.Runtime.Audio;

/// <summary>Buffer de áudio carregado na memória da placa de som (OpenAL buffer).</summary>
public sealed class AudioClip
{
    internal uint BufferId { get; }
    internal AudioClip(uint bufferId) => BufferId = bufferId;
}
