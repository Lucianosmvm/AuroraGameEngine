namespace Bichinhos;

/// <param name="Dia">Posição na sequência de 7 (1 a 7).</param>
/// <param name="Moedas">Quanto vale o presente deste dia.</param>
/// <param name="Sequencia">Quantos dias seguidos o jogador abriu o jogo, contando hoje.</param>
/// <param name="Item">Id do item que vem junto (só o 7º dia, e só se o jogador ainda não tem).</param>
public readonly record struct PresenteDoDia(int Dia, int Moedas, int Sequencia, string? Item = null);

/// <summary>
/// Presente diário com sequência de dias: o motivo mais simples para abrir o jogo amanhã. Faltar
/// um dia zera a sequência (sem punir além disso). O sétimo dia paga o dobro do sexto e dá a coroa dourada.
///
/// <para>A data é a do aparelho, em dia de calendário local (não "24 horas depois"): quem abre às
/// 23h e de novo às 8h da manhã seguinte ganhou dois dias de verdade.</para>
/// </summary>
public static class Presente
{
    public static readonly int[] MoedasPorDia = [5, 7, 9, 11, 13, 15, 30];

    private const string Formato = "yyyy-MM-dd";

    /// <summary>O presente que está esperando hoje, ou null se já foi pego.</summary>
    public static PresenteDoDia? Pendente(Progresso progresso, DateOnly hoje)
    {
        bool temUltimo = DateOnly.TryParseExact(progresso.UltimoPresente, Formato, out var ultimo);

        // Relógio voltado pra trás: não dá presente de novo.
        if (temUltimo && ultimo >= hoje)
            return null;

        int sequencia = temUltimo && ultimo == hoje.AddDays(-1) ? progresso.SequenciaDias + 1 : 1;
        int dia = (sequencia - 1) % MoedasPorDia.Length + 1;
        string? item = dia == MoedasPorDia.Length && !Loja.Tem(progresso, Itens.Coroa) ? Itens.Coroa.Id : null;
        return new PresenteDoDia(dia, MoedasPorDia[dia - 1], sequencia, item);
    }

    /// <summary>Entrega o presente de hoje (soma as moedas e marca o dia). Null se já foi pego.</summary>
    public static PresenteDoDia? Receber(Progresso progresso, DateOnly hoje)
    {
        if (Pendente(progresso, hoje) is not { } presente)
            return null;

        progresso.Moedas += presente.Moedas;
        if (Itens.Achar(presente.Item) is { } item)
            Loja.Dar(progresso, item);
        progresso.SequenciaDias = presente.Sequencia;
        progresso.UltimoPresente = hoje.ToString(Formato);
        return presente;
    }
}
