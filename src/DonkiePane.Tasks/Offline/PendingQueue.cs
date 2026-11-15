using DonkiePane.Core;

namespace DonkiePane.Tasks.Offline;

/// <summary>Eén wijziging die nog naar de bron moet.</summary>
public sealed class PendingOperation
{
    public const string Add = "add";
    public const string Update = "update";
    public const string Delete = "delete";

    public string Kind { get; set; } = "";
    public string ListId { get; set; } = "";

    /// <summary>De taak waar het om gaat. Bij een nieuwe taak is dit een tijdelijk id (<c>queued-…</c>) tot de bron er een gegeven heeft.</summary>
    public string TaskId { get; set; } = "";

    /// <summary>De gewenste staat van de taak (bij toevoegen en wijzigen).</summary>
    public TaskItem? Task { get; set; }

    public DateTimeOffset QueuedAt { get; set; }
}

public sealed class PendingQueueData
{
    public int Version { get; set; } = 1;
    public List<PendingOperation> Operations { get; set; } = [];
}

/// <summary>De wachtrij van één bron, bewaard in een bestand zodat wijzigingen een herstart overleven.</summary>
public sealed class PendingQueueStore(string filePath) : JsonFileStore<PendingQueueData>(filePath);
