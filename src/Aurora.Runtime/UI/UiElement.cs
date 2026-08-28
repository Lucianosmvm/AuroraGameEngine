using System.Numerics;
using Aurora.Runtime.Events;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.UI;

/// <summary>Elemento de tela (HUD/menu) em coordenadas de pixel de tela — não segue a câmera.</summary>
public abstract class UiElement
{
    public string Name = "";
    public float X;
    public float Y;

    /// <summary>Left (padrão: X é a borda esquerda, pixel absoluto — bom pra HUD grudado no
    /// canto) | Center (X é deslocamento a partir do centro horizontal da tela — bom pra
    /// menu, funciona igual em qualquer resolução) | Right (X é deslocamento a partir da
    /// borda direita). Sem isso, coordenada fixa só fica correta numa resolução específica —
    /// telas de Android reais são bem mais largas que a referência 1280x720 usada ao autorar.</summary>
    public string AnchorX = "Left";

    /// <summary>Top (padrão) | Center | Bottom — mesma ideia do AnchorX, eixo vertical.</summary>
    public string AnchorY = "Top";
}

/// <summary>Texto com suporte a tokens: {Nome} (variável do GameState), {Item:Nome} (quantidade
/// no inventário), {Quest:Nome} (estágio da quest) — resolvidos a cada frame no Draw.</summary>
public sealed class UiText : UiElement
{
    public string Text = "";
    public string Color = "#FFFFFFFF";
    public float Scale = 1f;

    /// <summary>Largura máxima em pixels de tela antes de quebrar a linha. 0 (padrão) = sem
    /// quebra automática, o texto vai até onde for. Quebras <c>\n</c> escritas no Text valem
    /// nos dois casos.</summary>
    public float MaxWidth;

    // Memo de uma entrada só do texto já quebrado: Text passa por interpolação de {Var} e pode
    // mudar todo frame, então a chave é o texto resolvido, não o template. Fica por elemento
    // (não na Font) pra telas com vários UiText não brigarem por um cache único.
    internal string? WrapSource;
    internal float WrapWidth;
    internal float WrapScale;
    internal Graphics.Font? WrapFont;
    internal string WrapResult = "";
}

/// <summary>Ícone/imagem estática (textura resolvida no Load).</summary>
public sealed class UiImage : UiElement
{
    public string? TexturePath;
    internal Texture2D? Texture;
    public float Width;
    public float Height;
    public string Color = "#FFFFFFFF";
}

/// <summary>Barra de progresso (vida, mana, XP…) lendo uma variável do GameState (0..Max).</summary>
public sealed class UiBar : UiElement
{
    public float Width = 100f;
    public float Height = 12f;
    public string Variable = "";
    public float Max = 100f;
    public string FillColor = "#40C040FF";
    public string BackColor = "#303030FF";
}

/// <summary>
/// Barra arrastável — volume, brilho, dificuldade. O irmão interativo do <see cref="UiBar"/>,
/// que só desenha.
///
/// <para>Liga em UM dos dois: <see cref="Setting"/> (preferência, sobrevive ao fechar o jogo e
/// não entra no save) ou <see cref="Variable"/> (variável do jogo, entra no save). Volume é
/// sempre Setting — guardado no save, cada slot teria o seu e jogo novo voltaria ao padrão.</para>
/// </summary>
public sealed class UiSlider : UiElement
{
    public float Width = 200f;
    public float Height = 20f;

    /// <summary>Chave de preferência (<see cref="Aurora.Runtime.Saves.GameSettings"/>). Para
    /// volume use "MasterVolume", "MusicVolume" ou "SfxVolume" — a engine lê essas três sozinha.
    /// Preenchida, tem precedência sobre <see cref="Variable"/>.</summary>
    public string Setting = "";

    /// <summary>Variável numérica do GameState, quando o valor é do jogo e não preferência.</summary>
    public string Variable = "";

    public float Min;
    public float Max = 1f;

    /// <summary>Degrau do arrasto. 0 (padrão) = contínuo. 0.1 num volume dá dez paradas, que é
    /// mais fácil de repetir do que um valor qualquer no meio.</summary>
    public float Step;

    /// <summary>Valor usado quando a chave ainda não existe — o estado de "nunca mexeram nisso".
    /// Volume quer 1 aqui; deixar 0 faria o jogo abrir mudo antes do primeiro ajuste.</summary>
    public float Default = 1f;

    public string BackColor = "#303030FF";
    public string FillColor = "#4A88C8FF";
    public string KnobColor = "#FFFFFFFF";

    /// <summary>Largura da alça. 0 esconde a alça e deixa só a barra preenchendo.</summary>
    public float KnobWidth = 10f;

    /// <summary>Valor atual, já dentro de Min..Max. Leitura pra código.</summary>
    public float Value { get; internal set; }

    /// <summary>Dedo/mouse que está arrastando agora — mesmo mecanismo do UiJoystick, que é o que
    /// mantém o arrasto vivo enquanto o ponteiro sai de cima do elemento.</summary>
    internal int? OwnerTouchId;

    // Último valor sincronizado, pra saber se a mudança veio do arrasto ou de fora.
    internal float LastSynced = float.NaN;
}

/// <summary>Retângulo sólido — fundo de janela/painel.</summary>
public sealed class UiPanel : UiElement
{
    public float Width = 100f;
    public float Height = 100f;
    public string Color = "#000000AA";
}

/// <summary>Botão clicável (mouse no Windows, toque no Android via InputManager.SetPointer).
/// <summary>
/// Campo de texto digitável — o que faltava pra montar "entrar por IP" só com a UI da engine.
/// <para>Estado de um frame só (<see cref="Submitted"/>) é lido igual ao
/// <see cref="UiButton.Clicked"/>: consulte no Update do seu jogo.</para>
/// </summary>
public sealed class UiTextInput : UiElement
{
    public float Width = 200f;
    public float Height = 32f;

