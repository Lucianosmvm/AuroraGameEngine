using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aurora.Runtime.Graphics;

/// <summary>Um recorte livre no sprite sheet, em pixels da imagem. Só existe quando a folha
/// NÃO é uma grade regular (personagem com frames de larguras diferentes, tileset montado à
/// mão). Quando a lista de recortes está vazia, o índice do frame é resolvido pela grade.</summary>
public sealed class SpriteSheetFrame
{
    public int X;
    public int Y;
    public int Width;
    public int Height;
}

/// <summary>Uma animação da folha: os índices de frame, quanto dura cada um e se repete.</summary>
public sealed class SpriteSheetClip
{
    public string Name = "";
    public int[] Frames = [];
    public float Duration = 0.1f;
    public bool Loop = true;
}

/// <summary>
/// Uma folha de sprites recortada — o arquivo <c>Assets/spritesheets/*.sheet.json</c> que o
/// editor de sprite sheet grava e o <see cref="Ecs.Components.Animator"/> lê.
///
/// <para>O ponto de existir um arquivo em vez de repetir os números em cada cena: o recorte é
/// propriedade da IMAGEM, não da entidade. Dez inimigos que usam a mesma folha compartilham o
/// mesmo recorte e as mesmas animações; corrigir a altura do frame conserta os dez de uma vez.
/// A cena continua podendo sobrescrever qualquer campo — quem grava o Animator na cena manda.</para>
///
/// <para>Só depende de System.Text.Json de propósito: o editor compila este mesmo arquivo por
/// link (ver Aurora.Editor.csproj), então editor e jogo leem exatamente o mesmo formato.</para>
/// </summary>
public sealed class SpriteSheetAsset
{
    /// <summary>Caminho da imagem, relativo à raiz de assets (ex.: "sprites/player.png").</summary>
    public string Texture = "";

    public int FrameWidth;
    public int FrameHeight;

    /// <summary>Quantas colunas de frames a grade tem. Usado pra converter índice em coluna/linha.</summary>
    public int Columns = 1;

    /// <summary>Quantas linhas a grade tem. A engine não precisa dela pra animar (o índice já
    /// diz tudo), mas o editor usa pra desenhar a grade e pra saber quantos frames existem.</summary>
    public int Rows = 1;

    /// <summary>Borda vazia antes do primeiro frame, em pixels.</summary>
    public int MarginX;
    public int MarginY;

    /// <summary>Vão entre frames vizinhos, em pixels — comum em folhas exportadas por atlas.</summary>
    public int SpacingX;
    public int SpacingY;

    /// <summary>Recortes livres. Vazio = grade regular (o caso normal).</summary>
    public List<SpriteSheetFrame> Frames = [];

    public List<SpriteSheetClip> Clips = [];

    public bool IsFreeCut => Frames.Count > 0;

    /// <summary>Quantos frames a folha tem — o total do recorte livre, ou colunas × linhas.</summary>
    public int FrameCount => IsFreeCut ? Frames.Count : Math.Max(0, Columns) * Math.Max(0, Rows);

    /// <summary>Retângulo do frame de índice <paramref name="index"/> na imagem, ou null se o
    /// índice não existe (clipe que sobrou de um recorte maior, por exemplo).</summary>
    public RectF? FrameRect(int index)
    {
        if (IsFreeCut)
        {
            if (index < 0 || index >= Frames.Count)
                return null;
            var f = Frames[index];
            return new RectF(f.X, f.Y, f.Width, f.Height);
        }

        return GridRect(index, Columns, FrameWidth, FrameHeight, MarginX, MarginY, SpacingX, SpacingY);
    }

    /// <summary>
    /// Índice da grade → retângulo na imagem. Estático porque a mesma conta é feita em três
    /// lugares — a folha, o Animator em runtime e a grade desenhada no editor — e três cópias
    /// dela é como se chega no editor mostrando um recorte e o jogo desenhando outro.
    /// </summary>
    public static RectF? GridRect(int index, int columns, int frameWidth, int frameHeight,
        int marginX = 0, int marginY = 0, int spacingX = 0, int spacingY = 0)
    {
        if (index < 0 || columns <= 0 || frameWidth <= 0 || frameHeight <= 0)
            return null;

        int col = index % columns;
        int row = index / columns;
        return new RectF(
            marginX + col * (frameWidth + spacingX),
            marginY + row * (frameHeight + spacingY),
            frameWidth,
            frameHeight);
    }

    /// <summary>
    /// O caminho de volta: pixel da imagem → índice do frame. -1 quando o ponto caiu na margem
    /// ou no vão entre frames — clicar ali no editor não deve marcar o vizinho de tabela.
    /// </summary>
    public static int GridIndexAt(int px, int py, int columns, int rows, int frameWidth, int frameHeight,
        int marginX = 0, int marginY = 0, int spacingX = 0, int spacingY = 0)
    {
        if (columns <= 0 || rows <= 0 || frameWidth <= 0 || frameHeight <= 0)
            return -1;

        int localX = px - marginX;
        int localY = py - marginY;
        if (localX < 0 || localY < 0)
            return -1;

        int cellW = frameWidth + spacingX;
        int cellH = frameHeight + spacingY;

        if (localX % cellW >= frameWidth || localY % cellH >= frameHeight)
            return -1;

        int col = localX / cellW;
        int row = localY / cellH;
        if (col >= columns || row >= rows)
            return -1;

        return row * columns + col;
    }

