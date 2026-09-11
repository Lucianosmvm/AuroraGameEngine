using System.Numerics;
using Aurora.Runtime.Graphics;
using BeastArena.Sim;

namespace BeastArena.Render;

/// <summary>Carta sendo arrastada (ou selecionada) em cima da arena: onde cairia e se pode.</summary>
public readonly record struct PreviaDeJogada(CartaDef Carta, Vector2 Tile, bool Valida);

/// <summary>
/// Desenha a <see cref="Batalha"/> no passe de mundo. Só LÊ a simulação — nada aqui muda o
/// resultado da partida, o que permite trocar este arquivo inteiro por sprites e animação sem
/// tocar numa regra.
///
/// <para>Não usa entidades do <c>World</c> de propósito: a simulação já é a fonte da verdade, e
/// espelhar cada unidade numa entidade seria manter dois estados sincronizados à mão.</para>
/// </summary>
public sealed class VisaoArena
{
    /// <summary>Pixels de mundo por tile. 18 tiles x 40 = os 720 de largura da tela.</summary>
    public const float Tile = 40f;

    /// <summary>Sobe a arena na tela pra sobrar a faixa de baixo pra mão de cartas. Com a câmera
    /// em (0,0), a arena vai de Y -620 a 340 no mundo = 20 a 980 na tela.</summary>
    public const float DeslocamentoY = -140f;

    /// <summary>Criatura desenhada maior que o corpo de colisão. No tamanho físico, um lobo tem
    /// 36 px — ilegível num celular. Só o desenho cresce; a regra continua usando o raio da ficha.</summary>
    public const float EscalaVisual = 1.35f;

    private sealed class Texto
    {
        public string Conteudo = "";
        public Vector2 Posicao;
        public Color Cor;
        public float Escala;
        public float Idade;
        public float Duracao;
    }

    private sealed class Onda
    {
        public Vector2 Centro;
        public float Raio;
        public Color Cor;
        public float Idade;
        public float Duracao;
    }

    private static readonly Color Ouro = Color.FromHex("#FFD54FFF");
    private static readonly Color CorMana = Color.FromHex("#7CC6FFFF");
    private static readonly Color Neutro = Color.FromHex("#A3A896FF");
    private static readonly Color Sombra = Color.FromHex("#00000048");

    private static readonly Color[] Musgo =
    [
        Color.FromHex("#34402FFF"),
        Color.FromHex("#37442FFF"),
        Color.FromHex("#313C2CFF"),
    ];

    private readonly CatalogoCartas _catalogo;
    private readonly List<Texto> _textos = [];
    private readonly List<Onda> _ondas = [];

    public VisaoArena(CatalogoCartas catalogo) => _catalogo = catalogo;

    public static Vector2 ParaMundo(Vector2 tile)
        => new((tile.X - Campo.Largura / 2f) * Tile, (tile.Y - Campo.CentroY) * Tile + DeslocamentoY);

    public static Vector2 ParaTile(Vector2 mundo)
        => new(mundo.X / Tile + Campo.Largura / 2f, (mundo.Y - DeslocamentoY) / Tile + Campo.CentroY);

    public static Color CorDaEquipe(Equipe equipe)
        => equipe == Equipe.Jogador ? Color.FromHex("#3DD6B5FF") : Color.FromHex("#E8609EFF");

    public void Limpar()
    {
        _textos.Clear();
        _ondas.Clear();
    }

    // ------------------------------------------------------------------ eventos → efeitos

