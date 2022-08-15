using DistanceSteamDataServer.Services;

namespace DistanceSteamDataServer;

internal static class Program
{
    public static SteamKit Steam = null!;

    public static async Task Main(string[] args)
    {
        var steamUsername = Environment.GetEnvironmentVariable("STEAM_USERNAME");
        var steamPassword = Environment.GetEnvironmentVariable("STEAM_PASSWORD");

        if (steamUsername == null || steamPassword == null)
        {
            throw new ArgumentException("Environment variables STEAM_USERNAME and STEAM_PASSWORD must be provided");
        }

        Steam = SteamKit.Connect(steamUsername, steamPassword);
        var steamTask = await Steam.RunAsync();

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddGrpc();
        var app = builder.Build();

        app.MapGrpcService<SteamService>();
        app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

        var cancelSource = new CancellationTokenSource();
        var appTask = app.RunAsync(cancelSource.Token);

        await Task.WhenAny(steamTask, appTask);
        cancelSource.Cancel();
        Steam.Disconnect();
        await Task.WhenAll(steamTask, appTask);
    }
}
