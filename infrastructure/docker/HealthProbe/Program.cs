using Grpc.Health.V1;
using Grpc.Net.Client;

try
{
    if (args.Length != 2) return 2;
    if (args[0] == "file")
        return File.Exists(args[1]) && DateTime.UtcNow - File.GetLastWriteTimeUtc(args[1]) < TimeSpan.FromSeconds(45) ? 0 : 1;
    if (args[0] == "grpc")
    {
        using var channel = GrpcChannel.ForAddress(args[1]);
        var response = await new Health.HealthClient(channel).CheckAsync(new HealthCheckRequest(), deadline: DateTime.UtcNow.AddSeconds(5));
        return response.Status == HealthCheckResponse.Types.ServingStatus.Serving ? 0 : 1;
    }
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    using var result = await client.GetAsync(args[1]);
    return result.IsSuccessStatusCode ? 0 : 1;
}
catch { return 1; }
