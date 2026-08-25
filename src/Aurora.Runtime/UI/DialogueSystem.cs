using System.Numerics;
using Aurora.Runtime.Assets;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.UI;

public abstract record DialogueEntry;

/// <summary>Mensagem simples; avança com <see cref="DialogueSystem.Advance"/>. <paramref name="Portrait"/>
/// é o caminho de uma textura (relativo a Assets) desenhada ao lado do texto — null/vazio = sem retrato,
/// a caixa fica do jeito de sempre.</summary>
public sealed record DialogueMessage(string Text, string? Speaker, string? Portrait = null) : DialogueEntry;

/// <summary>Escolha; navegue com SelectNext/Previous e confirme com Advance.</summary>
public sealed record DialogueChoice(string Prompt, IReadOnlyList<string> Options, Action<int> OnChosen) : DialogueEntry;

/// <summary>
/// Fila de diálogos com caixa desenhada na base da tela. Enquanto <see cref="IsActive"/>,
/// o EventSystem segura a sequência de ações e o jogo deve travar o movimento do jogador.
/// O jogo mapeia o input: Advance() para avançar/confirmar, SelectNext/Previous nas escolhas.
/// </summary>
public sealed class DialogueSystem
{
    private const float Padding = 16f;

    /// <summary>Lado do retrato, em pixels — quadrado, a caixa cresce pra caber.</summary>
    private const float PortraitSize = 96f;

    /// <summary>
    /// Catálogo de texturas, pro retrato do <see cref="DialogueMessage"/>. Propriedade e não
    /// parâmetro de <see cref="Draw"/> de propósito: a assinatura do Draw está escrita à mão no
    /// Game.cs de todo jogo já gerado por este editor (<c>Dialogue.Draw(SpriteBatch, _font,
    /// ScreenSize.X, ScreenSize.Y)</c>), e mexer nela quebraria todos eles pra ganhar nada. O
    /// Game (motor) é quem seta isto sozinho — ver Game.HandleLoad.
    /// </summary>
    public AssetManager? Assets { get; set; }

    /// <summary>Recuo do marcador "» " das opções — as linhas de continuação de uma opção
    /// quebrada não são recuadas, mas a largura disponível já desconta o marcador.</summary>
    private const string OptionMarker = "» ";

    private readonly Queue<DialogueEntry> _queue = new();

    // Layout já quebrado em linhas, refeito só quando muda a entrada, a fonte ou a largura da
    // caixa. Sem isso a quebra rodaria a cada frame em cima do mesmo texto — a caixa de diálogo
    // fica na tela por segundos.
    private DialogueEntry? _layoutFor;
    private Font? _layoutFont;
    private float _layoutWidth = -1f;
    private string _layoutBody = "";
    private readonly List<string> _layoutOptions = [];

    public DialogueEntry? Current { get; private set; }
    public int SelectedIndex { get; private set; }

    public bool IsActive => Current is not null || _queue.Count > 0;

    /// <summary>Descarta todos os diálogos pendentes. Chamado ao trocar de cena.</summary>
    public void Clear()
    {
        _queue.Clear();
        Current = null;
        SelectedIndex = 0;
        InvalidateLayout();
    }

    private void InvalidateLayout()
    {
        _layoutFor = null;
        _layoutFont = null;
        _layoutWidth = -1f;
        _layoutBody = "";
        _layoutOptions.Clear();
    }

    public void ShowMessage(string text, string? speaker = null, string? portrait = null)
        => _queue.Enqueue(new DialogueMessage(text, speaker, portrait));

    public void ShowChoice(string prompt, IReadOnlyList<string> options, Action<int> onChosen)
        => _queue.Enqueue(new DialogueChoice(prompt, options, onChosen));

    /// <summary>Chamado pela engine a cada frame: promove o próximo item da fila.</summary>
    public void Update()
    {
        if (Current is null && _queue.Count > 0)
        {
            Current = _queue.Dequeue();
            SelectedIndex = 0;
        }
    }

    /// <summary>Dispensa a mensagem atual ou confirma a opção selecionada.</summary>
    public void Advance()
    {
        switch (Current)
        {
            case DialogueMessage:
                Current = null;
                break;

            case DialogueChoice choice:
                Current = null;
                choice.OnChosen(SelectedIndex);
                break;
        }
    }

    public void SelectNext()
    {
        if (Current is DialogueChoice choice)
            SelectedIndex = (SelectedIndex + 1) % choice.Options.Count;
    }

    public void SelectPrevious()
    {
        if (Current is DialogueChoice choice)
            SelectedIndex = (SelectedIndex - 1 + choice.Options.Count) % choice.Options.Count;
    }

