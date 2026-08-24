using Silk.NET.Input;

namespace Aurora.Runtime.Input;

/// <summary>Que família de controle um nome de cena aponta.</summary>
public enum InputKind
{
    /// <summary>Nome vazio ou que não corresponde a controle nenhum.</summary>
    None,
    Key,
    Mouse,
    Gamepad,
}

/// <summary>
/// Traduz o NOME de um controle escrito na cena ("E", "Space", "MouseLeft", "GamepadA") no
/// "foi acionado neste frame" correspondente.
///
/// <para>Existe pra que escolher a tecla de interagir seja um campo da cena, e não uma decisão
/// do código da engine. Antes só nome de tecla era aceito (<c>Enum.TryParse&lt;Key&gt;</c>): um
/// "MouseLeft" virava <c>Key.Unknown</c> e o gatilho nunca disparava — sem erro, sem aviso.</para>
///
/// <para>No Android o toque entra como clique esquerdo (o InputManager dobra o ponteiro da
/// MainActivity no mesmo caminho do mouse, ver <c>BeginFrame</c>), então "MouseLeft" é também
/// "tocar na tela" — é a escolha certa pra interação que precisa servir desktop e celular.</para>
/// </summary>
public static class InputBinding
{
    private const string MousePrefix = "Mouse";
    private const string GamepadPrefix = "Gamepad";

    /// <summary>Nome resolvido: a família e o valor do enum correspondente.</summary>
    public readonly record struct Binding(InputKind Kind, int Code);

    /// <summary>
    /// Interpreta o nome, sem consultar o estado do input. Separado de
    /// <see cref="WasPressed"/> de propósito: é aqui que mora a decisão que pode falhar calada
    /// (nome digitado errado), e sendo puro dá pra prender cada caso num teste sem precisar de
    /// janela, teclado ou GL.
    /// </summary>
    public static Binding Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new Binding(InputKind.None, 0);

        string trimmed = name.Trim();

        if (trimmed.StartsWith(MousePrefix, StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<MouseButton>(trimmed[MousePrefix.Length..], ignoreCase: true, out var button)
            && button != MouseButton.Unknown)
            return new Binding(InputKind.Mouse, (int)button);

        if (trimmed.StartsWith(GamepadPrefix, StringComparison.OrdinalIgnoreCase)
            && Enum.TryParse<ButtonName>(trimmed[GamepadPrefix.Length..], ignoreCase: true, out var gamepadButton)
            && gamepadButton != ButtonName.Unknown)
            return new Binding(InputKind.Gamepad, (int)gamepadButton);

        // Enum.TryParse aceita número solto ("5") como valor do enum. Um nome desses na cena é
        // quase certamente engano de digitação, e virar uma tecla arbitrária seria pior que não
        // funcionar: exige que comece por letra.
        return char.IsLetter(trimmed[0])
               && Enum.TryParse<Key>(trimmed, ignoreCase: true, out var key)
               && key != Key.Unknown
            ? new Binding(InputKind.Key, (int)key)
            : new Binding(InputKind.None, 0);
    }

    /// <summary>True no frame em que o controle nomeado foi acionado (borda, não estado
    /// contínuo). Nome vazio ou desconhecido devolve false.</summary>
    public static bool WasPressed(InputManager? input, string? name)
    {
        if (input is null)
            return false;

        var binding = Parse(name);

        return binding.Kind switch
        {
            InputKind.Key => input.WasKeyPressed((Key)binding.Code),
            InputKind.Mouse => input.WasMouseClicked((MouseButton)binding.Code),
            InputKind.Gamepad => input.WasGamepadButtonPressed((ButtonName)binding.Code),
            _ => false,
        };
    }
}
