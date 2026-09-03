using System.Numerics;
using Aurora.Runtime.Ecs;
using Aurora.Runtime.Ecs.Components;
using Aurora.Runtime.Graphics;
using Aurora.Runtime.Scenes;

namespace FruitNinja;

/// <summary>
/// O dedo do jogador: lê os toques, guarda o rastro e decide o que foi cortado.
///
/// <para>O corte é testado no SEGMENTO entre a posição do dedo no frame passado e a de agora,
/// não na posição atual isolada. É o detalhe que faz o jogo funcionar: a 60 FPS um gesto rápido
/// anda 200 px por frame, e quem só olha "onde o dedo está" erra toda fruta que passou no meio
/// do caminho.</para>
///
/// <para>Multi-toque de verdade: cada dedo tem o próprio traço e o próprio combo
/// (<c>InputManager.ActiveTouches</c> devolve todos). No desktop, a engine entrega o mouse como
/// um toque sintético de id -1, então o mesmo código roda no PC sem nenhum <c>if</c>.</para>
/// </summary>
[SceneScript]
public sealed class Lamina : Behavior
{
    private struct PontoDoRastro
    {
        public Vector2 Posicao;
        public float Idade;
    }

    private sealed class Traco
    {
        public readonly List<PontoDoRastro> Pontos = [];
        public Vector2 Ultima;
        public int FrutasNoGolpe;
        public Vector2 UltimoCorte;
    }

    /// <summary>Traços dos dedos encostados agora, por id de toque.</summary>
    private readonly Dictionary<int, Traco> _ativos = new();

    /// <summary>Traços de dedos que já soltaram: o golpe fechou, mas o rastro continua na tela
    /// até apagar. Sem esta lista o rastro sumiria no instante em que o dedo levanta.</summary>
    private readonly List<Traco> _apagando = [];

    /// <summary>Reaproveitada a cada teste de corte pra não alocar uma lista por frame.</summary>
    private readonly List<Fruta> _alvos = [];

    public override void Update(float deltaTime)
    {
        if (World?.Input is null || World.Camera is null)
            return;

        var def = Partida.Atual?.Lamina ?? new LaminaDef();

        // O rastro envelhece em tempo REAL mesmo com o poder Congelar ligado: quem está em
        // câmera lenta é a fruta, não a mão do jogador.
        Envelhecer(deltaTime, def.Duracao);

        var vistos = new HashSet<int>();

        foreach (var (id, tela) in World.Input.ActiveTouches)
        {
            vistos.Add(id);
            var mundo = World.Camera.ScreenToWorld(tela);

            if (!_ativos.TryGetValue(id, out var traco))
            {
                traco = new Traco { Ultima = mundo };
                _ativos[id] = traco;
                traco.Pontos.Add(new PontoDoRastro { Posicao = mundo });
                continue;
            }

            var passo = mundo - traco.Ultima;
            float distancia = passo.Length();

            if (distancia > 0.5f)
            {
                traco.Pontos.Add(new PontoDoRastro { Posicao = mundo });

                // Encostar parado não corta — e uma lâmina pesada exige o gesto mais rápido.
                if (deltaTime > 0f && distancia / deltaTime >= def.VelocidadeMinima)
                    Cortar(traco, traco.Ultima, mundo, passo / distancia, def);

                traco.Ultima = mundo;
            }
        }

        // Dedo que saiu da lista soltou a tela: fecha o golpe (é aqui que o combo é pago) e
        // manda o rastro pra fila de apagar.
        foreach (int id in _ativos.Keys.ToList())
        {
            if (vistos.Contains(id))
                continue;

            var traco = _ativos[id];
            _ativos.Remove(id);

            if (traco.FrutasNoGolpe > 0)
                Partida.Atual?.FecharGolpe(traco.FrutasNoGolpe, traco.UltimoCorte);

            _apagando.Add(traco);
        }
    }

    private void Envelhecer(float deltaTime, float duracao)
    {
        foreach (var traco in _ativos.Values)
            EnvelheceTraco(traco, deltaTime, duracao);

        for (int i = _apagando.Count - 1; i >= 0; i--)
        {
            EnvelheceTraco(_apagando[i], deltaTime, duracao);
            if (_apagando[i].Pontos.Count == 0)
                _apagando.RemoveAt(i);
        }
    }

