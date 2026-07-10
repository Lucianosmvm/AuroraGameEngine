using System.Numerics;
using Silk.NET.OpenGL;

namespace Aurora.Runtime.Graphics;

/// <summary>
/// Agrupa sprites em lotes por textura para minimizar draw calls.
/// Uso: Begin(viewProjection) → Draw(...) N vezes → End().
/// </summary>
public sealed class SpriteBatch : IDisposable
{
    private const int MaxQuads = 2048;
    private const int FloatsPerVertex = 8; // pos(2) + uv(2) + cor(4)
    private const int FloatsPerQuad = FloatsPerVertex * 4;

    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _ebo;
    private readonly float[] _vertices = new float[MaxQuads * FloatsPerQuad];

    private int _quadCount;
    private Texture2D? _currentTexture;
    private Matrix4x4 _viewProjection = Matrix4x4.Identity;
    private bool _begun;
    private int _drawCalls;

    /// <summary>Draw calls emitidas no último frame (diagnóstico de batching).</summary>
    public int DrawCallsLastFrame { get; private set; }

    /// <param name="gles">True quando o contexto é OpenGL ES (Android) — troca o dialeto GLSL.</param>
    public unsafe SpriteBatch(GL gl, bool gles = false)
    {
        _gl = gl;
        _shader = new Shader(gl, BuildSource(VertexBody, gles, fragment: false),
            BuildSource(FragmentBody, gles, fragment: true));

        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        _ebo = gl.GenBuffer();

        gl.BindVertexArray(_vao);

        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_vertices.Length * sizeof(float)), null, BufferUsageARB.DynamicDraw);

        // Índices fixos: dois triângulos por quad (0-1-2, 2-3-0).
        var indices = new ushort[MaxQuads * 6];
        for (int i = 0; i < MaxQuads; i++)
        {
            int v = i * 4;
            int x = i * 6;
            indices[x + 0] = (ushort)(v + 0);
            indices[x + 1] = (ushort)(v + 1);
            indices[x + 2] = (ushort)(v + 2);
            indices[x + 3] = (ushort)(v + 2);
            indices[x + 4] = (ushort)(v + 3);
            indices[x + 5] = (ushort)(v + 0);
        }

        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        fixed (ushort* ptr = indices)
        {
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(ushort)), ptr, BufferUsageARB.StaticDraw);
        }

        uint stride = FloatsPerVertex * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(4 * sizeof(float)));

        gl.BindVertexArray(0);
    }

    public void Begin(Matrix4x4 viewProjection)
    {
        if (_begun)
            throw new InvalidOperationException("Begin() chamado duas vezes sem End().");

        _viewProjection = viewProjection;
        _begun = true;
    }

    /// <param name="origin">Pivô normalizado (0,0 = canto superior esquerdo; 0.5,0.5 = centro).</param>
    /// <param name="rotation">Rotação em radianos ao redor do pivô.</param>
    public void Draw(Texture2D texture, Vector2 position, Vector2 size, Vector2 origin,
        float rotation, Color color, bool flipX = false, bool flipY = false)
        => Draw(texture, position, size, origin, rotation, color,
            new RectF(0f, 0f, texture.Width, texture.Height), flipX, flipY);

    /// <summary>Desenha um recorte da textura (tile de tileset, frame de atlas).</summary>
    public void Draw(Texture2D texture, Vector2 position, Vector2 size, Vector2 origin,
        float rotation, Color color, in RectF source, bool flipX = false, bool flipY = false)
    {
        if (!_begun)
            throw new InvalidOperationException("Chame Begin() antes de Draw().");

        if (!ReferenceEquals(_currentTexture, texture) && _currentTexture is not null)
            Flush();
        if (_quadCount == MaxQuads)
            Flush();

        _currentTexture = texture;

        float u0 = source.X / texture.Width;
        float v0 = source.Y / texture.Height;
        float u1 = (source.X + source.Width) / texture.Width;
        float v1 = (source.Y + source.Height) / texture.Height;

        if (flipX)
            (u0, u1) = (u1, u0);
        if (flipY)
            (v0, v1) = (v1, v0);

        var originPx = size * origin;
        float cos = MathF.Cos(rotation);
        float sin = MathF.Sin(rotation);

        Span<Vector2> corners = stackalloc Vector2[4]
        {
            new(-originPx.X, -originPx.Y),
            new(size.X - originPx.X, -originPx.Y),
            new(size.X - originPx.X, size.Y - originPx.Y),
            new(-originPx.X, size.Y - originPx.Y),
        };
        Span<Vector2> uvs = stackalloc Vector2[4]
        {
            new(u0, v0),
            new(u1, v0),
            new(u1, v1),
            new(u0, v1),
        };

        int o = _quadCount * FloatsPerQuad;
        for (int i = 0; i < 4; i++)
        {
            _vertices[o++] = corners[i].X * cos - corners[i].Y * sin + position.X;
            _vertices[o++] = corners[i].X * sin + corners[i].Y * cos + position.Y;
            _vertices[o++] = uvs[i].X;
            _vertices[o++] = uvs[i].Y;
            _vertices[o++] = color.R;
            _vertices[o++] = color.G;
            _vertices[o++] = color.B;
            _vertices[o++] = color.A;
        }

        _quadCount++;
    }

    /// <summary>Atalho: desenha no tamanho natural da textura, sem rotação.</summary>
    public void Draw(Texture2D texture, Vector2 position)
        => Draw(texture, position, new Vector2(texture.Width, texture.Height), Vector2.Zero, 0f, Color.White);

    public void End()
    {
        if (!_begun)
            throw new InvalidOperationException("End() chamado sem Begin().");

        Flush();
        _begun = false;
        DrawCallsLastFrame = _drawCalls;
        _drawCalls = 0;
    }

    private unsafe void Flush()
    {
        if (_quadCount == 0 || _currentTexture is null)
            return;

        _shader.Use();
        _shader.SetMatrix("uViewProj", _viewProjection);
        _shader.SetInt("uTexture", 0);
        _currentTexture.Bind(0);

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* ptr = _vertices)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(_quadCount * FloatsPerQuad * sizeof(float)), ptr);
        }

        _gl.DrawElements(PrimitiveType.Triangles, (uint)(_quadCount * 6), DrawElementsType.UnsignedShort, (void*)0);

        _quadCount = 0;
        _currentTexture = null;
        _drawCalls++;
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _shader.Dispose();
    }

    // Mesmo corpo GLSL para desktop (330 core) e GLES (300 es); só o cabeçalho muda.
    // GLES exige qualificador de precisão no fragment shader.
    private static string BuildSource(string body, bool gles, bool fragment)
    {
        string header = gles
            ? fragment ? "#version 300 es\nprecision mediump float;\n" : "#version 300 es\n"
            : "#version 330 core\n";
        return header + body;
    }

    // System.Numerics é row-major (v' = v * M). Upload sem transpose faz o GLSL enxergar
    // a matriz transposta, então o produto correto no shader é "matriz * vetor".
    // "vetor * matriz" aqui vaza a translação para o componente w → distorção de
    // perspectiva que cresce conforme a câmera se afasta da origem.
    private const string VertexBody = """
        layout(location = 0) in vec2 aPos;
        layout(location = 1) in vec2 aUv;
        layout(location = 2) in vec4 aColor;

        uniform mat4 uViewProj;

        out vec2 vUv;
        out vec4 vColor;

        void main()
        {
            gl_Position = uViewProj * vec4(aPos, 0.0, 1.0);
            vUv = aUv;
            vColor = aColor;
        }
        """;

    private const string FragmentBody = """
        in vec2 vUv;
        in vec4 vColor;

        uniform sampler2D uTexture;

        out vec4 FragColor;

        void main()
        {
            FragColor = texture(uTexture, vUv) * vColor;
        }
        """;
}
