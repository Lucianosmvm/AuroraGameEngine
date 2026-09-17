using Aurora.Runtime;
using Aurora.Runtime.Ecs;

namespace CaminhosDaFe;

/// <summary>
/// O roteiro do protótipo: a missão "O Rebanho" do Ato 1. Tudo que depende de "em que ponto da
/// história estamos" mora aqui — falas do Jessé, texto de objetivo da HUD, o que acontece quando
/// a última ovelha entra no curral e quando o lobo cai.
///
/// <para>O progresso é o estágio da quest <see cref="Id"/> no <c>QuestManager</c> da engine
/// (salvo junto com o jogo), e contagens e virtudes são variáveis do <c>GameState</c>. Nenhum
/// estado fica nesta classe — por isso ela é estática.</para>
/// </summary>
public static class Missao
{
    public const string Id = "Rebanho";

    // Estágios.
    public const int NaoIniciada = 0;
    public const int BuscandoOvelhas = 1;
    public const int OvelhasRecolhidas = 2;
    public const int EnfrentarLobo = 3;
    public const int LoboVencido = 4;
    public const int Concluida = 5;

    public const int TotalOvelhas = 3;
    public const string VarOvelhas = "OvelhasNoCurral";
    public const string VarFe = "Fe";
    public const string VarCoragem = "Coragem";
    public const string SwitchLobo = "LoboAcordado";

    private const string Jesse = "Jessé";
    private const string RetratoJesse = "sprites/retrato_jesse.png";
    private const string DaviNome = "Davi";
    private const string RetratoDavi = "sprites/retrato_davi.png";

    public static int Estagio(World? world) => world?.Quests?.GetStage(Id) ?? 0;

    public static void FalarCom(string npc, World? world)
    {
        if (npc != "jesse" || world?.Dialogue is not { } d || world.Quests is not { } quests || world.State is not { } state)
            return;

        switch (quests.GetStage(Id))
        {
            case NaoIniciada:
                d.ShowMessage("Davi, meu filho! Três ovelhas se afastaram do rebanho enquanto você dormia.", Jesse, RetratoJesse);
                d.ShowMessage("Uma foi para a beira do rio, outra para o bosque a leste, e a mais teimosa se meteu entre os arbustos ao sul.", Jesse, RetratoJesse);
                d.ShowChoice("O que você responde?", ["Eu vou buscá-las, pai.", "Por que sempre eu?"], escolha =>
                {
                    if (escolha == 0)
                    {
                        state.AddVariable(VarFe, 1f);
                        d.ShowMessage("Eu sabia que podia contar com você.", Jesse, RetratoJesse);
                    }
                    else
                    {
                        d.ShowMessage("Porque quem cuida do pouco com zelo será chamado para cuidar de muito. Vá, filho.", Jesse, RetratoJesse);
                    }

                    d.ShowMessage("Chegue perto de uma ovelha e aperte E para chamá-la. Ela vai seguir você até o curral.", null);
                    quests.SetStage(Id, BuscandoOvelhas);
                    ConferirRebanho(world);
                });
                break;

            case BuscandoOvelhas:
                int faltam = TotalOvelhas - (int)state.GetVariable(VarOvelhas);
                d.ShowMessage(faltam == 1
                    ? "Falta só uma. Não desista dela."
                    : $"Ainda faltam {faltam} ovelhas. O rebanho só está completo com todas.", Jesse, RetratoJesse);
                break;

            case OvelhasRecolhidas:
                d.ShowMessage("Todas de volta, e nenhuma ferida. Muito bem, Davi.", Jesse, RetratoJesse);
                d.ShowMessage("...Ouviu isso? Um uivo, lá das pedras ao norte.", Jesse, RetratoJesse);
                d.ShowMessage("Um lobo. Ele vai descer atrás do rebanho.", DaviNome, RetratoDavi);
                d.ShowMessage("Pegue sua funda. Segure o botão do mouse para girá-la e solte para arremessar. Quanto mais girar, mais forte.", Jesse, RetratoJesse);
                d.ShowMessage("Quando ele se encolher, vai saltar. Saia da frente, ou acerte-o antes.", Jesse, RetratoJesse);
                quests.SetStage(Id, EnfrentarLobo);
                state.SetSwitch(SwitchLobo, true);
                break;

            case EnfrentarLobo:
                d.ShowMessage("O lobo ainda ronda. Proteja o rebanho, filho!", Jesse, RetratoJesse);
                break;

            case LoboVencido:
                d.ShowMessage("Você não fugiu. Enfrentou o lobo para guardar o que lhe foi confiado.", Jesse, RetratoJesse);
                d.ShowMessage("Um dia, Davi, o Senhor vai te pedir coragem para coisas maiores do que um lobo.", Jesse, RetratoJesse);
                state.AddVariable(VarCoragem, 1f);
                quests.SetStage(Id, Concluida);
                break;

            default:
                d.ShowMessage("Descanse, filho. O rebanho está seguro.", Jesse, RetratoJesse);
                break;
        }
    }

    public static void OvelhaRecolhida(World? world)
    {
        if (world?.State is not { } state)
            return;

        // Ovelha do rebanho nasce no curral e não passa por aqui; só as que o Davi trouxe contam.
        state.AddVariable(VarOvelhas, 1f);
        ConferirRebanho(world);
    }

    /// <summary>Avança quando as três estão no curral. Chamado também ao aceitar a missão: quem
    /// recolheu as ovelhas antes de falar com o Jessé não pode ficar preso no estágio 1.</summary>
    private static void ConferirRebanho(World world)
    {
        if (world.State is not { } state || world.Quests is not { } quests)
            return;

        if (state.GetVariable(VarOvelhas) >= TotalOvelhas && quests.GetStage(Id) == BuscandoOvelhas)
        {
            quests.SetStage(Id, OvelhasRecolhidas);
            world.Dialogue?.ShowMessage("Todas as ovelhas estão no curral. Volte e fale com Jessé.", null);
        }
    }

    public static void LoboDerrotado(World? world)
    {
        if (world?.Quests is not { } quests || quests.GetStage(Id) != EnfrentarLobo)
            return;

        quests.SetStage(Id, LoboVencido);
        world.Dialogue?.ShowMessage("O lobo fugiu ferido e não vai voltar. Conte a Jessé.", DaviNome, RetratoDavi);
    }

    /// <summary>Linha de objetivo da HUD.</summary>
    public static string Objetivo(QuestManager quests, GameState state) => quests.GetStage(Id) switch
    {
        NaoIniciada => "Fale com Jessé, seu pai.",
        BuscandoOvelhas => $"Traga as ovelhas perdidas ao curral ({(int)state.GetVariable(VarOvelhas)}/{TotalOvelhas})",
        OvelhasRecolhidas => "Volte e fale com Jessé.",
        EnfrentarLobo => "Enfrente o lobo que desceu das pedras.",
        LoboVencido => "Conte a Jessé o que aconteceu.",
        _ => "Capítulo concluído.",
    };
}
