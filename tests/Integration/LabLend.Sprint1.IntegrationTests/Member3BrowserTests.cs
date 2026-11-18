using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace LabLend.Sprint1.IntegrationTests;

public sealed class BrowserFactAttribute : FactAttribute
{
    public BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LABLEND_POSTGRES_TESTS") != "1" || Environment.GetEnvironmentVariable("LABLEND_BROWSER_TESTS") != "1")
            Skip = "Set LABLEND_POSTGRES_TESTS=1 and LABLEND_BROWSER_TESTS=1; install apps/web Playwright browser first.";
    }
}

[CollectionDefinition("browser-postgres", DisableParallelization = true)]
public sealed class BrowserPostgresCollection : ICollectionFixture<PostgresFixture>;

[Collection("browser-postgres")]
public sealed class Member3BrowserTests(PostgresFixture postgres, ITestOutputHelper output)
{
    [BrowserFact]
    public Task P0_React_roles_catalog_loans_admin_profile_notifications_and_worker_use_real_services() => Run("m3-p0.mjs");

    [BrowserFact]
    public Task Incident_report_inspection_exchange_notifications_admin_and_maintenance_use_real_services() => Run("incidents.mjs");

    private async Task Run(string script)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = $"http://localhost:{port}";
        await using var stack = new Sprint2Stack(postgres); await stack.Start(webOrigin: origin);
        await using var notification = new LiveApi();
        await notification.Start("Notification", postgres.NotificationConnection, postgres, settings: new Dictionary<string, string>
        { ["ConnectionStrings__Redis"] = postgres.RedisConnection });
        await using var gateway = new LiveApi();
        var settings = new Dictionary<string, string>();
        foreach (var (name, address) in new[] { ("identity", stack.IdentityAddress), ("catalog", stack.CatalogAddress), ("inventory", stack.InventoryAddress), ("borrowing", stack.BorrowingAddress), ("notification", notification.Address) })
            settings[$"ReverseProxy__Clusters__{name}__Destinations__primary__Address"] = address;
        settings["Cors__AllowedOrigins__0"] = origin;
        await gateway.Start("Gateway", postgres.BorrowingConnection, postgres, settings: settings);
        await using var worker = new LiveBorrowingWorker(); await worker.Start(postgres, stack);
        var (type, assets) = await stack.Stock(2);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "LabLendAI.sln"))) root = root.Parent;
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("LABLEND_NODE_EXECUTABLE") ?? "node")
        { WorkingDirectory = Path.Combine(root!.FullName, "apps", "web"), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("e2e/" + script);
        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["LABLEND_E2E_GATEWAY"] = gateway.Address, ["LABLEND_E2E_PORT"] = port.ToString(),
            ["LABLEND_E2E_STUDENT"] = stack.StudentEmail, ["LABLEND_E2E_STAFF"] = stack.StaffEmail,
            ["LABLEND_E2E_ADMIN"] = "admin@sprint1.test", ["LABLEND_E2E_ADMIN_PASSWORD"] = postgres.AdminPassword,
            ["LABLEND_E2E_PASSWORD"] = stack.TestPassword, ["LABLEND_E2E_TYPE"] = type.ToString(),
            ["LABLEND_E2E_ASSETS"] = string.Join(",", assets), ["LABLEND_E2E_STUDENT_ID"] = stack.StudentId.ToString()
        }) start.Environment[name] = value;
        using var browser = Process.Start(start)!;
        var stdout = browser.StandardOutput.ReadToEndAsync(); var stderr = browser.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        try { await browser.WaitForExitAsync(timeout.Token); }
        catch { if (!browser.HasExited) browser.Kill(entireProcessTree: true); throw; }
        var logs = await stdout + await stderr;
        output.WriteLine(logs);
        Assert.True(browser.ExitCode == 0, "Real-service browser E2E failed: " + logs);
    }
}
