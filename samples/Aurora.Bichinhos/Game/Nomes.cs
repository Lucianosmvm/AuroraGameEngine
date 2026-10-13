namespace Bichinhos;

/// <summary>
/// Nomes para o bichinho. Dar nome é o que faz alguém sentir o bicho como "meu", mas o celular
/// não tem teclado à mão no meio do jogo: o jogador sorteia até gostar de um.
/// </summary>
public static class Nomes
{
    public static readonly string[] Todos =
    [
        "Fumaça", "Pipoca", "Bolinha", "Nuvem", "Tico", "Mel", "Biscoito", "Faísca", "Pudim", "Chiclete",
        "Jujuba", "Trovão", "Cacau", "Estrela", "Pingo", "Churros", "Brigadeiro", "Gominha", "Canjica", "Bombom",
        "Pimenta", "Fofão", "Neblina", "Tapioca", "Mingau", "Docinho", "Boris", "Lulu", "Zeca", "Mimi",
        "Nino", "Pétala", "Coentro", "Algodão", "Sorvete", "Pérola", "Fubá", "Chuvisco", "Paçoca", "Dindim",
    ];

    /// <summary>Sorteia um nome, evitando repetir o que já está na tela.</summary>
    public static string Sortear(Random rng, string? diferenteDe = null)
    {
        string nome;
        do
        {
            nome = Todos[rng.Next(Todos.Length)];
        }
        while (nome == diferenteDe);
        return nome;
    }
}
