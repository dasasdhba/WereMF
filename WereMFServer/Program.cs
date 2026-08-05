using System.Text;
using WereMFServer;

var options = ServerOptions.Parse(args);
if (!File.Exists(options.GamePath))
{
    Console.Error.WriteLine($"WereMF executable not found: {options.GamePath}");
    return 1;
}

if (options.Config is not null && !File.Exists(options.Config))
{
    Console.Error.WriteLine($"WereMF config not found: {options.Config}");
    return 1;
}
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory, WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot") });
builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

var server = new GameServer(options);
app.Lifetime.ApplicationStopping.Register(server.Dispose);
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", activeRooms = server.ActiveRoomCount, version = "2", llmBots = server.LlmBotsEnabled, llmModel = server.LlmBotModel, llmStats = server.LlmBotStats }));
app.MapGet("/api/rooms", () => Results.Ok(server.GetPublicRooms()));
if (options.DebugApi)
{
    app.MapGet("/api/rooms/{roomCode}/log", (string roomCode) =>
    {
        var log = server.GetRoomLog(roomCode);
        return log is null
            ? Results.NotFound()
            : Results.File(new UTF8Encoding(true).GetBytes(log.Content), "text/plain; charset=utf-8", log.FileName);
    });
}
app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await server.HandleConnectionAsync(socket, context.RequestAborted);
});
app.MapFallbackToFile("index.html");

Console.WriteLine($"WereMF Web Server: http://{options.Host}:{options.Port}");
Console.WriteLine($"Game executable: {options.GamePath}");
await app.RunAsync();
return 0;

internal sealed record ServerOptions(string GamePath, string Host, int Port, string? Config, int? Seed, int RequestTimeoutSeconds, int VoteSecondsPerAlive, int VotePenaltySeconds, int EventIntervalSeconds, bool DebugApi, string? LlmApiKey, string LlmEndpoint, string LlmModel, int LlmTimeoutSeconds, int LlmBotThinkSeconds)
{
    public static ServerOptions Parse(string[] args)
    {
        var gamePath = OperatingSystem.IsWindows() ? "WereMF.exe" : "WereMF";
        var host = "127.0.0.1";
        var port = 5000;
        string? config = Path.GetFullPath(Path.Combine("WereMF", "config.json"));
        int? seed = null;
        var requestTimeoutSeconds = 30;
        var voteSecondsPerAlive = 60;
        var votePenaltySeconds = 30;
        var eventIntervalSeconds = 2;
        var debugApi = false;
        var llmApiKey = Environment.GetEnvironmentVariable("SILICONFLOW_API_KEY");
        var llmEndpoint = Environment.GetEnvironmentVariable("SILICONFLOW_BASE_URL") ?? "https://api.siliconflow.cn/v1/";
        var llmModel = Environment.GetEnvironmentVariable("SILICONFLOW_MODEL") ?? "Qwen/Qwen3.5-4B";
        var llmTimeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("SILICONFLOW_TIMEOUT_SECONDS"), out var configuredLlmTimeout) ? Math.Clamp(configuredLlmTimeout, 1, 30) : 5;
        var llmBotThinkSeconds = int.TryParse(Environment.GetEnvironmentVariable("SILICONFLOW_BOT_THINK_SECONDS"), out var configuredThinkSeconds)
            ? Math.Clamp(configuredThinkSeconds, 3, 60) : 10;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} requires a value");
            switch (args[i])
            {
                case "--path": gamePath = Path.GetFullPath(Next()); break;
                case "--host": host = Next(); break;
                case "--port":
                case "--websocket-port": port = int.Parse(Next()); break;
                case "--http-port": _ = Next(); break;
                case "--config": config = Path.GetFullPath(Next()); break;
                case "--seed": seed = int.Parse(Next()); break;
                case "--request-timeout-seconds": requestTimeoutSeconds = Math.Max(1, int.Parse(Next())); break;
                case "--vote-seconds-per-alive": voteSecondsPerAlive = Math.Max(1, int.Parse(Next())); break;
                case "--vote-penalty-seconds": votePenaltySeconds = Math.Max(0, int.Parse(Next())); break;
                case "--event-interval-seconds": eventIntervalSeconds = Math.Clamp(int.Parse(Next()), 0, 10); break;
                case "--debug-api": debugApi = true; break;
                case "--llm-model": llmModel = Next(); break;
                case "--llm-endpoint": llmEndpoint = Next(); break;
                case "--llm-timeout-seconds": llmTimeoutSeconds = Math.Clamp(int.Parse(Next()), 1, 30); break;
                case "--llm-bot-think-seconds": llmBotThinkSeconds = Math.Clamp(int.Parse(Next()), 3, 60); break;
                case "--disable-llm-bots": llmApiKey = null; break;
                case "--help":
                case "-h":
                    Console.WriteLine("WereMFServer: --path <file> --host <address> --port <number> --config <file> --seed <number> --request-timeout-seconds <n> --vote-seconds-per-alive <n> --vote-penalty-seconds <n> --event-interval-seconds <n> --llm-model <name> --llm-endpoint <url> --llm-timeout-seconds <n> --llm-bot-think-seconds <n> --disable-llm-bots --debug-api");
                    Environment.Exit(0);
                    break;
            }
        }
        if (!llmEndpoint.EndsWith('/') ) llmEndpoint += "/";
        return new ServerOptions(Path.GetFullPath(gamePath), host, port, config, seed, requestTimeoutSeconds, voteSecondsPerAlive, votePenaltySeconds, eventIntervalSeconds, debugApi, string.IsNullOrWhiteSpace(llmApiKey) ? null : llmApiKey, llmEndpoint, llmModel, llmTimeoutSeconds, llmBotThinkSeconds);
    }
}
