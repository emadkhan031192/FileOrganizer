using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileOrganizer.Core;

/// <summary>
/// Persists every move/copy/rename so recent operations can be reversed (↶ Undo, §11).
/// Stored next to the config as history.json. Capped so the file cannot grow forever.
/// </summary>
public sealed class HistoryService
{
    private const int MaxEntries = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly object _lock = new();
    private readonly List<OperationEntry> _entries;

    public string HistoryPath { get; }

    public HistoryService(string? historyPath = null)
    {
        HistoryPath = historyPath ?? Path.Combine(
            Path.GetDirectoryName(ConfigService.DefaultConfigPath())!, "history.json");
        _entries = LoadFromDisk();
    }

    private List<OperationEntry> LoadFromDisk()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return new List<OperationEntry>();
            return JsonSerializer.Deserialize<List<OperationEntry>>(File.ReadAllText(HistoryPath), JsonOptions)
                   ?? new List<OperationEntry>();
        }
        catch (JsonException) { return new List<OperationEntry>(); }
        catch (IOException) { return new List<OperationEntry>(); }
    }

    private void SaveToDisk()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
        var tmp = HistoryPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, JsonOptions));
        File.Move(tmp, HistoryPath, overwrite: true);
    }

    public void Record(OperationEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
            SaveToDisk();
        }
    }

    public void RecordBatch(string batchId, string kind, IEnumerable<(string Source, string Destination)> pairs)
    {
        lock (_lock)
        {
            foreach (var (source, destination) in pairs)
                _entries.Add(new OperationEntry { BatchId = batchId, Kind = kind, SourcePath = source, DestinationPath = destination });
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
            SaveToDisk();
        }
    }

    /// <summary>Most recent first.</summary>
    public IReadOnlyList<OperationEntry> Recent(int count = 50)
    {
        lock (_lock)
            return _entries.OrderByDescending(e => e.TimestampUtc).Take(count).ToList();
    }

    /// <summary>The newest batch that has not been undone yet, or null.</summary>
    public string? LastUndoableBatchId
    {
        get
        {
            lock (_lock)
                return _entries.Where(e => !e.Undone).OrderByDescending(e => e.TimestampUtc)
                               .Select(e => e.BatchId).FirstOrDefault();
        }
    }

    internal List<OperationEntry> EntriesForBatch(string batchId)
    {
        lock (_lock)
            return _entries.Where(e => e.BatchId == batchId && !e.Undone)
                           .OrderByDescending(e => e.TimestampUtc).ToList();
    }

    internal void MarkUndone(IEnumerable<string> entryIds)
    {
        lock (_lock)
        {
            var ids = entryIds.ToHashSet();
            foreach (var e in _entries.Where(e => ids.Contains(e.Id)))
                e.Undone = true;
            SaveToDisk();
        }
    }
}
