namespace DonkiePane.Tasks;

public enum TaskImportance
{
    Normal,
    High,
}

/// <summary>
/// Eén taak. De velden zijn afgestemd op Microsoft To Do, zodat een koppeling
/// later geen omzetting van het model vraagt.
/// </summary>
public sealed class TaskItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ListId { get; set; } = "";

    /// <summary>De hoofdtaak als dit een subtaak is (Google Taken kent die); anders <c>null</c>. Wordt ingesprongen getoond.</summary>
    public string? ParentId { get; set; }
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TaskImportance Importance { get; set; }
    public DateOnly? DueDate { get; set; }

    /// <summary>Volgorde binnen de open taken; lager = hoger in de lijst. To Do kent dit niet, dus dit blijft lokaal.</summary>
    public double SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Id van deze taak bij een externe bron (bijv. To Do); <c>null</c> voor lokale taken.</summary>
    public string? ExternalId { get; set; }

    public TaskItem Clone() => (TaskItem)MemberwiseClone();
}
