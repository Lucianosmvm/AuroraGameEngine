using Aurora.SlandSurvivor.Items;

namespace Aurora.SlandSurvivor.Saves;

/// <summary>Tudo que precisa ser guardado para o mundo voltar exatamente como estava.</summary>
public sealed class SaveData
{
    public int Seed;
    public int Width;
    public int Height;
    public int[] Foreground = [];
    public int[] Background = [];

    public float PlayerX;
    public float PlayerY;
    public float Health;
    public float Clock;
    public int Day = 1;

    public int[] Items = [];
    public int[] Counts = [];
    public int SelectedSlot;
}

/// <summary>
/// Gravação do mundo em arquivo binário. As duas camadas de tiles vão comprimidas por
/// repetição (RLE): um mundo de 1200x300 tem 360 mil células, mas quase todas em sequências
/// longas do mesmo tile, então o arquivo fica na casa de dezenas de KB em vez de 3 MB.
/// </summary>
public static class WorldSave
{
    private const uint Magic = 0x444E4C53;   // "SLND"
    private const int Version = 1;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SlandSurvivor", "world.dat");

    public static bool Exists(string? path = null) => File.Exists(path ?? DefaultPath);

    public static void Save(SaveData data, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(data.Seed);
        writer.Write(data.Width);
        writer.Write(data.Height);

        writer.Write(data.PlayerX);
        writer.Write(data.PlayerY);
        writer.Write(data.Health);
        writer.Write(data.Clock);
        writer.Write(data.Day);
        writer.Write(data.SelectedSlot);

        writer.Write(data.Items.Length);
        for (int i = 0; i < data.Items.Length; i++)
        {
            writer.Write(data.Items[i]);
            writer.Write(data.Counts[i]);
        }

        WriteRle(writer, data.Foreground);
        WriteRle(writer, data.Background);
    }

    /// <summary>Lê o save; devolve null se o arquivo não existir ou não for deste jogo/versão.</summary>
    public static SaveData? Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                return null;

            var data = new SaveData
            {
                Seed = reader.ReadInt32(),
                Width = reader.ReadInt32(),
                Height = reader.ReadInt32(),
                PlayerX = reader.ReadSingle(),
                PlayerY = reader.ReadSingle(),
                Health = reader.ReadSingle(),
                Clock = reader.ReadSingle(),
                Day = reader.ReadInt32(),
                SelectedSlot = reader.ReadInt32(),
            };

            int slots = reader.ReadInt32();
            data.Items = new int[slots];
            data.Counts = new int[slots];
            for (int i = 0; i < slots; i++)
            {
                data.Items[i] = reader.ReadInt32();
                data.Counts[i] = reader.ReadInt32();
            }

            int cells = data.Width * data.Height;
            data.Foreground = ReadRle(reader, cells);
            data.Background = ReadRle(reader, cells);
            return data;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or InvalidDataException)
        {
            Console.Error.WriteLine($"[save] arquivo ilegível, começando mundo novo: {ex.Message}");
            return null;
        }
    }

    public static SaveData FromInventory(Inventory inventory)
    {
        var data = new SaveData
        {
            Items = new int[Inventory.TotalSlots],
            Counts = new int[Inventory.TotalSlots],
            SelectedSlot = inventory.Selected,
        };

        for (int i = 0; i < Inventory.TotalSlots; i++)
        {
            data.Items[i] = inventory.Slots[i].Item;
            data.Counts[i] = inventory.Slots[i].Count;
        }

        return data;
    }

    public static void ApplyInventory(SaveData data, Inventory inventory)
    {
        inventory.Clear();

        for (int i = 0; i < data.Items.Length && i < Inventory.TotalSlots; i++)
        {
            inventory.Slots[i] = new Inventory.Slot
            {
                Item = data.Items[i],
                Count = data.Counts[i],
            };
        }

        inventory.Selected = data.SelectedSlot;
    }

    private static void WriteRle(BinaryWriter writer, int[] values)
    {
        long lengthPosition = writer.BaseStream.Position;
        writer.Write(0);                                   // reservado: número de sequências

        int runs = 0;
        int index = 0;

        while (index < values.Length)
        {
            int value = values[index];
            int run = 1;

            while (index + run < values.Length && values[index + run] == value && run < int.MaxValue)
                run++;

            writer.Write(value);
            writer.Write(run);
            runs++;
            index += run;
        }

        long end = writer.BaseStream.Position;
        writer.BaseStream.Position = lengthPosition;
        writer.Write(runs);
        writer.BaseStream.Position = end;
    }

    private static int[] ReadRle(BinaryReader reader, int expectedCells)
    {
        int runs = reader.ReadInt32();
        var values = new int[expectedCells];
        int index = 0;

        for (int i = 0; i < runs; i++)
        {
            int value = reader.ReadInt32();
            int run = reader.ReadInt32();

            for (int j = 0; j < run && index < values.Length; j++)
                values[index++] = value;
        }

        if (index != expectedCells)
            throw new InvalidDataException($"save truncado: {index} de {expectedCells} células.");

        return values;
    }
}
