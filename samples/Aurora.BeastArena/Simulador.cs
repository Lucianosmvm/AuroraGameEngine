using BeastArena.Sim;

namespace BeastArena;

/// <summary>
/// <c>--simular N</c>: roda N partidas IA contra IA sem abrir janela e imprime o resumo.
///
/// <para>É a ferramenta de balanceamento: mexeu no <c>cartas.json</c>, roda 200 partidas em
/// segundos e vê se o jogo ficou mais rápido, se a evolução parou de acontecer ou se um lado
/// passou a ganhar sempre. Só funciona porque a batalha não depende de janela nem de relógio.</para>
/// </summary>
public static class Simulador
{
    public static void Rodar(int partidas)
    {
        string caminho = Path.Combine(AppContext.BaseDirectory, "Assets", CatalogoCartas.Caminho);
        var baralho = CatalogoCartas.Carregar(File.ReadAllText(caminho)).Baralho();

        int baixo = 0, cima = 0, empates = 0, naMeta = 0, capturas = 0, evolucoes = 0, recompensa = 0;
        float tempo = 0f, abates = 0f;
        var jogadas = new Dictionary<string, int>();
        var eventos = new List<EventoDeBatalha>();

        for (int semente = 1; semente <= partidas; semente++)
        {
            var batalha = new Batalha(baralho, baralho, semente);
            var azul = new IaOponente(batalha, Equipe.Jogador, Dificuldade.Normal, semente * 7 + 1);
            var vermelha = new IaOponente(batalha, Equipe.Inimigo, Dificuldade.Normal, semente * 13 + 5);

            while (!batalha.Acabou)
            {
                azul.Passo();
                vermelha.Passo();
                batalha.Avancar();

                batalha.ColherEventos(eventos);
                foreach (var evento in eventos.Where(e => e.Tipo == TipoDeEvento.Implantou))
                    jogadas[evento.Texto] = jogadas.GetValueOrDefault(evento.Texto) + 1;
                eventos.Clear();
            }

            switch (batalha.Resultado)
            {
                case ResultadoDaBatalha.VitoriaDoJogador: baixo++; break;
                case ResultadoDaBatalha.VitoriaDoInimigo: cima++; break;
                default: empates++; break;
            }

            if (batalha.TempoRestante > 0f)
                naMeta++;

            tempo += batalha.Tempo;
            foreach (var equipe in new[] { Equipe.Jogador, Equipe.Inimigo })
            {
                var lado = batalha.LadoDe(equipe);
                capturas += lado.Capturas;
                evolucoes += lado.Evolucoes;
                recompensa += lado.ManaDeRecompensa;
                abates += lado.ManaDeAbates;
            }
        }

        Console.WriteLine($"{partidas} partidas (IA normal x IA normal)");
        Console.WriteLine($"  vitórias: baixo {baixo} · cima {cima} · empates {empates}");
        Console.WriteLine($"  duração média: {tempo / partidas:0}s · terminaram na meta de {Batalha.MetaDePontos:0} pontos: {100f * naMeta / partidas:0}%");
        Console.WriteLine($"  santuários dominados por partida: {(float)capturas / partidas:0.0}");
        Console.WriteLine($"  evoluções por partida: {(float)evolucoes / partidas:0.0} · mana de recompensa: {(float)recompensa / partidas:0.0} · mana de abates: {abates / partidas:0.0}");
        Console.WriteLine("  criaturas jogadas (feitiço não entra):");
        foreach (var (nome, vezes) in jogadas.OrderByDescending(j => j.Value))
            Console.WriteLine($"    {nome,-12} {(float)vezes / partidas:0.0} por partida");
    }
}
