using LabLend.Contracts;
using Recommendation.Domain;

namespace Recommendation.Application;

public sealed record CreateRecommendationRequest(string Purpose, string[]? Constraints = null, Guid? CategoryId = null);
public sealed record CatalogEquipment(Guid Id, Guid CategoryId, string CategoryName, string Name, string? Description,
    string? Specifications, string[] Tags, string? ImageUrl, bool IsActive);
public sealed record GeminiItem(string EquipmentTypeId, string Reason, string UsageNotes);
public sealed record GeminiResult(string ModelUsed, IReadOnlyList<GeminiItem> Recommendations);
public sealed record Availability(Guid EquipmentTypeId, int Available, int Total);
public sealed record RecommendationItemResponse(Guid EquipmentTypeId, string EquipmentName, string CategoryName,
    string? Description, string? Specifications, string? ImageUrl, bool IsActive, int Rank, string Reason, string UsageNotes,
    int Available, int Total);
public sealed record RecommendationResponse(Guid Id, string Purpose, string[] Constraints, Guid? CategoryId,
    DateTimeOffset CreatedAt, string ModelUsed, DateTimeOffset AvailabilityCheckedAt,
    IReadOnlyList<RecommendationItemResponse> Recommendations);
public sealed record RecommendationHistoryResponse(Guid Id, string Purpose, DateTimeOffset CreatedAt, string ModelUsed, int ItemCount);

public interface IGeminiClient
{
    void EnsureAvailable();
    Task<GeminiResult> Recommend(CreateRecommendationRequest request, IReadOnlyList<CatalogEquipment> candidates, CancellationToken ct);
}
public interface ICatalogClient
{
    Task<IReadOnlyList<CatalogEquipment>> Candidates(Guid? categoryId, CancellationToken ct);
    Task<CatalogEquipment?> Get(Guid id, CancellationToken ct);
}
public interface IInventoryAvailabilityClient
{
    Task<IReadOnlyList<Availability>> Check(IReadOnlyList<Guid> ids, string correlationId, CancellationToken ct);
}
public interface IRecommendationRepository
{
    Task Save(RecommendationSession session, CancellationToken ct);
    Task<RecommendationSession?> Get(Guid id, Guid userId, CancellationToken ct);
    Task<PagedResponse<RecommendationHistoryResponse>> History(Guid userId, int page, int pageSize, CancellationToken ct);
    // Counts attempts, including failed provider calls; atomic in PostgreSQL across replicas/restarts.
    Task<bool> TryConsumeDaily(Guid userId, DateOnly day, int limit, CancellationToken ct);
}
public interface IRecommendationService
{
    Task<RecommendationResponse> Create(Guid userId, CreateRecommendationRequest request, string correlationId, CancellationToken ct);
    Task<RecommendationResponse> Get(Guid userId, Guid id, string correlationId, CancellationToken ct);
    Task<PagedResponse<RecommendationHistoryResponse>> History(Guid userId, int page, int pageSize, CancellationToken ct);
}
public sealed class RecommendationFailure(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public static RecommendationFailure InvalidOutput() => new(502, "AI_INVALID_RESPONSE", "Gemini trả kết quả không hợp lệ. Vui lòng thử lại.");
    public static RecommendationFailure AiUnavailable() => new(503, "AI_UNAVAILABLE", "AI tư vấn hiện tạm thời không khả dụng. Bạn vẫn có thể tìm và mượn thiết bị qua danh mục.");
    public static RecommendationFailure CatalogUnavailable() => new(503, "CATALOG_UNAVAILABLE", "Chưa thể đọc danh mục thiết bị. Vui lòng thử lại.");
    public static RecommendationFailure InventoryUnavailable() => new(503, "INVENTORY_UNAVAILABLE", "Chưa thể kiểm tra tồn kho. Vui lòng thử lại.");
}