    public void Consumir(List<EventoDeBatalha> eventos)
    {
        foreach (var evento in eventos)
        {
            switch (evento.Tipo)
            {
                case TipoDeEvento.Evoluiu:
                    Escrever($"{evento.Texto}!", evento.Posicao, Ouro, 0.9f, 1.5f);
                    Ondular(evento.Posicao, 1.4f, Ouro, 0.6f);
                    break;

                case TipoDeEvento.Recompensa:
                    Escrever(evento.Texto, evento.Posicao + new Vector2(0f, -0.7f), CorMana, 0.8f, 1.3f);
                    break;

                case TipoDeEvento.Capturou:
                    Escrever(evento.Texto, evento.Posicao, CorDaEquipe(evento.Equipe), 1.0f, 1.8f);
                    Ondular(evento.Posicao, 2.8f, CorDaEquipe(evento.Equipe), 0.7f);
                    break;

                case TipoDeEvento.Neutralizou:
                    Escrever(evento.Texto, evento.Posicao + new Vector2(0f, 0.8f), Color.FromHex("#E6E2D3FF"), 0.75f, 1.4f);
                    Ondular(evento.Posicao, 1.8f, Color.FromHex("#FFFFFFAA"), 0.4f);
                    break;

                case TipoDeEvento.FeiticoCaiu:
                    var cor = _catalogo.Todas.FirstOrDefault(c => c.Id == evento.Texto)?.Cor ?? "#FFFFFFFF";
                    Ondular(evento.Posicao, evento.Raio, Color.FromHex(cor), 0.45f);
                    break;

                case TipoDeEvento.Morreu:
                    Ondular(evento.Posicao, 0.7f, Color.FromHex("#FFFFFFAA"), 0.25f);
                    break;
            }
        }
    }

    public void Atualizar(float deltaTime)
    {
        foreach (var texto in _textos)
            texto.Idade += deltaTime;
        foreach (var onda in _ondas)
            onda.Idade += deltaTime;

        _textos.RemoveAll(t => t.Idade >= t.Duracao);
        _ondas.RemoveAll(o => o.Idade >= o.Duracao);
    }

    private void Escrever(string conteudo, Vector2 tile, Color cor, float escala, float duracao)
        => _textos.Add(new Texto { Conteudo = conteudo, Posicao = tile, Cor = cor, Escala = escala, Duracao = duracao });

    private void Ondular(Vector2 tile, float raio, Color cor, float duracao)
        => _ondas.Add(new Onda { Centro = tile, Raio = raio, Cor = cor, Duracao = duracao });

    // ------------------------------------------------------------------ desenho

    /// <param name="alfa">Fração (0..1) do caminho entre o último passo e o próximo — interpola
    /// posições pra 30 passos/s parecerem suaves a 60 FPS.</param>
    public void Desenhar(SpriteBatch batch, Font fonte, Formas formas, Batalha batalha, float alfa, PreviaDeJogada? previa)
    {
        DesenharChao(batch, formas, batalha, previa);
        DesenharNinhos(batch, formas, batalha);
        DesenharSantuarios(batch, formas, batalha);
        DesenharPedras(batch, formas);

        foreach (var unidade in batalha.Unidades.Where(u => !u.Voa).OrderBy(u => u.Posicao.Y))
            DesenharUnidade(batch, fonte, formas, batalha, unidade, alfa);

        foreach (var unidade in batalha.Unidades.Where(u => u.Voa).OrderBy(u => u.Posicao.Y))
            DesenharUnidade(batch, fonte, formas, batalha, unidade, alfa);

        DesenharProjeteis(batch, formas, batalha, alfa);
        DesenharFeiticos(batch, formas, batalha);
        DesenharOndas(batch, formas);

        if (previa is { } p)
            DesenharPrevia(batch, formas, p);

        DesenharTextos(batch, fonte);
    }

