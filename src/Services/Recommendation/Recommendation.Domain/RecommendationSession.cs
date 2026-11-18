namespace Recommendation.Domain;

public sealed class RecommendationSession
{
    private RecommendationSession() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Purpose { get; private set; } = "";
    public string[] Constraints { get; private set; } = [];
    public Guid? CategoryId { get; private set; }
    public string ModelUsed { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public List<RecommendationItem> Items { get; private set; } = [];

    public RecommendationSession(Guid userId, string purpose, string[] constraints, Guid? categoryId, string modelUsed, DateTimeOffset createdAt)
    {
        if (userId == Guid.Empty || purpose.Trim().Length is < 10 or > 2000 || constraints.Length > 8
            || constraints.Any(c => string.IsNullOrWhiteSpace(c) || c.Length > 300)
            || string.IsNullOrWhiteSpace(modelUsed) || modelUsed.Length > 100 || createdAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Invalid recommendation session.");
        Id = Guid.NewGuid(); UserId = userId; Purpose = purpose.Trim(); Constraints = constraints.ToArray();
        CategoryId = categoryId; ModelUsed = modelUsed; CreatedAt = createdAt;
    }

    public void AddItem(Guid equipmentTypeId, string reason, string usageNotes)
    {
        if (Items.Count >= 6 || Items.Any(i => i.EquipmentTypeId == equipmentTypeId))
            throw new ArgumentException("Duplicate or excessive recommendation items.");
        Items.Add(new RecommendationItem(Id, equipmentTypeId, Items.Count + 1, reason, usageNotes));
    }
}

public sealed class RecommendationItem
{
    private RecommendationItem() { }
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid EquipmentTypeId { get; private set; }
    public int Rank { get; private set; }
    public string Reason { get; private set; } = "";
    public string UsageNotes { get; private set; } = "";

    internal RecommendationItem(Guid sessionId, Guid equipmentTypeId, int rank, string reason, string usageNotes)
    {
        if (sessionId == Guid.Empty || equipmentTypeId == Guid.Empty || rank is < 1 or > 6
            || string.IsNullOrWhiteSpace(reason) || reason.Length > 2000 || string.IsNullOrWhiteSpace(usageNotes) || usageNotes.Length > 2000)
            throw new ArgumentException("Invalid recommendation item.");
        Id = Guid.NewGuid(); SessionId = sessionId; EquipmentTypeId = equipmentTypeId;
        Rank = rank; Reason = reason.Trim(); UsageNotes = usageNotes.Trim();
    }
}
