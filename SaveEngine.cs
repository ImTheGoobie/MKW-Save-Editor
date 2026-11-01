using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Linq;
namespace MKWWinUI;

public enum SaveFormat { None, Eden, Switch }
public sealed class Driver : INotifyPropertyChanged
{
    public string Actor { get; init; } = "";
    public string Character { get; init; } = "";
    public string OutfitName { get; init; } = "";
    public uint Id { get; init; }
    public string DisplayName => OutfitName;
    private bool selected;
    public bool Selected { get => selected; set { if (selected == value) return; selected = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
public sealed class SaveEngine
{
    const int EdenSize = 850928, SwitchSize = 864156;
    const int EdenIds = 0x29880, EdenCount = 0x5DE18, EdenOwned = 0x22B1C;
    const int EdenState = 0x68C8, EdenAcquired = 0x4B5F0;
    const int SwitchIds = 0x33D34, Capacity = 154;
    private byte[]? loaded;
    private string? path;
    public string? Path => path;
    public SaveFormat Format { get; private set; }
    public int LoadedCount { get; private set; }
    public ObservableCollection<Driver> Drivers { get; } = new();
    public List<string> DefaultActorNames { get; } = new();

    static uint Read(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    static void Write(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);

    public SaveEngine()
    {
        using var catalogStream = typeof(SaveEngine).Assembly.GetManifestResourceStream("MKW.WinUI.drivers.json")
            ?? throw new InvalidOperationException("Embedded driver catalog was not found.");
        using var catalogReader = new StreamReader(catalogStream);
        var json = catalogReader.ReadToEnd();
        using var doc = JsonDocument.Parse(json);
        foreach (var character in doc.RootElement.EnumerateObject())
        {
            var group = character.Value;
            if (group.TryGetProperty("baseActor", out var baseActor))
            {
                var actor = baseActor.GetString()!;
                DefaultActorNames.Add(actor["Driver_".Length..^3]);
            }
            foreach (var outfit in group.GetProperty("outfits").EnumerateArray())
            {
                var actor = outfit.GetProperty("actor").GetString()!;
                if (actor == "Driver_DriverBase") continue;
                Drivers.Add(new Driver
                {
                    Character = character.Name,
                    Actor = actor,
                    OutfitName = outfit.GetProperty("name").GetString()!,
                    Id = Convert.ToUInt32(outfit.GetProperty("id").GetString()![2..], 16)
                });
            }
        }
    }
    public void Open(string file)
    {
        var bytes = File.ReadAllBytes(file);
        SaveFormat kind;
        int count, offset;
        if (bytes.Length == EdenSize)
        {
            kind = SaveFormat.Eden; count = checked((int)Read(bytes, EdenCount)); offset = EdenIds;
        }
        else if (bytes.Length == SwitchSize)
        {
            kind = SaveFormat.Switch; offset = SwitchIds; count = 0;
            while (count < Capacity && Read(bytes, offset + count * 4) != 0) count++;
            // Verify known starter character sequence to avoid interpreting unrelated data.
            if (Read(bytes, offset) != 0xD5330F41 || Read(bytes, offset + 4) != 0x0613A431)
                throw new InvalidDataException("Switch driver array signature did not match.");
        }
        else throw new InvalidDataException($"Unsupported save size: {bytes.Length:N0} bytes.");
        if (count < 1 || count > Capacity) throw new InvalidDataException("Invalid driver count.");
        var ids = new HashSet<uint>();
        for (int i = 0; i < count; i++) ids.Add(Read(bytes, offset + 4 * i));
        foreach (var d in Drivers) d.Selected = ids.Contains(d.Id);
        loaded = bytes; path = file; Format = kind; LoadedCount = count;
    }
    public (int added, int total, string backup) Save()
    {
        if (loaded is null || path is null) throw new InvalidOperationException("Open a save first.");
        if (!File.ReadAllBytes(path).SequenceEqual(loaded))
            throw new IOException("The save changed on disk. Reopen it first.");
        var bytes = (byte[])loaded.Clone();
        int offset = Format == SaveFormat.Eden ? EdenIds : SwitchIds;
        var known = Drivers.Select(d => d.Id).ToHashSet();
        var selected = Drivers.Where(d => d.Selected).Select(d => d.Id).ToHashSet();
        var records = new List<(uint id, uint owned, uint state)>();
        var seen = new HashSet<uint>();
        for (int i = 0; i < LoadedCount; i++)
        {
            var id = Read(bytes, offset + 4 * i);
            if (known.Contains(id) && !selected.Contains(id)) continue;
            if (!seen.Add(id)) continue;
            records.Add((id, Format == SaveFormat.Eden ? Read(bytes, EdenOwned + i * 4) : 0,
                Format == SaveFormat.Eden ? Read(bytes, EdenState + i * 4) : 0));
        }
        int added = 0;
        foreach (var d in Drivers.Where(d => d.Selected))
            if (seen.Add(d.Id)) { records.Add((d.Id, 1, 1)); added++; }
        if (records.Count > Capacity) throw new InvalidDataException("Driver list is full.");
        if (Format == SaveFormat.Switch)
        {
            // We know the Switch driver-ID array, but not its count/ownership metadata.
            // Never remove or reorder existing entries in this format.
            if (records.Count < LoadedCount ||
                Enumerable.Range(0, LoadedCount).Any(i => records[i].id != Read(bytes, offset + i * 4)))
                throw new InvalidOperationException(
                    "Switch removal/reordering is disabled until its metadata is decoded.");
            for (int i = LoadedCount; i < records.Count; i++)
            {
                if (Read(bytes, offset + i * 4) != 0)
                    throw new InvalidDataException("Switch array has no empty slot.");
                Write(bytes, offset + i * 4, records[i].id);
            }
        }
        else
        {
            for (int i = 0; i < Math.Max(LoadedCount, records.Count); i++)
            {
                Write(bytes, offset + i * 4, 0);
                Write(bytes, EdenOwned + i * 4, 0);
                Write(bytes, EdenState + i * 4, 0);
            }
            for (int i = 0; i < records.Count; i++)
            {
                Write(bytes, offset + i * 4, records[i].id);
                Write(bytes, EdenOwned + i * 4, records[i].owned);
                Write(bytes, EdenState + i * 4, records[i].state);
            }
            Write(bytes, EdenCount, (uint)records.Count);
            Write(bytes, EdenAcquired, checked(Read(bytes, EdenAcquired) + (uint)added));
        }
        if (bytes.SequenceEqual(loaded)) return (0, records.Count, "");
        var backup = path + ".bak";
        File.Copy(path, backup, overwrite: true);
        var temporary = path + ".mkw.tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        loaded = bytes; LoadedCount = records.Count;
        return (added, records.Count, backup);
    }
}