    /// <summary>Desenha a caixa de diálogo (chame no passe de UI).</summary>
    public void Draw(SpriteBatch batch, Font font, float screenWidth, float screenHeight)
    {
        if (Current is null)
            return;

        var background = new Color(0.06f, 0.05f, 0.12f, 0.92f);
        var accent = Color.FromBytes(120, 110, 200);
        var speakerColor = Color.FromBytes(251, 242, 54);

        string? speaker = (Current as DialogueMessage)?.Speaker;
        string? portraitPath = (Current as DialogueMessage)?.Portrait;
        var portrait = portraitPath is { Length: > 0 } && Assets is not null ? Assets.LoadTexture(portraitPath) : null;

        // Retrato empurra o texto pra direita — a largura da caixa não muda por causa dele
        // (senão a caixa "pularia" de tamanho entre uma fala com retrato e outra sem).
        float textOffsetX = portrait is not null ? PortraitSize + Padding : 0f;

        float boxWidth = MathF.Min(screenWidth * 0.85f, 720f);
        EnsureLayout(font, boxWidth - textOffsetX);

        float textHeight = font.MeasureText(_layoutBody).Y
            + (speaker is not null ? font.LineHeight : 0f);
        foreach (string option in _layoutOptions)
            textHeight += font.MeasureText(option).Y;

        float boxHeight = MathF.Max(textHeight, portrait is not null ? PortraitSize : 0f) + Padding * 2f + 8f;

        var boxPosition = new Vector2((screenWidth - boxWidth) / 2f, screenHeight - boxHeight - 20f);

        batch.DrawRect(boxPosition, new Vector2(boxWidth, boxHeight), background);
        batch.DrawRect(boxPosition, new Vector2(boxWidth, 2f), accent);

        if (portrait is not null)
        {
            // "Contain": encolhe preservando proporção em vez de esticar num quadrado — um
            // retrato retangular (rosto num busto vertical) não pode virar rosto largo.
            float portraitScale = MathF.Min(PortraitSize / portrait.Width, PortraitSize / portrait.Height);
            var portraitDrawSize = new Vector2(portrait.Width * portraitScale, portrait.Height * portraitScale);
            var portraitPosition = boxPosition + new Vector2(
                Padding + (PortraitSize - portraitDrawSize.X) / 2f,
                (boxHeight - PortraitSize) / 2f + (PortraitSize - portraitDrawSize.Y) / 2f);
            batch.Draw(portrait, portraitPosition, portraitDrawSize, Vector2.Zero, 0f, Color.White);
        }

        var pen = boxPosition + new Vector2(Padding + textOffsetX, Padding);

        if (speaker is not null)
        {
            font.Draw(batch, speaker, pen, speakerColor);
            pen.Y += font.LineHeight;
        }

        font.Draw(batch, _layoutBody, pen, Color.White);
        pen.Y += font.MeasureText(_layoutBody).Y + 4f;

        if (_layoutOptions.Count > 0)
        {
            for (int i = 0; i < _layoutOptions.Count; i++)
            {
                bool selected = i == SelectedIndex;
                float optionHeight = font.MeasureText(_layoutOptions[i]).Y;

                if (selected)
                {
                    batch.DrawRect(new Vector2(boxPosition.X + 6f, pen.Y - 2f),
                        new Vector2(boxWidth - 12f, optionHeight), accent.WithAlpha(0.35f));
                }

                font.Draw(batch, (selected ? OptionMarker : "   ") + _layoutOptions[i],
                    pen, selected ? Color.White : new Color(0.75f, 0.75f, 0.8f));
                pen.Y += optionHeight;
            }
        }
        else
        {
            font.Draw(batch, "»", boxPosition + new Vector2(boxWidth - Padding - 10f, boxHeight - font.LineHeight - 6f),
                accent);
        }
    }

    /// <summary>Recalcula o texto quebrado se a entrada, a fonte ou a largura da caixa mudaram.
    /// A caixa ocupa a largura toda menos as margens; as opções ainda descontam o marcador.</summary>
    private void EnsureLayout(Font font, float boxWidth)
    {
        if (ReferenceEquals(_layoutFor, Current) && ReferenceEquals(_layoutFont, font) && _layoutWidth == boxWidth)
            return;

        _layoutFor = Current;
        _layoutFont = font;
        _layoutWidth = boxWidth;
        _layoutOptions.Clear();

        float textWidth = boxWidth - Padding * 2f;

        string body = Current switch
        {
            DialogueMessage message => message.Text,
            DialogueChoice choice => choice.Prompt,
            _ => "",
        };
        _layoutBody = font.WrapText(body, textWidth);

        if (Current is DialogueChoice { Options: { } options })
        {
            float optionWidth = textWidth - font.MeasureText(OptionMarker).X;
            foreach (string option in options)
                _layoutOptions.Add(font.WrapText(option, optionWidth));
        }
    }
}
