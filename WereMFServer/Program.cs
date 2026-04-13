using System.CommandLine;
using System.Runtime.InteropServices;
using WereMFServer;

var defaultPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
    ? "WereMF.exe" 
    : "WereMF";

var pathOption = new Option<string>(
    name: "--path",
    description: "Path to WereMF executable",
    getDefaultValue: () => defaultPath);

var hostOption = new Option<string>(
    name: "--host",
    description: "Host to bind",
    getDefaultValue: () => "127.0.0.1");

var wsPortOption = new Option<int>(
    name: "--websocket-port",
    description: "WebSocket port",
    getDefaultValue: () => 5000);

var httpPortOption = new Option<int>(
    name: "--http-port",
    description: "HTTP port for status page",
    getDefaultValue: () => 5001);

var rootCommand = new RootCommand("WereMF Game Server");

rootCommand.AddOption(pathOption);
rootCommand.AddOption(hostOption);
rootCommand.AddOption(wsPortOption);
rootCommand.AddOption(httpPortOption);

rootCommand.SetHandler((path, host, wsPort, httpPort) =>
{
    var server = new GameServer(path, host, wsPort, httpPort);
    
    while (true)
    {
        var input = (Console.ReadLine() ?? "").Trim();
        if (input == "") continue;
        
        switch (input.ToLower())
        {
            case "help":
                PrintHelp();
                break;
            case "ls":
                server.List();
                break;
            case "seed":
                server.SetSeed(null);
                break;
            case "quit":
            case "exit":
                return;
            case var cmd when cmd.StartsWith("kill "):
                var parts = cmd.Split(' ', 2);
                if (parts.Length == 2) server.Kill(parts[1]);
                else Console.WriteLine("Usage: kill <game-id>");
                break;
            case var cmd when cmd.StartsWith("config "):
                var configParts = cmd.Split(' ', 2);
                if (configParts.Length == 2) server.SetConfig(configParts[1]);
                else Console.WriteLine("Usage: config <path>");
                break;
            case var cmd when cmd.StartsWith("seed "):
                var seedParts = cmd.Split(' ', 2);
                if (seedParts.Length == 2 && int.TryParse(seedParts[1], out var seed))
                    server.SetSeed(seed);
                else Console.WriteLine("Usage: seed <number>");
                break;
            default:
                Console.WriteLine($"Unknown: {input}");
                break;
        }
    }
}, pathOption, hostOption, wsPortOption, httpPortOption);

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    return;
}

rootCommand.Invoke(args);

void PrintHelp()
{
    Console.WriteLine(@"
Usage: WereMFServer [options]

Options:
  --path <path>            WereMF executable path
  --host <host>            Bind host [default: 127.0.0.1]
  --websocket-port <port> WebSocket port [default: 5000]
  --http-port <port>      HTTP port [default: 5001]

Runtime Commands:
  help         Show help
  ls           List active games
  seed         Random seed for new games
  seed <n>     Set seed
  config <p>   Set config
  kill <id>    Kill game
  exit         Exit
");
}