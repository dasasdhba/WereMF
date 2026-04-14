using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
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
            if (!socket.IsAvailable) return;
        
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

    private void SendInput(IWebSocketConnection socket, string message)
    {
        if (!_games.TryGetValue(socket, out var game)) return;
    
        const int retryCount = 10;
        var counter = 0;
        while (!game.SendInput(message))
        {
            if (!socket.IsAvailable) return;
        
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

    private void Start(IWebSocketConnection socket, string path)
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
            
            var psi = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(path),
                Arguments = arg
            };
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardInputEncoding = Encoding.UTF8;
                
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
            
        socket.OnMessage = message => SendInput(socket, message);
            
        socket.OnClose = () =>
        {
            if (_games.Remove(socket, out var game))
            {
                game.Kill();
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
                
                var gameLogs = new Dictionary<Guid, string>();
                foreach (var (socket, game) in _games)
                {
                    if (game.SendInput("\\log null"))
                    {
                        int logCounter = 0;
                        while (socket.IsAvailable)
                        {
                            Thread.Sleep(200);

                            try
                            {
                                var log = game.GetQueuedLog();
                                if (log != "")
                                {
                                    gameLogs[game.GameId] = log;
                                    break;
                                }

                                throw new Exception();
                            }
                            catch
                            {
                                logCounter++;
                                if (logCounter >= retryCount)
                                {
                                    Console.WriteLine($"[{DateTime.Now}] Failed to get log of game process {game.GameId}.");
                                    break;
                                }
                            }
                        }
                    }
                }
                
                var html = GenerateStatusHtml(gameLogs);
                var buffer = Encoding.UTF8.GetBytes(html);
                
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

    private string GenerateStatusHtml(Dictionary<Guid, string> gameLogs)
    {
        var gameList = string.Join("", _games
            .Where(s => s.Key.IsAvailable )
            .Select(s => 
            { 
                var id = s.Value.GameId;
                var logContent = "";
                if (gameLogs.TryGetValue(id, out var log))
                {
                    var escaped = log.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
                    logContent = $"<div class=\"log-content\" style=\"display:none;\"><pre style=\"background:#f5f5f5;padding:10px;max-height:300px;overflow:auto;font-size:11px;\">{escaped}</pre></div>";
                }
                return $"<div class=\"game-item\"><button class=\"game-btn\" onclick=\"this.nextElementSibling.style.display=this.nextElementSibling.style.display==='none'?'block':'none'\">Game: {id}</button>{logContent}</div>";
            }));
        
        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>WereMF Server</title>
    <style>
        body {{ font-family: Arial, margin: 40px; }}
        .count {{ font-size: 24px; font-weight: bold; color: #2196F3; }}
        .game-item {{ margin-bottom: 10px; }}
        .game-btn {{ padding: 10px 20px; background: #2196F3; color: white; border: none; cursor: pointer; }}
        .log-content {{ margin-top: 10px; }}
    </style>
</head>
<body>
    <h1>WereMF Server</h1>
    <p>Active Games: <span class=""count"">{_games.Count}</span></p>
    <div>{gameList}</div>
</body>
</html>";
    }

    public GameServer(string path, string host, int wsPort, int httpPort)
    {
        if (!File.Exists(path))
        {
            throw new Exception($"[{DateTime.Now}] {path} is not a valid WereMF cli program, server initialization failed.");
        }
    
        _games = [];
        
        var url = $"ws://{host}:{wsPort}";
        var http = $"http://{host}:{httpPort}/";
        _server = new(url);
        _server.Start(socket => Start(socket, path));

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