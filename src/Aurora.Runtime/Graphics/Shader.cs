using System.Numerics;
using Silk.NET.OpenGL;

namespace Aurora.Runtime.Graphics;

/// <summary>Par de shaders (vertex + fragment) compilado e linkado em um programa OpenGL.</summary>
public sealed class Shader : IDisposable
{
    private readonly GL _gl;
    private readonly uint _handle;
    private readonly Dictionary<string, int> _uniformCache = new();

    public Shader(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;

        uint vertex = Compile(ShaderType.VertexShader, vertexSource);
        uint fragment = Compile(ShaderType.FragmentShader, fragmentSource);

        _handle = gl.CreateProgram();
        gl.AttachShader(_handle, vertex);
        gl.AttachShader(_handle, fragment);
        gl.LinkProgram(_handle);

        gl.GetProgram(_handle, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0)
            throw new InvalidOperationException($"Falha ao linkar shader: {gl.GetProgramInfoLog(_handle)}");

        gl.DetachShader(_handle, vertex);
        gl.DetachShader(_handle, fragment);
        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);
    }

    private uint Compile(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0)
            throw new InvalidOperationException($"Falha ao compilar {type}: {_gl.GetShaderInfoLog(shader)}");

        return shader;
    }

    public void Use() => _gl.UseProgram(_handle);

    private int Location(string name)
    {
        if (_uniformCache.TryGetValue(name, out int loc))
            return loc;

        loc = _gl.GetUniformLocation(_handle, name);
        _uniformCache[name] = loc;
        return loc;
    }

    public unsafe void SetMatrix(string name, Matrix4x4 value)
        => _gl.UniformMatrix4(Location(name), 1, false, (float*)&value);

    public void SetInt(string name, int value)
        => _gl.Uniform1(Location(name), value);

    public void Dispose() => _gl.DeleteProgram(_handle);
}
