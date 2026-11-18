using System.Net;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Recommendation.Application;
using Recommendation.Infrastructure;
using Xunit;
using V1 = LabLend.Inventory.Contracts.V1;

namespace Recommendation.Tests;

public sealed class DependencyTests
{
    [Fact] public async Task Catalog_follows_all_pages_beyond_100_without_silent_truncation()
    {
        var rows = Enumerable.Range(0, 125).Select(_ => Data.Equipment with { Id = Guid.NewGuid() }).ToArray(); var calls = 0;
        var client = new CatalogClient(new HttpClient(new Handler((request, _) => {
            calls++; Assert.Contains($"page={calls}", request.RequestUri!.Query);
            return Task.FromResult(Handler.Json(new { items = rows.Skip((calls - 1) * 100).Take(100), total = rows.Length, page = calls, pageSize = 100 }));
        })) { BaseAddress = new("http://catalog/") }, new());
        Assert.Equal(125, (await client.Candidates(null, default)).Count); Assert.Equal(2, calls);
    }
    [Fact] public async Task Catalog_over_budget_requires_scope_before_provider_and_sends_explicit_category_filter()
    {
        var categoryId = Guid.NewGuid(); var calls = 0;
        var client = new CatalogClient(new HttpClient(new Handler((request, _) => {
            calls++; Assert.Contains($"categoryId={categoryId:D}", request.RequestUri!.Query);
            return Task.FromResult(Handler.Json(new { items = new[] { Data.Equipment }, total = 201, page = 1, pageSize = 100 }));
        })) { BaseAddress = new("http://catalog/") }, new());
        Assert.Equal("VALIDATION_ERROR", (await Assert.ThrowsAsync<RecommendationFailure>(() => client.Candidates(categoryId, default))).Code); Assert.Equal(1, calls);
    }
    [Theory] [InlineData("inactive")] [InlineData("empty-page")] [InlineData("http")] [InlineData("json")]
    public async Task Invalid_catalog_is_unavailable(string defect)
    {
        var client = new CatalogClient(new HttpClient(new Handler((_, _) => Task.FromResult(defect switch {
            "http" => Handler.Raw("private error", HttpStatusCode.ServiceUnavailable), "json" => Handler.Raw("invalid"),
            "inactive" => Handler.Json(new { items = new[] { Data.Equipment with { IsActive = false } }, total = 1, page = 1, pageSize = 100 }),
            _ => Handler.Json(new { items = Array.Empty<CatalogEquipment>(), total = 2, page = 1, pageSize = 100 }) }))) { BaseAddress = new("http://catalog/") }, new());
        Assert.Equal("CATALOG_UNAVAILABLE", (await Assert.ThrowsAsync<RecommendationFailure>(() => client.Candidates(null, default))).Code);
    }
    [Fact] public async Task Missing_catalog_detail_returns_null()
    {
        var client = new CatalogClient(new HttpClient(new Handler((_, _) => Task.FromResult(Handler.Raw("", HttpStatusCode.NotFound)))) { BaseAddress = new("http://catalog/") }, new());
        Assert.Null(await client.Get(Guid.NewGuid(), default));
    }
    [Fact] public async Task Inventory_uses_only_CheckAvailability_with_service_token_deadline_and_correlation()
    {
        var invoker = new AvailabilityInvoker();
        var client = new InventoryAvailabilityClient(new V1.InventoryAllocation.InventoryAllocationClient(invoker), Options.Create(new InventoryOptions { ServiceToken = new('s', 40) }));
        var result = await client.Check([Data.Equipment.Id], "request-correlation", default);
        Assert.Equal(0, Assert.Single(result).Available);
        Assert.Equal("CheckAvailability", invoker.Method); Assert.Equal("Bearer " + new string('s', 40), invoker.Call.Headers!.GetValue("authorization"));
        Assert.Equal("request-correlation", invoker.Call.Headers.GetValue("x-correlation-id"));
        Assert.InRange((invoker.Call.Deadline!.Value - DateTime.UtcNow).TotalSeconds, 0, 6);
    }
    [Theory] [InlineData(StatusCode.Unauthenticated)] [InlineData(StatusCode.Unavailable)] [InlineData(StatusCode.DeadlineExceeded)]
    public async Task Inventory_rpc_errors_are_dependency_failures(StatusCode status)
    {
        var client = new InventoryAvailabilityClient(new V1.InventoryAllocation.InventoryAllocationClient(new AvailabilityInvoker { Failure = status }), Options.Create(new InventoryOptions { ServiceToken = new('s', 40) }));
        Assert.Equal("INVENTORY_UNAVAILABLE", (await Assert.ThrowsAsync<RecommendationFailure>(() => client.Check([Data.Equipment.Id], "c", default))).Code);
    }
    [Fact] public async Task Inventory_missing_credential_does_not_send_rpc()
    {
        var invoker = new AvailabilityInvoker();
        var client = new InventoryAvailabilityClient(new V1.InventoryAllocation.InventoryAllocationClient(invoker), Options.Create(new InventoryOptions()));
        await Assert.ThrowsAsync<RecommendationFailure>(() => client.Check([Data.Equipment.Id], "c", default)); Assert.Null(invoker.Method);
    }
    private sealed class AvailabilityInvoker : CallInvoker
    {
        public string? Method { get; private set; } public CallOptions Call { get; private set; } public StatusCode? Failure { get; init; }
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Method = method.Name; Call = options; Assert.Equal("CheckAvailability", method.Name); // Any mutation immediately fails this test.
            Assert.Equal(Data.Equipment.Id.ToString(), Assert.Single(((V1.CheckAvailabilityRequest)(object)request).EquipmentTypeIds));
            var result = new V1.CheckAvailabilityReply { Items = { new V1.Availability { EquipmentTypeId = Data.Equipment.Id.ToString(), Available = 0, Total = 2 } } };
            var response = Failure.HasValue ? Task.FromException<TResponse>(new RpcException(new Status(Failure.Value, "sensitive dependency error"))) : Task.FromResult((TResponse)(object)result);
            return new(response, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });
        }
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> m, string? h, CallOptions o, TRequest r) => throw new NotSupportedException();
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> m, string? h, CallOptions o, TRequest r) => throw new NotSupportedException();
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> m, string? h, CallOptions o) => throw new NotSupportedException();
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> m, string? h, CallOptions o) => throw new NotSupportedException();
    }
}
