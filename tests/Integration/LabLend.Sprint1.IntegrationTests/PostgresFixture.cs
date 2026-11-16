using System.Diagnostics;
using System.Security.Cryptography;
using Npgsql;
using Xunit;

namespace LabLend.Sprint1.IntegrationTests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LABLEND_POSTGRES_TESTS") != "1")
            Skip = "Set LABLEND_POSTGRES_TESTS=1 to run isolated real PostgreSQL/Compose tests.";
    }
}

[CollectionDefinition("postgres", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string project = "lablend-s1-test-" + Guid.NewGuid().ToString("N");
    private readonly string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private string? root;
    public string IdentityConnection { get; private set; } = "";
    public string BorrowingConnection { get; private set; } = "";
    public string CatalogConnection { get; private set; } = "";
    public string InventoryConnection { get; private set; } = "";
    public string SigningKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string AdminPassword { get; } = "Aa1!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("LABLEND_POSTGRES_TESTS") != "1") return;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LabLendAI.sln"))) dir = dir.Parent;
        root = dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
        try
        {
            await Docker("up", "-d", "--wait", "--wait-timeout", "90");
            var address = (await Docker("port", "postgres", "5432")).Trim();
            var admin = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = int.Parse(address.Split(':').Last()),
                Database = "postgres", Username = "postgres", Password = password, IncludeErrorDetail = false };
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync();
            foreach (var service in new[] { "identity", "borrowing", "catalog", "inventory" })
            {
                await using var setup = connection.CreateCommand();
                // Test-generated hex password only. No user input or existing DB is accepted by this fixture.
                // CREATE DATABASE cannot run in a multi-statement implicit transaction.
                setup.CommandText = $"CREATE ROLE {service}_user LOGIN PASSWORD '{password}'";
                await setup.ExecuteNonQueryAsync();
                setup.CommandText = $"CREATE DATABASE {service}_db OWNER {service}_user";
                await setup.ExecuteNonQueryAsync();
                setup.CommandText = $"REVOKE CONNECT ON DATABASE {service}_db FROM PUBLIC";
                await setup.ExecuteNonQueryAsync();
                var scoped = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = service + "_db", Username = service + "_user" };
                switch (service)
                {
                    case "identity": IdentityConnection = scoped.ConnectionString; break;
                    case "borrowing": BorrowingConnection = scoped.ConnectionString; break;
                    case "catalog": CatalogConnection = scoped.ConnectionString; break;
                    case "inventory": InventoryConnection = scoped.ConnectionString; break;
                }
            }
        }
        catch { await DisposeAsync(); throw; }
    }
    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        // project is a private, generated name and this Compose file has no external/shared volumes.
        // Remove only its anonymous PostgreSQL data volume as well as its containers/network.
        if (root != null) await Docker("down", "--volumes", "--remove-orphans");
    }
    private async Task<string> Docker(params string[] args)
    {
        var start = new ProcessStartInfo("docker") { WorkingDirectory = root!, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "compose", "-p", project, "-f", "infrastructure/docker/compose.tests.yml" }.Concat(args)) start.ArgumentList.Add(arg);
        start.Environment["LABLEND_TEST_DB_PASSWORD"] = password;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Docker Compose did not respond; check Docker Desktop before rerunning tests.");
        }
        var result = await output;
        if (process.ExitCode != 0) throw new InvalidOperationException("Isolated Docker Compose test command failed: " + (await error).Replace(password, "[redacted]"));
        return result;
    }
}
