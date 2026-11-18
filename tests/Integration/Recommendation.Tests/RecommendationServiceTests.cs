using Recommendation.Application;
using Recommendation.Domain;
using Xunit;

namespace Recommendation.Tests;

public sealed class RecommendationServiceTests
{
    private readonly FakeGemini gemini = new(); private readonly FakeCatalog catalog = new();
    private readonly FakeInventory inventory = new(); private readonly MemoryRepository repository = new();
    private RecommendationService Service(RecommendationPolicy? policy = null) => new(gemini, catalog, inventory, repository, policy ?? new(), TimeProvider.System);
    [Fact] public async Task Valid_result_saves_session_items_and_preserves_real_zero_stock()
    {
        var result = await Service().Create(Data.User, Data.Request, "correlation", default);
        Assert.Equal(Data.Equipment.Name, Assert.Single(result.Recommendations).EquipmentName);
        Assert.Equal(0, result.Recommendations[0].Available); Assert.Equal(3, result.Recommendations[0].Total);
        var saved = Assert.Single(repository.Sessions); Assert.Equal(Data.User, saved.UserId); Assert.Single(saved.Items);
        Assert.Equal(1, saved.Items[0].Rank); Assert.Equal(TimeSpan.Zero, saved.CreatedAt.Offset);
    }
    [Theory] [InlineData("fake")] [InlineData("guid")] [InlineData("inactive")] [InlineData("missing")] [InlineData("long")] [InlineData("blank")] [InlineData("many")]
    public async Task Invalid_output_never_saves(string defect)
    {
        gemini.Result = defect switch {
            "fake" => new("test", [new("not-a-guid", "Reason", "Notes")]),
            "guid" => new("test", [Data.Item(Guid.NewGuid())]),
            "long" => new("test", [Data.Item() with { Reason = new string('x', 2001) }]),
            "blank" => new("test", [Data.Item() with { UsageNotes = " " }]),
            "many" => new("test", Enumerable.Repeat(Data.Item(), 7).ToArray()), _ => gemini.Result };
        if (defect == "inactive") catalog.Current = Data.Equipment with { IsActive = false };
        if (defect == "missing") catalog.Current = null;
        var failure = await Assert.ThrowsAsync<RecommendationFailure>(() => Service().Create(Data.User, Data.Request, "c", default));
        Assert.Equal("AI_INVALID_RESPONSE", failure.Code); Assert.Empty(repository.Sessions); Assert.Equal(0, inventory.Calls);
    }
    [Fact] public async Task Duplicates_are_removed_without_reordering()
    {
        gemini.Result = new("test", [Data.Item(), Data.Item()]);
        var result = await Service().Create(Data.User, Data.Request, "c", default);
        Assert.Single(result.Recommendations); Assert.Single(Assert.Single(repository.Sessions).Items);
    }
    [Fact] public async Task Empty_provider_result_is_success_and_distinct_from_failure()
    {
        gemini.Result = new("test", []); var result = await Service().Create(Data.User, Data.Request, "c", default);
        Assert.Empty(result.Recommendations); Assert.Single(repository.Sessions); Assert.Equal(0, inventory.Calls);
    }
    [Fact] public async Task Empty_catalog_does_not_call_Gemini_or_inventory()
    {
        catalog.Rows = []; var result = await Service().Create(Data.User, Data.Request, "c", default);
        Assert.Empty(result.Recommendations); Assert.Equal("catalog-empty", result.ModelUsed); Assert.Equal(0, gemini.Calls);
    }
    [Fact] public async Task Inventory_failure_does_not_save_or_replace_with_zero()
    {
        inventory.Fail = true; var failure = await Assert.ThrowsAsync<RecommendationFailure>(() => Service().Create(Data.User, Data.Request, "c", default));
        Assert.Equal("INVENTORY_UNAVAILABLE", failure.Code); Assert.Empty(repository.Sessions);
    }
    [Fact] public async Task Malformed_stock_is_dependency_failure()
    {
        inventory.Rows = [new(Data.Equipment.Id, 5, 2)];
        Assert.Equal("INVENTORY_UNAVAILABLE", (await Assert.ThrowsAsync<RecommendationFailure>(() => Service().Create(Data.User, Data.Request, "c", default))).Code);
        Assert.Empty(repository.Sessions);
    }
    [Fact] public async Task Daily_limit_blocks_provider_call()
    {
        repository.Allow = false;
        Assert.Equal("RATE_LIMITED", (await Assert.ThrowsAsync<RecommendationFailure>(() => Service().Create(Data.User, Data.Request, "c", default))).Code);
        Assert.Equal(0, gemini.Calls);
    }
    [Fact] public async Task Byte_budget_requires_explicit_scope_without_truncation()
    {
        Assert.Equal("VALIDATION_ERROR", (await Assert.ThrowsAsync<RecommendationFailure>(() => Service(new() { MaxContextBytes = 10 }).Create(Data.User, Data.Request, "c", default))).Code);
        Assert.Equal(0, gemini.Calls);
    }
    [Fact] public async Task Owner_only_history_and_detail_refresh_stock_and_flag_inactive()
    {
        var svc = Service(); var original = await svc.Create(Data.User, Data.Request, "c", default);
        var failure = await Assert.ThrowsAsync<RecommendationFailure>(() => svc.Get(Guid.NewGuid(), original.Id, "c", default));
        Assert.Equal(404, failure.Status);
        Assert.Empty((await svc.History(Guid.NewGuid(), 1, 10, default)).Items);
        Assert.Single((await svc.History(Data.User, 1, 10, default)).Items);
        catalog.Current = Data.Equipment with { IsActive = false }; inventory.Rows = [new(Data.Equipment.Id, 2, 3)];
        var refreshed = await svc.Get(Data.User, original.Id, "c", default);
        Assert.Equal(2, refreshed.Recommendations[0].Available); Assert.False(refreshed.Recommendations[0].IsActive);
    }
    [Theory] [InlineData(0, 10)] [InlineData(1, 0)] [InlineData(1, 101)]
    public async Task Bad_history_pagination_is_rejected(int page, int size) =>
        Assert.Equal(400, (await Assert.ThrowsAsync<RecommendationFailure>(() => Service().History(Data.User, page, size, default))).Status);
    [Theory] [InlineData(0)] [InlineData(9)] [InlineData(2001)]
    public async Task Input_length_is_validated_before_provider(int length)
    {
        await Assert.ThrowsAsync<RecommendationFailure>(() => Service().Create(Data.User, new(new string('x', length)), "c", default)); Assert.Equal(0, gemini.Calls);
    }
    [Fact] public async Task Cancellation_propagates_without_saving()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().Create(Data.User, Data.Request, "c", cancellation.Token));
        Assert.Empty(repository.Sessions);
    }
    [Fact] public void Domain_rejects_duplicate_items_and_non_UTC_timestamps()
    {
        var session = new RecommendationSession(Data.User, Data.Request.Purpose, [], null, "test", DateTimeOffset.UtcNow);
        session.AddItem(Data.Equipment.Id, "reason", "usage");
        Assert.Throws<ArgumentException>(() => session.AddItem(Data.Equipment.Id, "reason", "usage"));
        Assert.Throws<ArgumentException>(() => new RecommendationSession(Data.User, Data.Request.Purpose, [], null, "test", DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(7))));
    }
}
