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

        /// <summary>Com textura, a onda é a arte do feitiço (poça, labareda) sumindo no chão
        /// em vez de um anel que cresce.</summary>
        public Texture2D? Textura;
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
    private readonly Sprites _sprites;
    private readonly List<Texto> _textos = [];
    private readonly List<Onda> _ondas = [];

    /// <summary>Pra que lado cada unidade olha (por Id). Guardado porque parada, ou andando reto
    /// pra cima, não dá direção nenhuma — sem memória o bicho viraria a cada passo.</summary>
    private readonly Dictionary<int, bool> _olhaPraEsquerda = [];

    public VisaoArena(CatalogoCartas catalogo, Sprites sprites)
    {
        _catalogo = catalogo;
        _sprites = sprites;
    }

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
        _olhaPraEsquerda.Clear();
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
                    var carta = _catalogo.Todas.FirstOrDefault(c => c.Id == evento.Texto);
                    Ondular(evento.Posicao, evento.Raio, Color.FromHex(carta?.Cor ?? "#FFFFFFFF"), 0.45f);
                    if (carta is not null && _sprites.AreaDoFeitico(carta) is { } arte)
                        _ondas.Add(new Onda { Centro = evento.Posicao, Raio = evento.Raio, Cor = Color.White, Duracao = 1.1f, Textura = arte });
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
        DesenharOndas(batch, formas, arte: true);
        DesenharNinhos(batch, formas, batalha);
        DesenharSantuarios(batch, formas, batalha);

        // Pedra entra na mesma ordem por Y das criaturas de chão: bicho atrás da pedra fica atrás dela.
        var deChao = batalha.Unidades.Where(u => !u.Voa).Select(u => (Y: u.Posicao.Y, Unidade: (Unidade?)u, Pedra: default(Circulo)))
            .Concat(Campo.Pedras.Select(p => (Y: p.Centro.Y, Unidade: (Unidade?)null, Pedra: p)))
            .OrderBy(item => item.Y);

        foreach (var item in deChao)
        {
            if (item.Unidade is { } unidade)
                DesenharUnidade(batch, fonte, formas, batalha, unidade, alfa);
            else
                DesenharPedra(batch, formas, item.Pedra);
        }

        foreach (var unidade in batalha.Unidades.Where(u => u.Voa).OrderBy(u => u.Posicao.Y))
            DesenharUnidade(batch, fonte, formas, batalha, unidade, alfa);

        DesenharProjeteis(batch, formas, batalha, alfa);
        DesenharFeiticos(batch, formas, batalha);
        DesenharOndas(batch, formas, arte: false);

        if (previa is { } p)
            DesenharPrevia(batch, formas, p);

        DesenharTextos(batch, fonte);
        EsquecerMortas(batalha);
    }

    private void EsquecerMortas(Batalha batalha)
    {
        if (_olhaPraEsquerda.Count <= batalha.Unidades.Count + 64)
            return;

        var vivas = batalha.Unidades.Select(u => u.Id).ToHashSet();
        foreach (int id in _olhaPraEsquerda.Keys.Where(id => !vivas.Contains(id)).ToList())
            _olhaPraEsquerda.Remove(id);
    }

    private void DesenharChao(SpriteBatch batch, Formas formas, Batalha batalha, PreviaDeJogada? previa)
    {
        bool mostrarArea = previa is { Carta.Tipo: TipoDeCarta.Criatura };
        var foraDaArea = Color.FromHex("#0A0D0999");

        for (int y = 0; y < (int)Campo.Altura; y++)
        {
            for (int x = 0; x < (int)Campo.Largura; x++)
            {
                var canto = ParaMundo(new Vector2(x, y));
                int ruido = Ruido(x, y);
                batch.DrawRect(canto, new Vector2(Tile), Musgo[ruido % Musgo.Length]);

                // Tufos e flores espalhados por hash, não por sorteio: o chão fica igual a cada frame.
                if (ruido % 7 == 0 || ruido % 17 == 1)
                {
                    bool flor = ruido % 7 != 0;
                    var enfeite = flor ? _sprites.Flores[ruido / 3 % 2] : _sprites.Tufos[ruido / 3 % 2];
                    var pe = canto + new Vector2(8f + ruido % 24, 18f + ruido / 7 % 18);
                    float lado = flor ? 16f : 22f + ruido / 11 % 8;
                    batch.Draw(enfeite, pe, new Vector2(lado), new Vector2(0.5f, 0.88f), 0f, Color.White.WithAlpha(0.7f),
                        flipX: ruido % 2 == 0);
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

    private void DesenharNinhos(SpriteBatch batch, Formas formas, Batalha batalha)
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

            batch.Draw(formas.Disco, centro + new Vector2(0f, raio * 0.35f), new Vector2(raio * 2.4f, raio * 1.4f),
                new Vector2(0.5f), 0f, Sombra);

            // Ninho de gravetos com o ovo do time: o ovo é quase branco no PNG e ganha a cor aqui.
            batch.Draw(_sprites.Ninho, centro, new Vector2(raio * 2.3f), new Vector2(0.5f), 0f, Color.White);
            batch.DrawGlow(centro, raio * (0.9f + 0.25f * pulso), cor.WithAlpha(0.45f));
            float ovo = raio * 1.05f;
            batch.Draw(_sprites.Ovo, centro + new Vector2(0f, ovo * 0.1f), new Vector2(ovo), new Vector2(0.5f), 0f, Clarear(cor, 0.25f));
        }
    }

    private void DesenharSantuarios(SpriteBatch batch, Formas formas, Batalha batalha)
    {
        const int Contas = 28;

        // A plataforma do PNG tem raio 47 numa tela de 100, e o sulco das contas fica em 32.
        const float LadoDoSprite = 100f / 47f;
        const float RaioDasContas = 32f / 47f;

        // Pilar: lado na tela e altura do soquete da gema acima do pé (y 26 da tela, pé em 88).
        const float LadoDoPilar = 36f;
        const float AlturaDaGema = (88f - 26f) / 100f * LadoDoPilar;

        foreach (var santuario in batalha.Santuarios)
        {
            var centro = ParaMundo(santuario.Posicao);
            float raio = santuario.Raio * Tile;
            var corDono = santuario.Dono is { } dono ? CorDaEquipe(dono) : Neutro;

            batch.Draw(_sprites.Santuario, centro, new Vector2(raio * LadoDoSprite), new Vector2(0.5f), 0f, Color.White);

            if (santuario.Dono is not null)
                formas.DesenharDisco(batch, centro, raio * 0.76f, corDono.WithAlpha(0.18f));

            // Anel de contas: quantas acendem = quanto da influência já foi puxada, na cor de
            // quem puxou. Dá pra ler "falta pouco pra virar" sem número.
            int acesas = (int)MathF.Round(MathF.Abs(santuario.Influencia) * Contas);
            var corInfluencia = CorDaEquipe(santuario.Influencia >= 0f ? Equipe.Jogador : Equipe.Inimigo);

            for (int i = 0; i < Contas; i++)
            {
                float angulo = -MathF.PI / 2f + MathF.Tau * i / Contas;
                var ponto = centro + new Vector2(MathF.Cos(angulo), MathF.Sin(angulo)) * raio * RaioDasContas;
                bool acesa = i < acesas;
                formas.DesenharDisco(batch, ponto, acesa ? 4.5f : 3f, acesa ? corInfluencia : Color.FromHex("#00000066"));
            }

            for (int k = 0; k < 4; k++)
            {
                float angulo = MathF.PI / 4f + k * MathF.PI / 2f;
                var pilar = centro + new Vector2(MathF.Cos(angulo), MathF.Sin(angulo)) * raio * 1.02f;
                batch.Draw(formas.Disco, pilar + new Vector2(3f, 0f), new Vector2(26f, 12f), new Vector2(0.5f), 0f, Sombra);
                batch.Draw(_sprites.Pilar, pilar, new Vector2(LadoDoPilar), new Vector2(0.5f, 0.88f), 0f, Color.White);
                formas.DesenharDisco(batch, pilar - new Vector2(0f, AlturaDaGema), 4.5f, corDono);
            }

            if (santuario.Dono is not null)
                batch.DrawGlow(centro, raio * 0.6f, corDono.WithAlpha(0.45f));

            formas.DesenharDisco(batch, centro, 10f, corDono);

            if (santuario.Disputado)
            {
                float pisca = 0.5f + 0.5f * MathF.Sin(batalha.Tempo * 12f);
                formas.DesenharAnel(batch, centro, raio + 5f, Color.White.WithAlpha(0.3f + 0.5f * pisca));
            }
        }
    }

    private void DesenharPedra(SpriteBatch batch, Formas formas, Circulo pedra)
    {
        var centro = ParaMundo(pedra.Centro);
        float raio = pedra.Raio * Tile;
        int variante = Ruido((int)(pedra.Centro.X * 10f), (int)(pedra.Centro.Y * 10f)) % _sprites.Pedras.Count;

        batch.Draw(formas.Disco, centro + new Vector2(6f, raio * 0.45f), new Vector2(raio * 2.3f, raio * 1.3f),
            new Vector2(0.5f), 0f, Sombra);
        batch.Draw(_sprites.Pedras[variante], centro, new Vector2(raio * 2.5f), new Vector2(0.5f, 0.56f), 0f, Color.White,
            flipX: variante == 1);
    }

    private void DesenharUnidade(SpriteBatch batch, Font fonte, Formas formas, Batalha batalha, Unidade unidade, float alfa)
    {
        var chao = ParaMundo(Vector2.Lerp(unidade.PosicaoAnterior, unidade.Posicao, alfa));
        float raio = unidade.Raio * Tile * EscalaVisual;
        float opacidade = unidade.Implantando > 0f ? 0.5f : 1f;
        var corDaEquipe = CorDaEquipe(unidade.Equipe);
        var arte = _sprites.Criatura(unidade.Carta, unidade.Estagio);

        // Sombra e aro do time no chão (voador também: é o que diz onde ele está de verdade).
        var pe = chao + new Vector2(0f, raio * 0.3f);
        var aro = new Vector2(raio * 1.9f, raio * 0.95f);
        batch.Draw(formas.Disco, pe + new Vector2(0f, 2f), aro * (unidade.Voa ? 0.7f : 1f), new Vector2(0.5f), 0f, Sombra);
        batch.Draw(formas.Disco, pe, aro, new Vector2(0.5f), 0f, corDaEquipe.WithAlpha(0.22f * opacidade));
        batch.Draw(formas.Anel, pe, aro, new Vector2(0.5f), 0f, corDaEquipe.WithAlpha(0.9f * opacidade));

        // Anel de ouro no pé = "esta aqui evoluiu". É a informação que decide a jogada do
        // oponente (matar agora e levar a recompensa), então precisa saltar aos olhos.
        if (unidade.Estagio > 0)
            batch.Draw(formas.Anel, pe, aro * 1.22f, new Vector2(0.5f), 0f, Ouro);

        float lado = raio * 2f * Sprites.FatorDaCriatura(unidade.Carta);
        var (ancora, inclinacao, esquerda) = Pose(batalha, unidade, chao, lado, alfa);
        var centroDoCorpo = arte is null ? ancora : ancora - new Vector2(0f, lado * 0.36f);

        if (unidade.Estagio > 0)
            batch.DrawGlow(centroDoCorpo, raio * (1.8f + unidade.Estagio * 0.5f), Ouro.WithAlpha(0.5f));

        if (arte is not null)
        {
            batch.Draw(arte, ancora, new Vector2(lado), new Vector2(0.5f, 0.88f), inclinacao,
                TintaDeEstado(batalha, unidade).WithAlpha(opacidade), flipX: esquerda);
        }
        else
        {
            // Carta sem arte ainda: o disco com letra do protótipo.
            formas.DesenharDisco(batch, ancora, raio + 3f, corDaEquipe.WithAlpha(opacidade));
            formas.DesenharDisco(batch, ancora, raio, Tingir(Color.FromHex(unidade.Carta.Cor), TintaDeEstado(batalha, unidade)).WithAlpha(opacidade));

            float escalaLetra = MathF.Max(0.55f, raio / 22f);
            var medida = fonte.MeasureText(unidade.Carta.Letra, escalaLetra);
            fonte.Draw(batch, unidade.Carta.Letra, ancora - medida / 2f, Color.FromHex("#1B1B22FF").WithAlpha(opacidade), escalaLetra);
        }

        if (unidade.Implantando > 0f)
        {
            float fracao = unidade.Implantando / Batalha.TempoDeImplantacao;
            batch.Draw(formas.Anel, pe, aro * (1.1f + 0.8f * fracao), new Vector2(0.5f), 0f, Color.White.WithAlpha(0.7f));
            return;
        }

        float largura = MathF.Max(30f, raio * 2f);
        float topo = arte is null ? ancora.Y - raio - 14f : ancora.Y - lado * 0.74f - 4f;
        DesenharBarra(batch, new Vector2(chao.X, topo), largura, 5f, unidade.Vida / unidade.VidaMaxima, corDaEquipe);

        if (unidade.ProximaEvolucao is not null)
        {
            batch.DrawRect(new Vector2(chao.X - largura / 2f, topo + 6f), new Vector2(largura, 3f), Color.FromHex("#00000088"));
            batch.DrawRect(new Vector2(chao.X - largura / 2f, topo + 6f), new Vector2(largura * unidade.ProgressoDaEvolucao, 3f), Ouro);
        }

        for (int i = 0; i < unidade.Estagio; i++)
        {
            float x = chao.X - (unidade.Estagio - 1) * 6f + i * 12f;
            formas.DesenharDisco(batch, new Vector2(x, topo - 8f), 4.5f, Ouro);
        }
    }

    /// <summary>
    /// Animação sem quadros: o sprite é um só, e a vida vem de mexer nele. Andando, quica e
    /// balança; voando, flutua; no golpe, dá um bote pra frente. Tudo derivado do estado da
    /// simulação — nada aqui guarda tempo de animação além de pra que lado o bicho olha.
    /// </summary>
    private (Vector2 Ancora, float Inclinacao, bool Esquerda) Pose(Batalha batalha, Unidade unidade, Vector2 chao, float lado, float alfa)
    {
        float tempo = batalha.Tempo + alfa * Batalha.Passo + unidade.Id * 0.37f;
        var passo = unidade.Posicao - unidade.PosicaoAnterior;
        bool andando = unidade.Implantando <= 0f && passo.LengthSquared() > 1e-6f;

        // Direção: pro alvo quando está batendo; senão pra onde anda, se anda de lado o bastante.
        if (!_olhaPraEsquerda.TryGetValue(unidade.Id, out bool esquerda))
            esquerda = unidade.Equipe == Equipe.Inimigo;

        if (unidade.Atacando && unidade.Alvo is { Viva: true } alvo && MathF.Abs(alvo.Posicao.X - unidade.Posicao.X) > 0.15f)
            esquerda = alvo.Posicao.X < unidade.Posicao.X;
        else if (andando && MathF.Abs(passo.X) > MathF.Abs(passo.Y) * 0.35f)
            esquerda = passo.X < 0f;

        _olhaPraEsquerda[unidade.Id] = esquerda;
        float frente = esquerda ? -1f : 1f;

        var ancora = chao;
        float inclinacao = 0f;

        if (unidade.Voa)
        {
            ancora.Y -= 22f + MathF.Sin(tempo * 7f) * 3f;
            inclinacao = MathF.Sin(tempo * 3.5f) * 0.05f;
        }
        else if (andando)
        {
            float fase = tempo * 11f;
            ancora.Y -= MathF.Abs(MathF.Sin(fase)) * lado * 0.035f;
            inclinacao = MathF.Sin(fase) * 0.05f;
        }

        // Bote: a Recarga volta pra Cadencia no instante do golpe.
        float desdeOGolpe = unidade.Carta.Cadencia - unidade.Recarga;
        if (unidade.Atacando && desdeOGolpe is >= 0f and < 0.25f)
        {
            float bote = MathF.Sin(desdeOGolpe / 0.25f * MathF.PI);
            ancora.X += frente * bote * lado * 0.09f;
            inclinacao += frente * bote * 0.14f;
        }

        return (ancora, inclinacao, esquerda);
    }

    /// <summary>Golpe recebido pisca vermelho; lama e veneno tingem enquanto duram.</summary>
    private static Color TintaDeEstado(Batalha batalha, Unidade unidade)
    {
        if (batalha.Tempo - unidade.UltimoDano < 0.08f)
            return new Color(1f, 0.45f, 0.45f, 1f);
        if (unidade.Envenenada > 0f)
            return new Color(0.7f, 1f, 0.55f, 1f);
        if (unidade.Lenta > 0f)
            return new Color(0.85f, 0.7f, 0.5f, 1f);
        return Color.White;
    }

    private void DesenharProjeteis(SpriteBatch batch, Formas formas, Batalha batalha, float alfa)
    {
        foreach (var projetil in batalha.Projeteis)
        {
            var posicao = ParaMundo(Vector2.Lerp(projetil.PosicaoAnterior, projetil.Posicao, alfa));
            var carta = projetil.Fonte.Carta;

            // Tiro com área que a forma base não tinha (Rei dos Espinhos) = tiro evoluído: dourado e maior.
            bool evoluido = projetil.Area > 0f && carta.Area <= 0f;

            if (_sprites.Projetil(carta) is { } arte)
            {
                var direcao = projetil.Posicao - projetil.PosicaoAnterior;
                float angulo = direcao.LengthSquared() > 1e-8f ? MathF.Atan2(direcao.Y, direcao.X) : 0f;
                float tamanho = (projetil.Area > 0f ? 34f : 26f) * (evoluido ? 1.2f : 1f);
                batch.Draw(arte, posicao, new Vector2(tamanho), new Vector2(0.5f), angulo, evoluido ? Ouro : Color.White);
                continue;
            }

            var cor = projetil.Area > 0f ? Color.FromHex("#FFB86BFF") : Color.FromHex("#EDE6D6FF");
            formas.DesenharDisco(batch, posicao, projetil.Area > 0f ? 7f : 4f, cor);
        }
    }

    private void DesenharFeiticos(SpriteBatch batch, Formas formas, Batalha batalha)
    {
        foreach (var feitico in batalha.Feiticos)
        {
            var centro = ParaMundo(feitico.Centro);
            float raio = feitico.Carta.Raio * Tile;
            float progresso = 1f - Math.Clamp(feitico.Restante / MathF.Max(0.01f, feitico.Carta.Atraso), 0f, 1f);
            var cor = Color.FromHex(feitico.Carta.Cor);

            // A arte vai "se formando" enquanto o feitiço não cai: dá pra fugir se viu a tempo.
            if (_sprites.AreaDoFeitico(feitico.Carta) is { } arte)
                batch.Draw(arte, centro, new Vector2(raio * 2f * (0.6f + 0.4f * progresso)), new Vector2(0.5f), 0f, Color.White.WithAlpha(0.15f + 0.35f * progresso));
            else
                formas.DesenharDisco(batch, centro, raio, cor.WithAlpha(0.14f));

            formas.DesenharAnel(batch, centro, raio, cor.WithAlpha(0.4f));
            formas.DesenharAnel(batch, centro, MathF.Max(8f, raio * progresso), cor.WithAlpha(0.9f));
        }
    }

    /// <param name="arte">true = só as ondas com textura (vão no chão, debaixo das criaturas);
    /// false = só os anéis (por cima de tudo).</param>
    private void DesenharOndas(SpriteBatch batch, Formas formas, bool arte)
    {
        foreach (var onda in _ondas)
        {
            if (onda.Textura is not null != arte)
                continue;

            float t = onda.Idade / onda.Duracao;
            var centro = ParaMundo(onda.Centro);

            if (onda.Textura is { } textura)
            {
                float lado = onda.Raio * Tile * 2f * (1f + 0.08f * t);
                batch.Draw(textura, centro, new Vector2(lado), new Vector2(0.5f), 0f, Color.White.WithAlpha(MathF.Pow(1f - t, 1.5f)));
                continue;
            }

            float raio = onda.Raio * Tile * (0.4f + 0.6f * t);
            formas.DesenharDisco(batch, centro, raio, onda.Cor.WithAlpha(onda.Cor.A * 0.3f * (1f - t)));
            formas.DesenharAnel(batch, centro, raio, onda.Cor.WithAlpha(onda.Cor.A * (1f - t)));
        }
    }

    private void DesenharPrevia(SpriteBatch batch, Formas formas, PreviaDeJogada previa)
    {
        var carta = previa.Carta;
        var centro = ParaMundo(previa.Tile);
        var borda = previa.Valida ? Color.White : Color.FromHex("#FF4040FF");

        if (carta.Tipo == TipoDeCarta.Feitico)
        {
            if (_sprites.AreaDoFeitico(carta) is { } area)
                batch.Draw(area, centro, new Vector2(carta.Raio * Tile * 2f), new Vector2(0.5f), 0f, borda.WithAlpha(0.35f));
            else
                formas.DesenharDisco(batch, centro, carta.Raio * Tile, borda.WithAlpha(0.18f));
            formas.DesenharAnel(batch, centro, carta.Raio * Tile, borda.WithAlpha(0.8f));
            return;
        }

        if (carta.VelocidadeProjetil > 0f)
            formas.DesenharAnel(batch, centro, (carta.Alcance + carta.Raio) * Tile, Color.White.WithAlpha(0.25f));

        var arte = _sprites.Criatura(carta, 0);
        float raio = carta.Raio * Tile * EscalaVisual;

        for (int i = 0; i < carta.Quantidade; i++)
        {
            var posicao = ParaMundo(previa.Tile + Batalha.Formacao(i, carta));
            var aro = new Vector2(raio * 1.9f, raio * 0.95f);
            var pe = posicao + new Vector2(0f, raio * 0.3f);

            if (arte is null)
            {
                formas.DesenharDisco(batch, posicao, raio, Color.FromHex(carta.Cor).WithAlpha(0.55f));
                formas.DesenharAnel(batch, posicao, raio + 3f, borda.WithAlpha(0.9f));
                continue;
            }

            batch.Draw(formas.Anel, pe, aro, new Vector2(0.5f), 0f, borda.WithAlpha(0.9f));
            float lado = raio * 2f * Sprites.FatorDaCriatura(carta);
            var ancora = posicao - new Vector2(0f, carta.Voa ? 22f : 0f);
            batch.Draw(arte, ancora, new Vector2(lado), new Vector2(0.5f, 0.88f), 0f,
                (previa.Valida ? Color.White : Color.FromHex("#FF8080FF")).WithAlpha(0.6f));
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

    public static Color Clarear(Color cor, float fator)
        => new(cor.R + (1f - cor.R) * fator, cor.G + (1f - cor.G) * fator, cor.B + (1f - cor.B) * fator, cor.A);

    private static Color Tingir(Color cor, Color tinta) => new(cor.R * tinta.R, cor.G * tinta.G, cor.B * tinta.B, cor.A * tinta.A);
}
