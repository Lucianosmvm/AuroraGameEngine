using System.Numerics;
using Aurora.Runtime.Graphics;

namespace Aurora.Runtime.UI;

public abstract record DialogueEntry;

/// <summary>Mensagem simples; avança com <see cref="DialogueSystem.Advance"/>.</summary>
public sealed record DialogueMessage(string Text, string? Speaker) : DialogueEntry;

/// <summary>Escolha; navegue com SelectNext/Previous e confirme com Advance.</summary>
public sealed record DialogueChoice(string Prompt, IReadOnlyList<string> Options, Action<int> OnChosen) : DialogueEntry;

/// <summary>
/// Fila de diálogos com caixa desenhada na base da tela. Enquanto <see cref="IsActive"/>,
/// o EventSystem segura a sequência de ações e o jogo deve travar o movimento do jogador.
/// O jogo mapeia o input: Advance() para avançar/confirmar, SelectNext/Previous nas escolhas.
/// </summary>
public sealed class DialogueSystem
{
    private readonly Queue<DialogueEntry> _queue = new();

    public DialogueEntry? Current { get; private set; }
    public int SelectedIndex { get; private set; }

    public bool IsActive => Current is not null || _queue.Count > 0;

    public void ShowMessage(string text, string? speaker = null)
        => _queue.Enqueue(new DialogueMessage(text, speaker));

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

        const float padding = 16f;
        var background = new Color(0.06f, 0.05f, 0.12f, 0.92f);
        var accent = Color.FromBytes(120, 110, 200);
        var speakerColor = Color.FromBytes(251, 242, 54);

        string body = Current switch
        {
            DialogueMessage message => message.Text,
            DialogueChoice choice => choice.Prompt,
            _ => "",
        };
        string? speaker = (Current as DialogueMessage)?.Speaker;
        var options = (Current as DialogueChoice)?.Options;

        float boxWidth = MathF.Min(screenWidth * 0.85f, 720f);
        float textHeight = font.MeasureText(body).Y
            + (speaker is not null ? font.LineHeight : 0f)
            + (options?.Count ?? 0) * font.LineHeight;
        float boxHeight = textHeight + padding * 2f + 8f;

        var boxPosition = new Vector2((screenWidth - boxWidth) / 2f, screenHeight - boxHeight - 20f);

        batch.DrawRect(boxPosition, new Vector2(boxWidth, boxHeight), background);
        batch.DrawRect(boxPosition, new Vector2(boxWidth, 2f), accent);

        var pen = boxPosition + new Vector2(padding, padding);

        if (speaker is not null)
        {
            font.Draw(batch, speaker, pen, speakerColor);
            pen.Y += font.LineHeight;
        }

        font.Draw(batch, body, pen, Color.White);
        pen.Y += font.MeasureText(body).Y + 4f;

        if (options is not null)
        {
            for (int i = 0; i < options.Count; i++)
            {
                bool selected = i == SelectedIndex;
                if (selected)
                {
                    batch.DrawRect(new Vector2(boxPosition.X + 6f, pen.Y - 2f),
                        new Vector2(boxWidth - 12f, font.LineHeight), accent.WithAlpha(0.35f));
                }

                font.Draw(batch, (selected ? "» " : "   ") + options[i],
                    pen, selected ? Color.White : new Color(0.75f, 0.75f, 0.8f));
                pen.Y += font.LineHeight;
            }
        }
        else
        {
            font.Draw(batch, "»", boxPosition + new Vector2(boxWidth - padding - 10f, boxHeight - font.LineHeight - 6f),
                accent);
        }
    }
}
