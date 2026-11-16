using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Borrowing.Application;

namespace Borrowing.Infrastructure;

public sealed class CatalogClient(HttpClient client) : ICatalogClient
{
    public async Task ValidateEquipment(IReadOnlyCollection<Guid> ids, string correlationId, CancellationToken ct)
    {
        try
        {
            // One request per distinct type, at most twenty. The client has a single total timeout budget below.
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(5));
            foreach (var id in ids)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"api/equipment-types/{id}");
                request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
                using var response = await client.SendAsync(request, budget.Token);
                if (response.StatusCode == HttpStatusCode.NotFound) throw new LoanFailure(400, "Equipment type was not found in Catalog.");
                if (!response.IsSuccessStatusCode) throw new LoanFailure(503, "Catalog is unavailable.");
                var item = await response.Content.ReadFromJsonAsync<CatalogEquipment>(budget.Token);
                if (item == null || item.Id != id || item.IsActive == null) throw new LoanFailure(503, "Catalog response does not match the agreed contract.");
                if (item.IsActive != true) throw new LoanFailure(400, "Equipment type is inactive.");
            }
        }
        catch (HttpRequestException) { throw new LoanFailure(503, "Catalog is unavailable."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new LoanFailure(503, "Catalog timed out."); }
        catch (JsonException) { throw new LoanFailure(503, "Catalog returned an invalid response."); }
        catch (NotSupportedException) { throw new LoanFailure(503, "Catalog returned an unsupported response."); }
    }
    private sealed record CatalogEquipment(Guid Id, bool? IsActive);
}
