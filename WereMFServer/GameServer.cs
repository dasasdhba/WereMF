using System.Diagnostics;
using System.Net;
using Fleck;

namespace WereMFServer;

public class GameServer
{
    private string _config = "";
    private int? _seed;

    private WebSocketServer _server;
    private HttpListener? _httpServer;
    private Dictionary<IWebSocketConnection, GameProcess> _games;
    
    private void SendOutputs(IWebSocketConnection socket)
    {
        const int retryCount = 10;
        int counter = 0;
        while (_games.TryGetValue(socket, out var game))
        {
            try
            {
                var outputs = game.GetOutput();
                foreach (var line in outputs)
                {
                    socket.Send(line + "\n");
                }
                
                if (outputs.Count > 0)
                    Console.WriteLine($"[{DateTime.Now}] Sent output of game process {game.GameId}.");
                counter = 0;
            }
            catch (Exception e)
            {
                counter++;
                Console.WriteLine($"[{DateTime.Now}] {e}, failed sending output of game process {game.GameId}, retry {counter}/{retryCount}");
            }
            
            if (counter >= retryCount)
            {
                Console.WriteLine($"[{DateTime.Now}] Game process {game.GameId} is unexpected closed by too many errors of sending outputs.");
                socket.Close();
                break;
            }
            
            Thread.Sleep(50);
        }
    }

    private void Start(IWebSocketConnection socket, string wereMFPath)
    {
        socket.OnOpen = () =>
        {
            var gameId = Guid.NewGuid();
            var arg = "--api";
            if (_config != "")
            {
                arg += $" --config {_config}";
            }
            if (_seed != null)
            {
                arg += $" --seed {_seed}";
            }
            
            var psi = new ProcessStartInfo(wereMFPath)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(wereMFPath),
                Arguments = arg
            };
            psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
            psi.StandardInputEncoding = System.Text.Encoding.UTF8;
                
            var proc = Process.Start(psi);
            if (proc == null)
            {
                Console.WriteLine($"[{DateTime.Now}] Failed starting game process of {gameId}.");
                socket.Close();
                return;
            }
                
            var game = new GameProcess(gameId, proc);
            _games[socket] = game;
            Task.Run(() => SendOutputs(socket));
        };
            
        socket.OnMessage = message =>
        {
            if (_games.TryGetValue(socket, out var game))
            {
                const int retryCount = 10;
                var counter = 0;
                while (!game.SendInput(message))
                {
                    counter++;
                    Console.WriteLine($"[{DateTime.Now}] Failed sending input to game process {game.GameId}, retry {counter}/{retryCount}");

                    if (counter >= retryCount)
                    {
                        Console.WriteLine($"[{DateTime.Now}] Game process {game.GameId} is unexpected closed by too many errors of sending inputs.");
                        socket.Close();
                        break;
                    }
                }
            }
        };
            
        socket.OnClose = () =>
        {
            if (_games.Remove(socket, out var game))
            {
                game.Kill();
                _games.Remove(socket);
                Console.WriteLine($"[{DateTime.Now}] Closed game: {game.GameId}");
            }
        };
    }
    
    private void HandleHttpRequests()
    {
        const int retryCount = 10;
        int counter = 0;
        while (_httpServer is { IsListening: true })
        {
            try
            {
                var context = _httpServer.GetContext();
                var response = context.Response;
                
                var html = GenerateStatusHtml();
                var buffer = System.Text.Encoding.UTF8.GetBytes(html);
                
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer);
                response.Close();
                
                counter = 0;
            }
            catch (Exception e)
            {
                counter++;
                Console.WriteLine($"[{DateTime.Now}] {e}, HTTP refresh failed, retry {counter}/{retryCount}");
            }
            
            if (counter >= retryCount)
            {
                Console.WriteLine($"[{DateTime.Now}] HTTP listener stopped as refresh failed too many times.");
                break;
            }
        }
    }

    private string GenerateStatusHtml()
    {
        var gameList = string.Join("", _games.Values.Select(g => 
            $"<li>Game ID: {g.GameId}</li>"));
        
        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>WereMF Server Status</title>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 40px; }}
        h1 {{ color: #333; }}
        .count {{ font-size: 24px; font-weight: bold; color: #2196F3; }}
        ul {{ list-style-type: none; padding: 0; }}
        li {{ padding: 8px; border-bottom: 1px solid #eee; }}
    </style>
</head>
<body>
    <h1>WereMF Server Status</h1>
    <p>Active Games: <span class=""count"">{_games.Count}</span></p>
    <h2>Game List</h2>
    <ul>
        {(string.IsNullOrEmpty(gameList) ? "<li>No active games</li>" : gameList)}
    </ul>
</body>
</html>";
    }

    public GameServer(string wereMFPath, string host, int wsPort, int httpPort)
    {
        if (!File.Exists(wereMFPath))
        {
            Console.WriteLine($"[{DateTime.Now}] {wereMFPath} is not a valid WereMF cli program, server initialization failed.");
        }
    
        _games = [];
        
        var url = $"ws://{host}:{wsPort}";
        var http = $"http://{host}:{httpPort}/";
        _server = new(url);
        _server.Start(socket => Start(socket, wereMFPath));

        // Start HTTP server for status page
        _httpServer = new HttpListener();
        _httpServer.Prefixes.Add(http);
        _httpServer.Start();
        Task.Run(HandleHttpRequests);

        Console.WriteLine($"[{DateTime.Now}] WereMF Server started on {url}.");
        Console.WriteLine($"[{DateTime.Now}] Status page available at {http}.");
    }

    public void SetConfig(string config)
    {
        Console.WriteLine($"[{DateTime.Now}] Now using config {config}.");
        _config = config;
    }

    public void SetSeed(int? seed)
    {
        if (seed == null)
            Console.WriteLine($"[{DateTime.Now}] Next game will run with a random seed.");
        else
            Console.WriteLine($"[{DateTime.Now}] Next game will run with seed {seed}.");
        _seed = seed;
    }

    public void List()
    {
        Console.WriteLine($"[{DateTime.Now}] Current game count: {_games.Count}");
        foreach (var (k, v) in _games)
        {
            Console.WriteLine($"Game ID: {v.GameId}");
        }
    }

    public void Kill(string id)
    {
        foreach (var (k, v) in _games)
        {
            if (v.GameId.ToString() == id) k.Close();
        }
    }
}