    private static void DesenharChao(SpriteBatch batch, Formas formas, Batalha batalha, PreviaDeJogada? previa)
    {
        bool mostrarArea = previa is { Carta.Tipo: TipoDeCarta.Criatura };
        var tufo = Color.FromHex("#28321FFF");
        var foraDaArea = Color.FromHex("#0A0D0999");

        for (int y = 0; y < (int)Campo.Altura; y++)
        {
            for (int x = 0; x < (int)Campo.Largura; x++)
            {
                var canto = ParaMundo(new Vector2(x, y));
                int ruido = Ruido(x, y);
                batch.DrawRect(canto, new Vector2(Tile), Musgo[ruido % Musgo.Length]);

                // Tufos espalhados por hash, não por sorteio: o chão fica igual a cada frame.
                if (ruido % 5 == 0)
                {
                    var base_ = canto + new Vector2(8f + ruido % 20, 12f + ruido / 7 % 16);
                    batch.DrawRect(base_, new Vector2(3f, 9f), tufo);
                    batch.DrawRect(base_ + new Vector2(5f, -3f), new Vector2(3f, 12f), tufo);
                }

                // Enquanto arrasta criatura, apaga onde NÃO pode: o jogador vê a área crescer
                // em volta de cada santuário que domina.
                if (mostrarArea && !batalha.AreaDeImplantacao(Equipe.Jogador, new Vector2(x + 0.5f, y + 0.5f)))
                    batch.DrawRect(canto, new Vector2(Tile), foraDaArea);
            }
        }

        // Trilhas pisadas de cada ninho a cada santuário: mostram, sem texto, pra onde as
        // criaturas vão quando não tem ninguém no caminho.
        var trilha = Color.FromHex("#4A543FFF");
        foreach (var equipe in new[] { Equipe.Jogador, Equipe.Inimigo })
        {
            var ninho = Campo.PosicaoNinho(equipe);
            foreach (var santuario in batalha.Santuarios)
            {
                var direcao = Vector2.Normalize(santuario.Posicao - ninho);
                float comprimento = Vector2.Distance(ninho, santuario.Posicao) - Campo.RaioDoNinho - santuario.Raio - 0.3f;

                for (float t = 0f; t <= comprimento; t += 0.55f)
                    formas.DesenharDisco(batch, ParaMundo(ninho + direcao * (Campo.RaioDoNinho + 0.4f + t)), 3.5f, trilha);
            }
        }
    }

    private static void DesenharNinhos(SpriteBatch batch, Formas formas, Batalha batalha)
    {
        float pulso = 0.5f + 0.5f * MathF.Sin(batalha.Tempo * 2.2f);

        foreach (var equipe in new[] { Equipe.Jogador, Equipe.Inimigo })
        {
            var centro = ParaMundo(Campo.PosicaoNinho(equipe));
            var cor = CorDaEquipe(equipe);
            float raio = Campo.RaioDoNinho * Tile;
            float aura = Campo.AlcanceDoNinho * Tile;

            formas.DesenharDisco(batch, centro, aura, cor.WithAlpha(0.05f + 0.03f * pulso));
            formas.DesenharAnel(batch, centro, aura, cor.WithAlpha(0.22f));

            batch.Draw(formas.Disco, centro + new Vector2(0f, raio * 0.35f), new Vector2(raio * 2.2f, raio * 1.2f),
                new Vector2(0.5f), 0f, Sombra);

            // Galhos trançados: dois anéis de madeira em volta do ovo que brilha na cor do time.
            formas.DesenharDisco(batch, centro, raio, Color.FromHex("#3A3226FF"));
            formas.DesenharAnel(batch, centro, raio, Color.FromHex("#6B5639FF"));
            formas.DesenharAnel(batch, centro, raio * 0.78f, Color.FromHex("#57462FFF"));
            formas.DesenharDisco(batch, centro, raio * 0.5f, Escurecer(cor, 0.45f));
            batch.DrawGlow(centro, raio * (0.9f + 0.25f * pulso), cor.WithAlpha(0.45f));
            formas.DesenharDisco(batch, centro, raio * 0.3f, cor);
        }
    }

