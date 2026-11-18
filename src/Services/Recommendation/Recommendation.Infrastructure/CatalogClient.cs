using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabLend.Contracts;
using Recommendation.Application;

namespace Recommendation.Infrastructure;

public sealed class CatalogClient(HttpClient http, RecommendationPolicy policy) : ICatalogClient
{
    public async Task<IReadOnlyList<CatalogEquipment>> Candidates(Guid? categoryId, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var items = new Dictionary<Guid, CatalogEquipment>();
            long? expectedTotal = null;
            for (var page = 1; ; page++)
            {
                var data = await http.GetFromJsonAsync<PagedResponse<CatalogEquipment>>(
                    $"api/equipment-types?page={page}&pageSize=100&sortBy=name&direction=asc" + (categoryId.HasValue ? $"&categoryId={categoryId:D}" : ""), timeout.Token);
                if (data is null || data.Items is null || data.Page != page || data.PageSize != 100 || data.Total < 0
                    || (expectedTotal.HasValue && expectedTotal != data.Total)) throw RecommendationFailure.CatalogUnavailable();
                expectedTotal = data.Total;
                if (data.Total > policy.MaxCandidates) throw RecommendationService.ScopeRequired();
                foreach (var item in data.Items)
                {
                    if (item is null || item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Name) || !item.IsActive || (categoryId.HasValue && item.CategoryId != categoryId)
                        || !items.TryAdd(item.Id, item)) throw RecommendationFailure.CatalogUnavailable();
                }
                if (items.Count == data.Total) return items.Values.ToArray();
                if (data.Items.Count == 0 || items.Count > data.Total || items.Count > policy.MaxCandidates) throw RecommendationFailure.CatalogUnavailable();
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw RecommendationFailure.CatalogUnavailable(); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException) { throw RecommendationFailure.CatalogUnavailable(); }
    }

    public async Task<CatalogEquipment?> Get(Guid id, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync($"api/equipment-types/{id:D}", ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            var item = await response.Content.ReadFromJsonAsync<CatalogEquipment>(ct);
            if (item?.Id != id) throw RecommendationFailure.CatalogUnavailable();
            return item;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw RecommendationFailure.CatalogUnavailable(); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException) { throw RecommendationFailure.CatalogUnavailable(); }
    }
}
