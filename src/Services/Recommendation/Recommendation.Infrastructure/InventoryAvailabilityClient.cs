using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using Recommendation.Application;
using V1 = LabLend.Inventory.Contracts.V1;

namespace Recommendation.Infrastructure;

public sealed class InventoryOptions
{
    public string GrpcAddress { get; set; } = "http://localhost:5181";
    public string ServiceToken { get; set; } = "";
    public int DeadlineSeconds { get; set; } = 5;
}
public sealed class InventoryAvailabilityClient(V1.InventoryAllocation.InventoryAllocationClient client, IOptions<InventoryOptions> options) : IInventoryAvailabilityClient
{
    public async Task<IReadOnlyList<Availability>> Check(IReadOnlyList<Guid> ids, string correlationId, CancellationToken ct)
    {
        if (options.Value.ServiceToken.Length < 32) throw RecommendationFailure.InventoryUnavailable();
        if (ids.Count == 0) return [];
        var request = new V1.CheckAvailabilityRequest();
        request.EquipmentTypeIds.AddRange(ids.Select(id => id.ToString()));
        try
        {
            var result = await client.CheckAvailabilityAsync(request, new CallOptions(
                new Metadata { { "authorization", "Bearer " + options.Value.ServiceToken }, { "x-correlation-id", correlationId } },
                DateTime.UtcNow.AddSeconds(options.Value.DeadlineSeconds), ct));
            var items = new List<Availability>();
            foreach (var item in result.Items)
            {
                if (!Guid.TryParse(item.EquipmentTypeId, out var id)) throw RecommendationFailure.InventoryUnavailable();
                items.Add(new(id, item.Available, item.Total));
            }
            return items;
        }
        catch (RpcException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (RpcException) { throw RecommendationFailure.InventoryUnavailable(); }
    }
}