    private static void DesenharSantuarios(SpriteBatch batch, Formas formas, Batalha batalha)
    {
        const int Contas = 28;
        var pedra = Color.FromHex("#77705FFF");

        foreach (var santuario in batalha.Santuarios)
        {
            var centro = ParaMundo(santuario.Posicao);
            float raio = santuario.Raio * Tile;
            var corDono = santuario.Dono is { } dono ? CorDaEquipe(dono) : Neutro;

            formas.DesenharDisco(batch, centro, raio, Color.FromHex("#262C22FF"));
            formas.DesenharDisco(batch, centro, raio * 0.92f, Color.FromHex("#3B4335FF"));
            formas.DesenharAnel(batch, centro, raio, Color.FromHex("#5E6556FF"));

            if (santuario.Dono is not null)
                formas.DesenharDisco(batch, centro, raio * 0.92f, corDono.WithAlpha(0.14f));

            // Anel de contas: quantas acendem = quanto da influência já foi puxada, na cor de
            // quem puxou. Dá pra ler "falta pouco pra virar" sem número.
            int acesas = (int)MathF.Round(MathF.Abs(santuario.Influencia) * Contas);
            var corInfluencia = CorDaEquipe(santuario.Influencia >= 0f ? Equipe.Jogador : Equipe.Inimigo);

            for (int i = 0; i < Contas; i++)
            {
                float angulo = -MathF.PI / 2f + MathF.Tau * i / Contas;
                var ponto = centro + new Vector2(MathF.Cos(angulo), MathF.Sin(angulo)) * raio * 0.72f;
                bool acesa = i < acesas;
                formas.DesenharDisco(batch, ponto, acesa ? 4.5f : 3f, acesa ? corInfluencia : Color.FromHex("#00000066"));
            }

            for (int k = 0; k < 4; k++)
            {
                float angulo = MathF.PI / 4f + k * MathF.PI / 2f;
                var pilar = centro + new Vector2(MathF.Cos(angulo), MathF.Sin(angulo)) * raio * 1.02f;
                formas.DesenharDisco(batch, pilar + new Vector2(2f, 4f), 9f, Sombra);
                formas.DesenharDisco(batch, pilar, 9f, pedra);
                formas.DesenharDisco(batch, pilar, 5f, corDono);
            }

            if (santuario.Dono is not null)
                batch.DrawGlow(centro, raio * 0.6f, corDono.WithAlpha(0.45f));

            formas.DesenharDisco(batch, centro, 15f, Color.FromHex("#2A2E26FF"));
            formas.DesenharDisco(batch, centro, 11f, corDono);

            if (santuario.Disputado)
            {
                float pisca = 0.5f + 0.5f * MathF.Sin(batalha.Tempo * 12f);
                formas.DesenharAnel(batch, centro, raio + 5f, Color.White.WithAlpha(0.3f + 0.5f * pisca));
            }
        }
    }

    private static void DesenharPedras(SpriteBatch batch, Formas formas)
    {
        foreach (var pedra in Campo.Pedras)
        {
            var centro = ParaMundo(pedra.Centro);
            float raio = pedra.Raio * Tile;

            batch.Draw(formas.Disco, centro + new Vector2(4f, raio * 0.4f), new Vector2(raio * 2.1f, raio * 1.3f),
                new Vector2(0.5f), 0f, Sombra);
            formas.DesenharDisco(batch, centro, raio, Color.FromHex("#4F4B44FF"));
            formas.DesenharDisco(batch, centro - new Vector2(raio * 0.08f, raio * 0.1f), raio * 0.88f, Color.FromHex("#6E6A61FF"));
            formas.DesenharDisco(batch, centro - new Vector2(raio * 0.3f, raio * 0.32f), raio * 0.38f, Color.FromHex("#8C877CFF"));
        }
    }