    private static void EnvelheceTraco(Traco traco, float deltaTime, float duracao)
    {
        for (int i = 0; i < traco.Pontos.Count; i++)
        {
            var ponto = traco.Pontos[i];
            ponto.Idade += deltaTime;
            traco.Pontos[i] = ponto;
        }

        // Os pontos entram em ordem, então os velhos estão sempre na frente da lista.
        int vivos = 0;
        while (vivos < traco.Pontos.Count && traco.Pontos[vivos].Idade > duracao)
            vivos++;

        if (vivos > 0)
            traco.Pontos.RemoveRange(0, vivos);
    }

    private void Cortar(Traco traco, Vector2 de, Vector2 ate, Vector2 direcao, LaminaDef def)
    {
        if (World is null)
            return;

        // Materializa antes de cortar: Cortar() destrói a entidade e cria as metades, e mexer
        // no mundo no meio da varredura do Query invalidaria a iteração.
        _alvos.Clear();
        foreach (var (_, transform, fruta) in World.Query<Transform, Fruta>())
        {
            if (fruta.Cortada)
                continue;

            float raio = fruta.Raio * def.Alcance;
            if (DistanciaDoSegmento(de, ate, transform.Position) <= raio)
                _alvos.Add(fruta);
        }

        foreach (var fruta in _alvos)
        {
            if (fruta.Entity.Get<Transform>() is { } transform)
                traco.UltimoCorte = transform.Position;

            fruta.Cortar(direcao);

            // Bomba não entra na conta do combo: ela encerra a partida, e somar o "bônus" do
            // golpe que perdeu o jogo só confundiria a tela de fim.
            if (fruta.Def.Tipo != TipoDeFruta.Bomba)
                traco.FrutasNoGolpe++;
        }
    }

    /// <summary>Menor distância entre o ponto e o segmento AB — o teste de acerto do corte.</summary>
    private static float DistanciaDoSegmento(Vector2 a, Vector2 b, Vector2 ponto)
    {
        var ab = b - a;
        float comprimento = ab.LengthSquared();

        if (comprimento <= 0.0001f)
            return Vector2.Distance(a, ponto);

        float t = Math.Clamp(Vector2.Dot(ponto - a, ab) / comprimento, 0f, 1f);
        return Vector2.Distance(a + ab * t, ponto);
    }

    // ------------------------------------------------------------------ desenho

    /// <summary>
    /// Desenha o rastro. Chamado pelo <see cref="NinjaGame"/> no passe de mundo — um
    /// <c>Behavior</c> não tem gancho de render próprio, e o rastro precisa da mesma projeção
    /// das frutas pra cair em cima delas.
    /// </summary>
    public void Desenhar(SpriteBatch batch)
    {
        var def = Partida.Atual?.Lamina ?? new LaminaDef();
        var cor = Color.FromHex(def.Cor);
        var miolo = Color.FromHex(def.CorMiolo);

        foreach (var traco in _ativos.Values)
            DesenharTraco(batch, traco, def, cor, miolo);

        foreach (var traco in _apagando)
            DesenharTraco(batch, traco, def, cor, miolo);
    }

    private static void DesenharTraco(SpriteBatch batch, Traco traco, LaminaDef def,
        Color cor, Color miolo)
    {
        if (traco.Pontos.Count < 2)
            return;

        int total = traco.Pontos.Count;

        for (int i = 1; i < total; i++)
        {
            var a = traco.Pontos[i - 1].Posicao;
            var b = traco.Pontos[i].Posicao;

            var passo = b - a;
            float comprimento = passo.Length();
            if (comprimento <= 0.01f)
                continue;

            // Duas afinadas ao mesmo tempo: pela idade (o rabo apaga) e pela posição no traço
            // (a ponta é fina). Juntas dão a lâmina em forma de gota do original.
            float vida = 1f - Math.Clamp(traco.Pontos[i].Idade / MathF.Max(def.Duracao, 0.01f), 0f, 1f);
            float posicao = i / (float)(total - 1);
            float largura = def.Espessura * vida * (0.35f + 0.65f * posicao);

            if (largura < 0.6f)
                continue;

            float angulo = MathF.Atan2(passo.Y, passo.X);
            var origem = new Vector2(0f, 0.5f);

            batch.Draw(batch.WhitePixel, a, new Vector2(comprimento + largura * 0.5f, largura),
                origem, angulo, cor.WithAlpha(cor.A * vida));

            batch.Draw(batch.WhitePixel, a,
                new Vector2(comprimento + largura * 0.2f, largura * 0.34f),
                origem, angulo, miolo.WithAlpha(miolo.A * vida));
        }
    }
}