    /// <summary>Quantas células cabem numa dimensão da imagem, dado o tamanho do frame. É a
    /// conta que o editor faz quando o autor digita "32×32" e quer saber a grade que sai.</summary>
    public static int FitCount(int imageSize, int frameSize, int margin, int spacing)
        => frameSize <= 0 ? 1 : Math.Max(1, (imageSize - margin + spacing) / (frameSize + spacing));

    /// <summary>O inverso: o tamanho de cada célula quando o autor diz quantas quer.</summary>
    public static int FitSize(int imageSize, int count, int margin, int spacing)
        => count <= 0 ? 1 : Math.Max(1, (imageSize - margin - spacing * (count - 1)) / count);

    /// <summary>Lê uma folha. Arquivo corrompido devolve folha vazia em vez de exceção: uma
    /// folha ilegível deixa a entidade sem animação, e derrubar a cena inteira por causa disso
    /// seria pior do que o problema.</summary>
    public static SpriteSheetAsset FromJson(string json)
    {
        var sheet = new SpriteSheetAsset();

        JsonNode? parsed;
        try { parsed = JsonNode.Parse(json); }
        catch (JsonException) { return sheet; }

        if (parsed is not JsonObject root)
            return sheet;

        sheet.Texture = Str(root, "Texture");
        sheet.FrameWidth = Num(root, "FrameWidth");
        sheet.FrameHeight = Num(root, "FrameHeight");
        sheet.Columns = Num(root, "Columns", 1);
        sheet.Rows = Num(root, "Rows", 1);
        sheet.MarginX = Num(root, "MarginX");
        sheet.MarginY = Num(root, "MarginY");
        sheet.SpacingX = Num(root, "SpacingX");
        sheet.SpacingY = Num(root, "SpacingY");

        if (root["Frames"] is JsonArray frames)
        {
            foreach (var item in frames.OfType<JsonObject>())
            {
                sheet.Frames.Add(new SpriteSheetFrame
                {
                    X = Num(item, "X"),
                    Y = Num(item, "Y"),
                    Width = Num(item, "Width"),
                    Height = Num(item, "Height"),
                });
            }
        }

        if (root["Clips"] is JsonArray clips)
        {
            foreach (var item in clips.OfType<JsonObject>())
            {
                sheet.Clips.Add(new SpriteSheetClip
                {
                    Name = Str(item, "Name"),
                    Duration = Dec(item, "Duration", 0.1f),
                    Loop = item["Loop"]?.GetValue<bool>() ?? true,
                    Frames = item["Frames"] is JsonArray f
                        ? f.Select(n => (int)Math.Round(n?.GetValue<double>() ?? 0)).ToArray()
                        : [],
                });
            }
        }

        return sheet;
    }

    public string ToJson()
    {
        var root = new JsonObject
        {
            ["Texture"] = Texture,
            ["FrameWidth"] = FrameWidth,
            ["FrameHeight"] = FrameHeight,
            ["Columns"] = Columns,
            ["Rows"] = Rows,
        };

        if (MarginX != 0) root["MarginX"] = MarginX;
        if (MarginY != 0) root["MarginY"] = MarginY;
        if (SpacingX != 0) root["SpacingX"] = SpacingX;
        if (SpacingY != 0) root["SpacingY"] = SpacingY;

        if (Frames.Count > 0)
        {
            var arr = new JsonArray();
            foreach (var f in Frames)
                arr.Add(new JsonObject { ["X"] = f.X, ["Y"] = f.Y, ["Width"] = f.Width, ["Height"] = f.Height });
            root["Frames"] = arr;
        }

        var clipArray = new JsonArray();
        foreach (var clip in Clips)
        {
            var node = new JsonObject
            {
                ["Name"] = clip.Name,
                ["Duration"] = clip.Duration,
            };
            if (!clip.Loop) node["Loop"] = false;

            var frames = new JsonArray();
            foreach (int i in clip.Frames) frames.Add(i);
            node["Frames"] = frames;

            clipArray.Add(node);
        }
        root["Clips"] = clipArray;

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Str(JsonObject o, string key) => o[key]?.GetValue<string>() ?? "";

    private static int Num(JsonObject o, string key, int fallback = 0)
        => o[key] is { } n && n.AsValue().TryGetValue(out double d) ? (int)Math.Round(d) : fallback;

    private static float Dec(JsonObject o, string key, float fallback)
        => o[key] is { } n && n.AsValue().TryGetValue(out double d) ? (float)d : fallback;
}