    private static void DesenharUnidade(SpriteBatch batch, Font fonte, Formas formas, Batalha batalha, Unidade unidade, float alfa)
    {
        var chao = ParaMundo(Vector2.Lerp(unidade.PosicaoAnterior, unidade.Posicao, alfa));
        float raio = unidade.Raio * Tile * EscalaVisual;
        var corpo = chao - new Vector2(0f, unidade.Voa ? 22f : 0f);
        float opacidade = unidade.Implantando > 0f ? 0.5f : 1f;

        batch.Draw(formas.Disco, chao + new Vector2(0f, raio * 0.45f), new Vector2(raio * 1.9f, raio * 0.9f),
            new Vector2(0.5f), 0f, Sombra);

        // Brilho dourado = "esta aqui evoluiu". É a informação que decide a jogada do oponente
        // (matar agora e levar a recompensa), então precisa saltar aos olhos.
        if (unidade.Estagio > 0)
            batch.DrawGlow(corpo, raio * (1.8f + unidade.Estagio * 0.5f), Ouro.WithAlpha(0.55f));

        formas.DesenharDisco(batch, corpo, raio + 3f, CorDaEquipe(unidade.Equipe).WithAlpha(opacidade));
        formas.DesenharDisco(batch, corpo, raio, Color.FromHex(unidade.Carta.Cor).WithAlpha(opacidade));

        if (unidade.Lenta > 0f)
            formas.DesenharDisco(batch, corpo, raio, Color.FromHex("#8D6E4A88"));
        if (unidade.Envenenada > 0f)
            formas.DesenharDisco(batch, corpo, raio, Color.FromHex("#76FF0355"));
        if (batalha.Tempo - unidade.UltimoDano < 0.08f)
            formas.DesenharDisco(batch, corpo, raio, Color.FromHex("#FFFFFF88"));
        if (unidade.Estagio > 0)
            formas.DesenharAnel(batch, corpo, raio + 7f, Ouro);

        float escalaLetra = MathF.Max(0.55f, raio / 22f);
        var medida = fonte.MeasureText(unidade.Carta.Letra, escalaLetra);
        fonte.Draw(batch, unidade.Carta.Letra, corpo - medida / 2f, Color.FromHex("#1B1B22FF").WithAlpha(opacidade), escalaLetra);

        if (unidade.Implantando > 0f)
        {
            float fracao = unidade.Implantando / Batalha.TempoDeImplantacao;
            formas.DesenharAnel(batch, corpo, raio + 4f + 16f * fracao, Color.White.WithAlpha(0.7f));
            return;
        }

        float largura = MathF.Max(30f, raio * 2f);
        float topo = corpo.Y - raio - 14f;
        DesenharBarra(batch, new Vector2(corpo.X, topo), largura, 5f, unidade.Vida / unidade.VidaMaxima, CorDaEquipe(unidade.Equipe));

        if (unidade.ProximaEvolucao is not null)
        {
            batch.DrawRect(new Vector2(corpo.X - largura / 2f, topo + 6f), new Vector2(largura, 3f), Color.FromHex("#00000088"));
            batch.DrawRect(new Vector2(corpo.X - largura / 2f, topo + 6f), new Vector2(largura * unidade.ProgressoDaEvolucao, 3f), Ouro);
        }

        for (int i = 0; i < unidade.Estagio; i++)
        {
            float x = corpo.X - (unidade.Estagio - 1) * 6f + i * 12f;
            formas.DesenharDisco(batch, new Vector2(x, topo - 8f), 4.5f, Ouro);
        }
    }

    private static void DesenharProjeteis(SpriteBatch batch, Formas formas, Batalha batalha, float alfa)
    {
        foreach (var projetil in batalha.Projeteis)
        {
            var posicao = ParaMundo(Vector2.Lerp(projetil.PosicaoAnterior, projetil.Posicao, alfa));
            var cor = projetil.Area > 0f ? Color.FromHex("#FFB86BFF") : Color.FromHex("#EDE6D6FF");

            formas.DesenharDisco(batch, posicao, projetil.Area > 0f ? 7f : 4f, cor);
        }
    }

    private static void DesenharFeiticos(SpriteBatch batch, Formas formas, Batalha batalha)
    {
        foreach (var feitico in batalha.Feiticos)
        {
            var centro = ParaMundo(feitico.Centro);
            float raio = feitico.Carta.Raio * Tile;
            float progresso = 1f - Math.Clamp(feitico.Restante / MathF.Max(0.01f, feitico.Carta.Atraso), 0f, 1f);
            var cor = Color.FromHex(feitico.Carta.Cor);

            formas.DesenharDisco(batch, centro, raio, cor.WithAlpha(0.14f));
            formas.DesenharAnel(batch, centro, raio, cor.WithAlpha(0.4f));
            formas.DesenharAnel(batch, centro, MathF.Max(8f, raio * progresso), cor.WithAlpha(0.9f));
        }
    }

