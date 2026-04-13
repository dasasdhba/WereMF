using System.CommandLine;
using WereMFServer;

var pathOption = new Option<string>(
    name: "--path",
    description: "Path to WereMF executable",
    getDefaultValue: () => "WereMF");

var hostOption = new Option<string>(
    name: "--host",
    description: "Host to bind",
    getDefaultValue: () => "127.0.0.1");

var wsPortOption = new Option<int>(
    name: "--websocket-port",
    description: "Port to bind",
    getDefaultValue: () => 5000);

var httpPortOption = new Option<int>(
    name: "--http-port",
    description: "Port to bind",
    getDefaultValue: () => 5001);

var rootCommand = new RootCommand("WereMF Game Server")
{
    pathOption,
    hostOption,
    wsPortOption,
    httpPortOption
};

rootCommand.SetHandler((path, host, wsPort, httpPort) =>
{
    var server = new GameServer(path, host, wsPort, httpPort);
    while (true)
    {
        var input = (Console.ReadLine() ?? "").Trim();
        switch (input)
        {
            case "ls":
                server.List();
                break;
            case "seed":
                server.SetSeed(null);
                break;
            case var _ when input.StartsWith("kill"):
                server.Kill(input.Split(' ')[1]);
                break;
            case var _ when input.StartsWith("config"):
                server.SetConfig(input.Split(' ')[1]);
                break;
            case var _ when input.StartsWith("seed"):
                server.SetSeed(int.Parse(input.Split(' ')[1]));
                break;
        }
    }
}, pathOption, hostOption, wsPortOption, httpPortOption);

rootCommand.Invoke(args);