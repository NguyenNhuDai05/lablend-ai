using System.Net;
using System.Text;
using System.Text.Json;
using LabLend.Contracts;
using Recommendation.Application;
using Recommendation.Domain;

namespace Recommendation.Tests;

internal static class Data
{
    public static readonly Guid User = Guid.NewGuid();
    public static readonly CatalogEquipment Equipment = new(Guid.NewGuid(), Guid.NewGuid(), "Điện tử", "Thiết bị thật", "Mô tả", "Thông số", ["lab"], null, true);
    public static readonly CreateRecommendationRequest Request = new("Thử nghiệm điện tử cơ bản", ["Cho người mới"]);
    public static GeminiItem Item(Guid? id = null) => new((id ?? Equipment.Id).ToString(), "Lý do phù hợp", "Cách dùng an toàn");
    public static string ProviderJson(object output, string finish = "STOP") => JsonSerializer.Serialize(new
    { candidates = new[] { new { finishReason = finish, content = new { parts = new[] { new { text = JsonSerializer.Serialize(output, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } } } } } });
}
internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    public static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json") };
    public static HttpResponseMessage Raw(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
internal sealed class FakeGemini : IGeminiClient
{
    public GeminiResult Result { get; set; } = new("test-gemini", [Data.Item()]);
    public int Calls { get; private set; }
    public bool Disabled { get; set; }
    public void EnsureAvailable() { if (Disabled) throw RecommendationFailure.AiUnavailable(); }
    public Task<GeminiResult> Recommend(CreateRecommendationRequest request, IReadOnlyList<CatalogEquipment> candidates, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(Result); }
}
internal sealed class FakeCatalog : ICatalogClient
{
    public IReadOnlyList<CatalogEquipment> Rows { get; set; } = [Data.Equipment];
    public CatalogEquipment? Current { get; set; } = Data.Equipment;
    public Task<IReadOnlyList<CatalogEquipment>> Candidates(Guid? categoryId, CancellationToken ct) => Task.FromResult(Rows);
    public Task<CatalogEquipment?> Get(Guid id, CancellationToken ct) => Task.FromResult(Current?.Id == id ? Current : null);
}
internal sealed class FakeInventory : IInventoryAvailabilityClient
{
    public IReadOnlyList<Availability> Rows { get; set; } = [new(Data.Equipment.Id, 0, 3)];
    public bool Fail { get; set; }
    public int Calls { get; private set; }
    public Task<IReadOnlyList<Availability>> Check(IReadOnlyList<Guid> ids, string correlationId, CancellationToken ct)
    { Calls++; if (Fail) throw RecommendationFailure.InventoryUnavailable(); return Task.FromResult(Rows); }
}
internal sealed class MemoryRepository : IRecommendationRepository
{
    public List<RecommendationSession> Sessions { get; } = [];
    public bool Allow { get; set; } = true;
    public Task<bool> TryConsumeDaily(Guid userId, DateOnly day, int limit, CancellationToken ct) => Task.FromResult(Allow);
    public Task Save(RecommendationSession session, CancellationToken ct) { Sessions.Add(session); return Task.CompletedTask; }
    public Task<RecommendationSession?> Get(Guid id, Guid userId, CancellationToken ct) => Task.FromResult(Sessions.SingleOrDefault(s => s.Id == id && s.UserId == userId));
    public Task<PagedResponse<RecommendationHistoryResponse>> History(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        var rows = Sessions.Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt).ToArray();
        return Task.FromResult(new PagedResponse<RecommendationHistoryResponse>(rows.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new RecommendationHistoryResponse(s.Id, s.Purpose, s.CreatedAt, s.ModelUsed, s.Items.Count)).ToArray(), rows.Length, page, pageSize));
    }
}
