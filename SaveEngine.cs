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
        if (count < 0 || count > Capacity) throw new InvalidDataException("Invalid driver count.");
        // A legitimate fresh Eden save can have zero driver records even after Free Roam.
        // Permit inspection, but do not initialize an unknown driver structure.
        var ids = new HashSet<uint>();
        for (int i = 0; i < count; i++) ids.Add(Read(bytes, offset + 4 * i));
        foreach (var d in Drivers) d.Selected = ids.Contains(d.Id);
        loaded = bytes; path = file; Format = kind; LoadedCount = count;
    }
    // Experimental: prefix the 32 default hashes from the natural low-progress Switch save.
    // Only the Eden hash list and its assumed count are modified; ownership/state are untouched.
    private static readonly uint[] SwitchStarterHashes = new uint[]
    {
        0xD5330F41u, 0x0613A431u, 0x86957C49u, 0x13F70995u,
        0xF172BA74u, 0x8B6AAE56u, 0xE4C5FCBBu, 0x94FD556Eu,
        0x5F29E03Bu, 0xE32D72BEu, 0x17C01F52u, 0x6CD48F8Eu,
        0x6E260DC9u, 0xCC803F13u, 0x4EA426A1u, 0xCD071A6Au,
        0xE23BF936u, 0xEEE250FFu, 0xCAAE464Bu, 0x5BFDE09Bu,
        0x2D0E9655u, 0xC353E39Du, 0xCCE156BEu, 0xB0E76CCEu,
        0xEF898557u, 0x987F77F3u, 0x869CBF05u, 0xE3EC94EEu,
        0x5F5ACEF6u, 0x5F72AE5Du, 0x2CB49FA9u, 0xB60EDC0Eu,
    };

    // Normal saves always keep the 32 natural starter hashes at the front.
    // The default roster is not user-toggleable and must never be removed.
    public (int added, int total, string backup) Save()
    {
        if (loaded is null || path is null) throw new InvalidOperationException("Open a save first.");
        if (!File.ReadAllBytes(path).SequenceEqual(loaded))
            throw new IOException("The save changed on disk. Reopen it first.");

        var bytes = (byte[])loaded.Clone();
        int offset = Format == SaveFormat.Eden ? EdenIds : SwitchIds;
        var known = Drivers.Select(d => d.Id).ToHashSet();
        var selected = Drivers.Where(d => d.Selected).Select(d => d.Id).ToHashSet();
        var existing = new List<uint>();
        var existingSet = new HashSet<uint>();
        // Read the populated list rather than trusting the experimental count field.
        for (int i = 0; i < Capacity; i++)
        {
            uint id = Read(bytes, offset + i * 4);
            if (id == 0) break;
            if (!existingSet.Add(id))
                throw new InvalidDataException($"Duplicate driver hash at slot {i}; refusing to rewrite.");
            existing.Add(id);
        }
        if (Format == SaveFormat.Switch)
        {
            // Preserve Switch behavior: append only, never change its existing ordering.
            var appended = Drivers.Where(d => d.Selected && !existingSet.Contains(d.Id))
                                  .Select(d => d.Id).ToList();
            if (existing.Count + appended.Count > Capacity)
                throw new InvalidDataException("Switch driver list is full.");
            for (int i = 0; i < appended.Count; i++)
                Write(bytes, offset + (existing.Count + i) * 4, appended[i]);
            return Commit(bytes, appended.Count, existing.Count + appended.Count);
        }

        var output = new List<uint>(SwitchStarterHashes);
        var seen = SwitchStarterHashes.ToHashSet();
        foreach (uint id in existing)
        {
            // Only remove a known outfit if the user explicitly unchecked it.
            if (known.Contains(id) && !selected.Contains(id)) continue;
            if (seen.Add(id)) output.Add(id);
        }
        int added = 0;
        foreach (var d in Drivers.Where(d => d.Selected))
            if (seen.Add(d.Id)) { output.Add(d.Id); added++; }
        if (output.Count > Capacity)
            throw new InvalidDataException($"Need {output.Count} driver slots, capacity is {Capacity}. Nothing written.");

        // Only rewrite the ID list and its count. The before/after tests showed
        // ownership/state indices do not reliably match ID-list positions.
        // Preserving those arrays avoids corrupting unrelated progression data.
        for (int i = 0; i < Capacity; i++)
            Write(bytes, EdenIds + 4 * i, i < output.Count ? output[i] : 0u);
        Write(bytes, EdenCount, (uint)output.Count);
        // Do not fabricate acquired counts or ownership/state entries.
        return Commit(bytes, added, output.Count);
    }

    private (int added, int total, string backup) Commit(byte[] bytes, int added, int total)
    {
        if (loaded is null || path is null) throw new InvalidOperationException("Open a save first.");
        if (bytes.SequenceEqual(loaded)) return (0, total, "");
        string backup = path + ".bak";
        File.Copy(path, backup, overwrite: true);
        string temp = path + ".mkw.tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        loaded = bytes;
        LoadedCount = total;
        return (added, total, backup);
    }
}