    /// <summary>Conteúdo atual. Escreva aqui pra preencher o campo por código.</summary>
    public string Text = "";

    /// <summary>
    /// Nome da variável de texto do <see cref="GameState"/> espelhada por este campo. Vazio
    /// (padrão) = campo solto, lido só por código via <see cref="Text"/> — o comportamento de
    /// sempre.
    ///
    /// <para>Preenchido, o que o jogador digita vira variável a cada frame, e a variável de volta
    /// pro campo quando muda de fora (carregar um save). Escrever só no Enter seria a armadilha
    /// óbvia: quase toda tela confirma por botão, e o valor se perderia sem erro nenhum.</para>
    ///
    /// <para>A partir daí o valor está em toda parte: <c>{NomeVariavel}</c> num UiText, condição
    /// If do tipo Text, e dentro do save.</para>
    /// </summary>
    public string Variable = "";

    // Último valor que passou pela sincronização, pra saber de que lado veio a mudança.
    internal string LastSynced = "";

    // Se a primeira sincronização já aconteceu. O primeiro frame tem regra própria: decide quem
    // manda entre o Text escrito na cena e o valor que já esteja na variável.
    internal bool SyncStarted;

    /// <summary>Mostrado em cinza quando o campo está vazio (ex.: "192.168.0.10").</summary>
    public string Placeholder = "";

    public int MaxLength = 32;

    /// <summary>Caracteres aceitos. Vazio = todos. Para IP, <c>"0123456789."</c> — barrar na
    /// entrada evita a tela de erro depois e é o tipo de coisa que ninguém lembra de validar.</summary>
    public string Allowed = "";

    public string Color = "#20203CFF";
    public string FocusColor = "#2A2A55FF";
    public string TextColor = "#FFFFFFFF";
    public string PlaceholderColor = "#8888A8FF";
    public string CaretColor = "#FFFFFFFF";

    /// <summary>Com o cursor. Só um campo por vez fica focado.</summary>
    public bool Focused;

    /// <summary>Enter foi apertado neste frame com o campo focado.</summary>
    public bool Submitted;
}

/// OnClick usa o mesmo vocabulário de ações do EventTrigger, rodado por
/// <see cref="Aurora.Runtime.Events.EventSystem.RunActions"/> ao clicar/tocar.</summary>
public sealed class UiButton : UiElement
{
    public float Width = 120f;
    public float Height = 32f;
    public string Text = "";
    public string Color = "#3A3860FF";
    public string HoverColor = "#4A4880FF";
    public string PressedColor = "#2A2850FF";
    public string TextColor = "#FFFFFFFF";
    public List<EventAction> OnClick = [];

    /// <summary>Imagem do botão, caminho relativo à pasta de Assets (igual <see cref="UiImage"/>).
    /// Vazio = botão desenhado com Color/HoverColor/PressedColor, como sempre foi; preenchido =
    /// a imagem substitui o retângulo colorido (as três cores passam a ser ignoradas, o Text
    /// continua sendo desenhado por cima). Width/Height em 0 herdam o tamanho da imagem.</summary>
    public string? TexturePath;

    /// <summary>Imagem usada com o mouse em cima. Vazio = usa <see cref="TexturePath"/> clareado.</summary>
    public string? HoverTexturePath;

    /// <summary>Imagem usada enquanto o botão está apertado. Vazio = usa <see cref="TexturePath"/>
    /// escurecido.</summary>
    public string? PressedTexturePath;

    // Texturas resolvidas no Load (não serializadas) — ver UIManager.BuildButton.
    internal Texture2D? Texture;
    internal Texture2D? HoverTexture;
    internal Texture2D? PressedTexture;

    // Estado de runtime (não serializado) — atualizado por UIManager.Update.
    internal bool Hovered;
    internal int? OwnerTouchId;

    /// <summary>True só no frame do clique/toque — pra jogo reagir sem precisar do vocabulário
    /// genérico de EventAction (ex: chamar um método específico de um script). Lido em código:
    /// <c>if (UI.Find&lt;UiButton&gt;("hud", "BotaoAtk")?.Clicked == true) ...</c></summary>
    public bool Clicked;

    /// <summary>True enquanto o dedo/mouse continua sobre o botão (segurado) — diferente de
    /// <see cref="Clicked"/>, que só é true no frame do toque. Pra controles tipo "segura pra
    /// acelerar": <c>if (UI.Find&lt;UiButton&gt;("Hud", "Gas")?.Pressed == true) ...</c></summary>
    public bool Pressed;
}

/// <summary>Joystick virtual (toque multi-dedo no Android; clique-e-arraste no desktop) — base
/// fixa em (X,Y)/Anchor, direção lida em <see cref="Value"/> a cada frame. Convive com
/// UiButton/outro UiJoystick tocado por outro dedo ao mesmo tempo (UIManager.Update dá dono
/// por id de toque). Não dispara OnClick — é estado contínuo, não um clique único.</summary>
public sealed class UiJoystick : UiElement
{
    public float Radius = 70f;
    public string BaseColor = "#FFFFFF2E";
    public string KnobColor = "#FFFFFF66";

    /// <summary>Direção normalizada * intensidade (0..1) — leia a cada frame no script do
    /// player. Vetor zero quando ninguém está tocando o joystick.</summary>
    public Vector2 Value;

    // Estado de runtime (não serializado) — atualizado por UIManager.Update.
    internal int? OwnerTouchId;
    internal Vector2 KnobOffset;
}
