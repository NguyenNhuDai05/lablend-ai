using System.Diagnostics;

namespace LabLend.Sprint1.IntegrationTests;

internal sealed class LiveBorrowingWorker : IAsyncDisposable
{
    private Process? process;
    private Task<string>? stdout, stderr;
    private readonly string heartbeat = Path.Combine(Path.GetTempPath(), "lablend-s2-worker-" + Guid.NewGuid().ToString("N"));
    public bool Healthy => File.Exists(heartbeat) && DateTime.UtcNow - File.GetLastWriteTimeUtc(heartbeat) < TimeSpan.FromSeconds(10);
    public string Logs { get; private set; } = "";
    public async Task Start(PostgresFixture postgres, Sprint2Stack stack)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "LabLendAI.sln"))) root = root.Parent;
        var folder = Path.Combine(root!.FullName, "src", "Workers", "LabLend.Borrowing.Worker");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(folder, "bin", configuration, "net8.0", "LabLend.Borrowing.Worker.dll"));
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["DOTNET_ENVIRONMENT"] = "Development", ["ConnectionStrings__Database"] = postgres.BorrowingConnection,
            ["ConnectionStrings__Redis"] = postgres.RedisConnection, ["Inventory__GrpcAddress"] = stack.InventoryGrpc,
            ["Inventory__ServiceToken"] = postgres.SigningKey, ["Worker__HeartbeatPath"] = heartbeat,
            ["Worker__HeartbeatSeconds"] = "1", ["Reminders__PollSeconds"] = "1", ["BorrowingWorkflow__PollSeconds"] = "1",
            ["Outbox__PollSeconds"] = "1", ["Logging__LogLevel__Default"] = "Information", ["Logging__LogLevel__Microsoft"] = "Warning"
        }) start.Environment[key] = value;
        process = Process.Start(start)!; stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
        await Sprint2RecoveryTests.Wait(async () =>
        {
            if (process.HasExited) throw new InvalidOperationException("Test worker startup failed: " + await stderr + await stdout);
            return Healthy;
        });
    }
    public async ValueTask DisposeAsync()
    {
        if (process == null) return;
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(); Logs = await stdout! + await stderr!;
        process.Dispose(); process = null;
        if (File.Exists(heartbeat)) File.Delete(heartbeat); // This fixture's exact generated file only.
    }
}
