using System.Numerics;
using Aurora.Runtime.Graphics;

namespace Bichinhos;

/// <summary>
/// Dá vida ao sprite parado: respira (estica e encolhe), pisca, pula, mastiga, treme. Um por
/// bicho na tela; cada tela avisa o que aconteceu (<see cref="Pular"/>, <see cref="Mastigar"/>)
/// e chama <see cref="Desenhar"/> com o estado do momento.
///
/// <para>Dormir e piscar fecham os olhos desenhando uma pálpebra da cor da pele por cima do olho
/// do sprite — posição e cor vêm do <c>especies.json</c>. Assim um sprite serve pra acordado,
/// piscando e dormindo.</para>
/// </summary>
public sealed class BichoVisual
{
    private float _tempo;
    private float _pulo = -1f;
    private float _alturaPulo = 60f;
    private float _mastigar;
    private float _tremer;
    private float _proximaPiscada = 2f;
    private float _piscando;
    private float _flash;

    /// <summary>Deslocamento extra (investida na batalha, andar pela casa).</summary>
    public Vector2 Deslocamento { get; set; }
    public bool Espelhar { get; set; }

    /// <summary>0..1: some na tela de desmaio / aparece na evolução.</summary>
    public float Opacidade { get; set; } = 1f;

    public bool NoAr => _pulo >= 0f;

    public void Pular(float altura = 60f)
    {
        _pulo = 0f;
        _alturaPulo = altura;
    }

    public void Mastigar(float segundos = 1.2f) => _mastigar = segundos;
    public void Tremer(float segundos = 0.4f) => _tremer = segundos;
    public void Piscar() => _piscando = 0.14f;

    /// <summary>Pisca sumindo e voltando (levou dano).</summary>
    public void Flash(float segundos = 0.5f) => _flash = segundos;

    public void Atualizar(float dt)
    {
        _tempo += dt;
        _mastigar = MathF.Max(0f, _mastigar - dt);
        _tremer = MathF.Max(0f, _tremer - dt);
        _piscando = MathF.Max(0f, _piscando - dt);
        _flash = MathF.Max(0f, _flash - dt);

        if (_pulo >= 0f)
        {
            _pulo += dt;
            if (_pulo > 0.45f)
                _pulo = -1f;
        }

        _proximaPiscada -= dt;
        if (_proximaPiscada <= 0f)
        {
            Piscar();
            _proximaPiscada = 2.2f + (MathF.Sin(_tempo * 7.3f) * 0.5f + 0.5f) * 2.5f;
        }
    }

    /// <param name="pe">Onde o pé encosta no chão.</param>
    /// <param name="lado">Lado do sprite (o sprite é quadrado).</param>
    public void Desenhar(Tinta tinta, Especie especie, int estagio, Vector2 pe, float lado,
        bool dormindo = false, bool doente = false, bool triste = false, Color? tom = null)
    {
        if (Opacidade <= 0.001f)
            return;

        // Respiração: mais lenta dormindo. Mastigar é um "nhac" rápido e fundo.
        float respira = dormindo ? MathF.Sin(_tempo * 1.6f) * 0.035f : MathF.Sin(_tempo * 3.2f) * 0.025f;
        if (_mastigar > 0f)
            respira = MathF.Abs(MathF.Sin(_mastigar * 14f)) * -0.08f;

        float alturaPulo = 0f;
        if (_pulo >= 0f)
        {
            float t = _pulo / 0.45f;
            alturaPulo = 4f * t * (1f - t) * _alturaPulo;
            respira += t < 0.15f ? -0.08f : 0.05f;   // agacha antes, estica no ar
        }

        var escala = new Vector2(1f - respira * 0.8f, 1f + respira);
        if (dormindo)
            escala.Y *= 0.94f;

        var pos = pe + Deslocamento - new Vector2(0f, alturaPulo);
        if (_tremer > 0f)
            pos.X += MathF.Sin(_tempo * 70f) * 6f * (_tremer / 0.4f);
        if (doente)
            pos.X += MathF.Sin(_tempo * 2.2f) * 3f;

        // Sombra no chão encolhe quando ele sobe.
        float sombra = 1f - Math.Clamp(alturaPulo / 200f, 0f, 0.6f);
        tinta.Elipse(pe + new Vector2(Deslocamento.X, 0f), new Vector2(lado * 0.26f * sombra, lado * 0.035f * sombra),
            Color.FromHex("#00000030").WithAlpha(0.19f * Opacidade));

        var cor = tom ?? Color.White;
        if (doente) cor = new Color(cor.R * 0.82f, cor.G, cor.B * 0.78f, cor.A);
        if (triste) cor = new Color(cor.R * 0.9f, cor.G * 0.92f, cor.B, cor.A);
        cor = cor.WithAlpha(cor.A * Opacidade);

        // Piscar de dano do Pokémon: o sprite some e volta algumas vezes.
        if (_flash > 0f && (int)(_flash / 0.06f) % 2 == 1)
            return;

        var tamanho = new Vector2(lado * escala.X, lado * escala.Y);
        var textura = tinta.Textura(especie.Sprite(estagio));
        tinta.Sprite(textura, pos, tamanho, new Vector2(0.5f, 0.9f), cor, 0f, Espelhar);

        if (dormindo || _piscando > 0f)
            FecharOlhos(tinta, especie.Estagios[estagio], pos, tamanho);

        if (triste && !dormindo)
            Lagrima(tinta, especie.Estagios[estagio], pos, tamanho);
    }

    private void FecharOlhos(Tinta tinta, Estagio estagio, Vector2 pos, Vector2 tamanho)
    {
        var o = estagio.Olhos;
        var pele = Color.FromHex(estagio.Pele).WithAlpha(Opacidade);
        var traco = Tinta.Tinteiro.WithAlpha(Opacidade);

        for (int lado = -1; lado <= 1; lado += 2)
        {
            float ox = (o[0] + lado * o[2]) / 100f;
            if (Espelhar)
                ox = 1f - ox;

            var centro = pos + new Vector2((ox - 0.5f) * tamanho.X, (o[1] / 100f - 0.9f) * tamanho.Y);
            var raio = new Vector2(o[3] * 0.82f * 1.35f / 100f * tamanho.X, o[3] * 1.28f / 100f * tamanho.Y);
            tinta.Elipse(centro, raio, pele);
            // Olho fechado = arco virado pra baixo; um traço achatado dá conta nesse tamanho.
            tinta.Elipse(centro + new Vector2(0f, raio.Y * 0.2f), new Vector2(raio.X * 0.85f, MathF.Max(2f, raio.Y * 0.2f)), traco);
        }
    }

    private void Lagrima(Tinta tinta, Estagio estagio, Vector2 pos, Vector2 tamanho)
    {
        var o = estagio.Olhos;
        float ciclo = _tempo * 0.9f % 1f;
        float ox = (o[0] + o[2] + o[3]) / 100f;
        if (Espelhar)
            ox = 1f - ox;
        var centro = pos + new Vector2((ox - 0.5f) * tamanho.X, (o[1] / 100f - 0.9f) * tamanho.Y + ciclo * 30f);
        tinta.Icone("gota", centro, tamanho.X * 0.08f, Color.White.WithAlpha((1f - ciclo) * Opacidade));
    }
}
