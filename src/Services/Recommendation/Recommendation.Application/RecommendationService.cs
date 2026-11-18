using LabLend.Contracts;
using Recommendation.Domain;

namespace Recommendation.Application;

public sealed class RecommendationPolicy
{
    public int DailyLimit { get; set; } = 20;
    public int MaxCandidates { get; set; } = 200;
    public int MaxContextBytes { get; set; } = 100_000;
}

public sealed class RecommendationService(IGeminiClient gemini, ICatalogClient catalog, IInventoryAvailabilityClient inventory,
    IRecommendationRepository repository, RecommendationPolicy policy, TimeProvider time) : IRecommendationService
{
    public async Task<RecommendationResponse> Create(Guid userId, CreateRecommendationRequest request, string correlationId, CancellationToken ct)
    {
        Validate(request);
        gemini.EnsureAvailable();
        var normalized = request with { Purpose = request.Purpose.Trim(), Constraints = (request.Constraints ?? []).Select(c => c.Trim()).ToArray() };
        var candidates = await catalog.Candidates(request.CategoryId, ct);
        if (candidates.Count > policy.MaxCandidates) throw ScopeRequired();
        // A byte ceiling bounds the entire serialized context without truncating any Catalog field.
        if (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { request = normalized, candidates }).Length > policy.MaxContextBytes) throw ScopeRequired();
        if (!await repository.TryConsumeDaily(userId, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), policy.DailyLimit, ct))
            throw new RecommendationFailure(429, "RATE_LIMITED", "Bạn đã đạt giới hạn tư vấn trong ngày (UTC). Hãy thử lại ngày mai hoặc xem danh mục.");
        // Empty Catalog is a valid empty result, never a fabricated AI recommendation.
        var output = candidates.Count == 0 ? new GeminiResult("catalog-empty", []) : await gemini.Recommend(normalized, candidates, ct);
        if (output.Recommendations.Count > 6) throw RecommendationFailure.InvalidOutput();
        var allowed = candidates.Where(c => c.IsActive).ToDictionary(c => c.Id);
        var validated = new List<(Guid Id, GeminiItem Item)>();
        foreach (var item in output.Recommendations)
        {
            if (!Guid.TryParse(item.EquipmentTypeId, out var id) || !allowed.ContainsKey(id)
                || string.IsNullOrWhiteSpace(item.Reason) || item.Reason.Length > 2000
                || string.IsNullOrWhiteSpace(item.UsageNotes) || item.UsageNotes.Length > 2000)
                throw RecommendationFailure.InvalidOutput();
            if (validated.All(v => v.Id != id)) validated.Add((id, item));
        }
        // Re-read selected records after the external call: deactivation during inference must not pass.
        var current = new Dictionary<Guid, CatalogEquipment>();
        foreach (var (id, _) in validated)
        {
            var equipment = await catalog.Get(id, ct);
            if (equipment is not { IsActive: true }) throw RecommendationFailure.InvalidOutput();
            current.Add(id, equipment);
        }
        var session = new RecommendationSession(userId, normalized.Purpose, normalized.Constraints!, request.CategoryId, output.ModelUsed, time.GetUtcNow());
        foreach (var (id, item) in validated) session.AddItem(id, item.Reason, item.UsageNotes);
        var response = await Enrich(session, current, correlationId, ct);
        await repository.Save(session, ct);
        return response;
    }

    public async Task<RecommendationResponse> Get(Guid userId, Guid id, string correlationId, CancellationToken ct)
    {
        var session = await repository.Get(id, userId, ct) ?? throw new RecommendationFailure(404, "NOT_FOUND", "Không tìm thấy lần tư vấn.");
        var current = new Dictionary<Guid, CatalogEquipment>();
        foreach (var item in session.Items)
        {
            var equipment = await catalog.Get(item.EquipmentTypeId, ct);
            // Catalog uses soft deletion. If a historical reference disappears, never invent a name/spec.
            if (equipment is null) throw RecommendationFailure.CatalogUnavailable();
            current.Add(equipment.Id, equipment);
        }
        return await Enrich(session, current, correlationId, ct);
    }

    public Task<PagedResponse<RecommendationHistoryResponse>> History(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        if (page is < 1 or > 100_000 || pageSize is < 1 or > 100) throw new RecommendationFailure(400, "VALIDATION_ERROR", "Phân trang không hợp lệ.");
        return repository.History(userId, page, pageSize, ct);
    }

    private async Task<RecommendationResponse> Enrich(RecommendationSession session, IReadOnlyDictionary<Guid, CatalogEquipment> catalogItems, string correlationId, CancellationToken ct)
    {
        var ids = session.Items.Select(i => i.EquipmentTypeId).ToArray();
        var stock = ids.Length == 0 ? [] : await inventory.Check(ids, correlationId, ct);
        if (stock.Count != ids.Length || stock.Select(s => s.EquipmentTypeId).Distinct().Count() != ids.Length
            || stock.Any(s => !ids.Contains(s.EquipmentTypeId) || s.Available < 0 || s.Total < s.Available))
            throw RecommendationFailure.InventoryUnavailable();
        var availability = stock.ToDictionary(s => s.EquipmentTypeId);
        var items = session.Items.OrderBy(i => i.Rank).Select(i =>
        {
            var c = catalogItems[i.EquipmentTypeId]; var a = availability[i.EquipmentTypeId];
            return new RecommendationItemResponse(c.Id, c.Name, c.CategoryName, c.Description, c.Specifications, c.ImageUrl,
                c.IsActive, i.Rank, i.Reason, i.UsageNotes, a.Available, a.Total);
        }).ToArray();
        return new(session.Id, session.Purpose, session.Constraints, session.CategoryId, session.CreatedAt, session.ModelUsed, time.GetUtcNow(), items);
    }

    public static void Validate(CreateRecommendationRequest request)
    {
        if (request.Purpose is null || request.Purpose.Trim().Length is < 10 or > 2000
            || request.Constraints is { Length: > 8 } || (request.Constraints ?? []).Any(c => string.IsNullOrWhiteSpace(c) || c.Length > 300)
            || request.CategoryId == Guid.Empty)
            throw new RecommendationFailure(400, "VALIDATION_ERROR", "Mục đích cần 10–2.000 ký tự; tối đa 8 yêu cầu, mỗi yêu cầu 300 ký tự.");
    }
    public static RecommendationFailure ScopeRequired() => new(400, "VALIDATION_ERROR", "Danh mục vượt giới hạn tư vấn. Hãy chọn một danh mục cụ thể hoặc nhờ phòng lab chia nhỏ danh mục; không có thiết bị nào bị âm thầm bỏ qua.");
}
