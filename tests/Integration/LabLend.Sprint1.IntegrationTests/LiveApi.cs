using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace LabLend.Sprint1.IntegrationTests;

internal sealed class LiveApi : IAsyncDisposable
{
    private Process? process;
    private Task<string>? stdout;
    private Task<string>? stderr;
    public string Address { get; private set; } = "";
    public async Task Start(string service, string connection, PostgresFixture fixture, string? catalog = null)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LabLendAI.sln"))) directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("Repository not found.");
        var folder = Path.Combine(root, "src", "Services", service, service + ".Api");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        Address = $"http://127.0.0.1:{port}/";
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = folder, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        // Use the exact binaries tested by this configuration; no recursive build/test subprocess.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        start.ArgumentList.Add(Path.Combine(folder, "bin", configuration, "net8.0", service + ".Api.dll"));
        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["ASPNETCORE_URLS"] = Address, ["ASPNETCORE_ENVIRONMENT"] = "Development", ["DOTNET_ENVIRONMENT"] = "Development",
            ["ConnectionStrings__Database"] = connection, ["Jwt__SigningKey"] = fixture.SigningKey,
            ["Database__ApplyMigrations"] = "true", ["Logging__LogLevel__Default"] = "Warning",
            ["SessionCookie__Secure"] = "false", ["Bootstrap__Enabled"] = (service == "Identity").ToString(),
            ["Bootstrap__AdminEmail"] = "admin@sprint1.test", ["Bootstrap__AdminPassword"] = fixture.AdminPassword,
            ["Catalog__BaseAddress"] = catalog ?? "http://127.0.0.1:1/"
        }) start.Environment[name] = value;
        process = Process.Start(start)!;
        stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
        using var http = new HttpClient { BaseAddress = new Uri(Address), Timeout = TimeSpan.FromSeconds(1) };
        for (var i = 0; i < 100; i++)
        {
            if (process.HasExited) throw new InvalidOperationException("Test API startup failed: " + await stderr + await stdout);
            try { if ((await http.GetAsync("health/live")).IsSuccessStatusCode) return; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(200);
        }
        throw new TimeoutException("Live test API did not become ready.");
    }
    public async ValueTask DisposeAsync()
    {
        if (process == null) return;
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        if (stdout != null) await stdout;
        if (stderr != null) await stderr;
        process.Dispose();
    }
}
