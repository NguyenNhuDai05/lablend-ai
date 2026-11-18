using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Recommendation.Application;
using Recommendation.Infrastructure;
using Xunit;

namespace Recommendation.Tests;

public sealed class GeminiClientTests
{
    [Fact] public async Task Provider_observability_records_only_status_without_secret_or_prompt()
    {
        var logger = new StatusLogger();
        var client = new GeminiClient(new HttpClient(new Handler((_, _) => Task.FromResult(Handler.Raw("sensitive provider body", HttpStatusCode.TooManyRequests)))),
            Microsoft.Extensions.Options.Options.Create(Options()), logger);
        await Assert.ThrowsAsync<RecommendationFailure>(() => client.Recommend(Data.Request, [Data.Equipment], default));
        Assert.Equal("Gemini HTTP completed with status 429.", Assert.Single(logger.Messages));
        Assert.DoesNotContain("test-only-secret", logger.Messages[0]); Assert.DoesNotContain(Data.Request.Purpose, logger.Messages[0]);
        Assert.DoesNotContain("sensitive", logger.Messages[0]);
    }
    private sealed class StatusLogger : ILogger<GeminiClient>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
    private static GeminiOptions Options() => new() { Enabled = true, ApiKey = "test-only-secret", Model = "gemini-3.5-flash-lite", TimeoutSeconds = 1 };
    private static GeminiClient Client(Handler handler, GeminiOptions? options = null) => new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(options ?? Options()));
    [Fact] public async Task Real_REST_request_uses_key_header_system_instruction_schema_and_only_catalog_context()
    {
        var client = Client(new(async (request, ct) => {
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent", request.RequestUri!.ToString());
            Assert.Equal("test-only-secret", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            Assert.Null(request.Headers.Authorization);
            var body = await request.Content!.ReadAsStringAsync(ct); using var json = JsonDocument.Parse(body);
            Assert.DoesNotContain("test-only-secret", body);
            Assert.Contains("untrusted DATA", json.RootElement.GetProperty("systemInstruction").ToString());
            var schema = json.RootElement.GetProperty("generationConfig").GetProperty("responseFormat").GetProperty("text").GetProperty("schema");
            Assert.Equal(6, schema.GetProperty("properties").GetProperty("recommendations").GetProperty("maxItems").GetInt32());
            using var context = JsonDocument.Parse(json.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!);
            Assert.Equal(Data.Equipment.Specifications, context.RootElement.GetProperty("catalog")[0].GetProperty("Specifications").GetString());
            return Handler.Raw(Data.ProviderJson(new { recommendations = new[] { Data.Item() } }));
        }));
        Assert.Single((await client.Recommend(Data.Request, [Data.Equipment], default)).Recommendations);
    }
    [Theory] [InlineData("disabled")] [InlineData("key")] [InlineData("model")]
    public async Task Disabled_missing_key_or_invalid_model_never_sends(string defect)
    {
        var options = Options(); if (defect == "disabled") options.Enabled = false; if (defect == "key") options.ApiKey = ""; if (defect == "model") options.Model = "../../secret";
        var client = Client(new((_, _) => throw new Exception("Must not send")), options);
        Assert.Equal("AI_UNAVAILABLE", (await Assert.ThrowsAsync<RecommendationFailure>(() => client.Recommend(Data.Request, [Data.Equipment], default))).Code);
    }
    [Theory] [InlineData(429)] [InlineData(500)] [InlineData(503)] [InlineData(401)]
    public async Task Provider_HTTP_errors_are_sanitized_without_retries(int status)
    {
        var calls = 0; var client = Client(new((_, _) => { calls++; return Task.FromResult(Handler.Raw("sensitive raw provider error", (HttpStatusCode)status)); }));
        var failure = await Assert.ThrowsAsync<RecommendationFailure>(() => client.Recommend(Data.Request, [Data.Equipment], default));
        Assert.Equal("AI_UNAVAILABLE", failure.Code); Assert.DoesNotContain("sensitive", failure.Message); Assert.Equal(1, calls);
    }
    [Theory] [InlineData("garbage")] [InlineData("{}")]
    [InlineData("{\"candidates\":[]}")]
    public async Task Invalid_envelope_returns_invalid_response(string body)
    {
        var client = Client(new((_, _) => Task.FromResult(Handler.Raw(body))));
        Assert.Equal("AI_INVALID_RESPONSE", (await Assert.ThrowsAsync<RecommendationFailure>(() => client.Recommend(Data.Request, [Data.Equipment], default))).Code);
    }
    [Theory] [InlineData("null")] [InlineData("[]")] [InlineData("{}")] [InlineData("{\"recommendations\":null}")]
    [InlineData("{\"recommendations\":[],\"stock\":99}")] [InlineData("{\"recommendations\":[null]}")]
    public async Task Invalid_structured_output_is_rejected(string output)
    {
        var body = JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = output } } } } } });
        var client = Client(new((_, _) => Task.FromResult(Handler.Raw(body))));
        Assert.Equal("AI_INVALID_RESPONSE", (await Assert.ThrowsAsync<RecommendationFailure>(() => client.Recommend(Data.Request, [Data.Equipment], default))).Code);
    }
    [Fact] public async Task Truncated_response_is_rejected()
    {
        var client = Client(new((_, _) => Task.FromResult(Handler.Raw(Data.ProviderJson(new { recommendations = Array.Empty<object>() }, "MAX_TOKENS")))));
        Assert.Equal("AI_INVALID_RESPONSE", (await Assert.ThrowsAsync<RecommendationFailure>(() => client.Recommend(Data.Request, [Data.Equipment], default))).Code);
    }
    [Fact] public async Task Oversized_response_is_rejected_at_the_byte_ceiling()
    {
        var client = Client(new((_, _) => Task.FromResult(Handler.Raw(new string('x', 128 * 1024 + 1)))));
        Assert.Equal("AI_INVALID_RESPONSE", (await Assert.ThrowsAsync<RecommendationFailure>(() => client.Recommend(Data.Request, [Data.Equipment], default))).Code);
    }
    [Fact] public async Task Provider_timeout_is_unavailable()
    {
        var client = Client(new(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Handler.Json(new { }); }));
        Assert.Equal("AI_UNAVAILABLE", (await Assert.ThrowsAsync<RecommendationFailure>(() => client.Recommend(Data.Request, [Data.Equipment], default))).Code);
    }
    [Fact] public async Task Caller_cancellation_is_not_a_provider_failure()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var client = Client(new(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return Handler.Json(new { }); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Recommend(Data.Request, [Data.Equipment], cts.Token));
    }
}