    private void DesenharOndas(SpriteBatch batch, Formas formas)
    {
        foreach (var onda in _ondas)
        {
            float t = onda.Idade / onda.Duracao;
            var centro = ParaMundo(onda.Centro);
            float raio = onda.Raio * Tile * (0.4f + 0.6f * t);

            formas.DesenharDisco(batch, centro, raio, onda.Cor.WithAlpha(onda.Cor.A * 0.3f * (1f - t)));
            formas.DesenharAnel(batch, centro, raio, onda.Cor.WithAlpha(onda.Cor.A * (1f - t)));
        }
    }

    private static void DesenharPrevia(SpriteBatch batch, Formas formas, PreviaDeJogada previa)
    {
        var carta = previa.Carta;
        var centro = ParaMundo(previa.Tile);
        var borda = previa.Valida ? Color.White : Color.FromHex("#FF4040FF");

        if (carta.Tipo == TipoDeCarta.Feitico)
        {
            formas.DesenharDisco(batch, centro, carta.Raio * Tile, borda.WithAlpha(0.18f));
            formas.DesenharAnel(batch, centro, carta.Raio * Tile, borda.WithAlpha(0.8f));
            return;
        }

        if (carta.VelocidadeProjetil > 0f)
            formas.DesenharAnel(batch, centro, (carta.Alcance + carta.Raio) * Tile, Color.White.WithAlpha(0.25f));

        for (int i = 0; i < carta.Quantidade; i++)
        {
            var posicao = ParaMundo(previa.Tile + Batalha.Formacao(i, carta));
            formas.DesenharDisco(batch, posicao, carta.Raio * Tile * EscalaVisual, Color.FromHex(carta.Cor).WithAlpha(0.55f));
            formas.DesenharAnel(batch, posicao, carta.Raio * Tile * EscalaVisual + 3f, borda.WithAlpha(0.9f));
        }
    }

    private void DesenharTextos(SpriteBatch batch, Font fonte)
    {
        foreach (var texto in _textos)
        {
            float t = texto.Idade / texto.Duracao;
            var medida = fonte.MeasureText(texto.Conteudo, texto.Escala);
            var posicao = ParaMundo(texto.Posicao) - medida / 2f - new Vector2(0f, 40f + t * 36f);
            float opacidade = 1f - t * t;

            fonte.Draw(batch, texto.Conteudo, posicao + new Vector2(2f), Color.Black.WithAlpha(0.6f * opacidade), texto.Escala);
            fonte.Draw(batch, texto.Conteudo, posicao, texto.Cor.WithAlpha(texto.Cor.A * opacidade), texto.Escala);
        }
    }

    // ------------------------------------------------------------------ utilidades

    private static void DesenharBarra(SpriteBatch batch, Vector2 centroTopo, float largura, float altura, float fracao, Color cor)
    {
        var canto = new Vector2(centroTopo.X - largura / 2f, centroTopo.Y);
        batch.DrawRect(canto - new Vector2(1f), new Vector2(largura + 2f, altura + 2f), Color.FromHex("#000000AA"));
        batch.DrawRect(canto, new Vector2(largura * Math.Clamp(fracao, 0f, 1f), altura), cor);
    }

    /// <summary>Hash inteiro não negativo do tile — variação de chão estável entre frames.</summary>
    private static int Ruido(int x, int y) => ((x * 73856093) ^ (y * 19349663)) & 0x7FFFFFFF;

    public static Color Escurecer(Color cor, float fator) => new(cor.R * fator, cor.G * fator, cor.B * fator, cor.A);
}
