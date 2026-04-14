using System.Diagnostics;
using System.Text.Json;

namespace WereMFServer;

public class GameProcess
{
    public Guid GameId { get; }
    private readonly Process _proc;
    private readonly StreamWriter _input;
    private readonly StreamReader _output;
    private readonly StreamReader _error;
    private readonly Queue<string> _outputQueue = new();
    private string _queuedLog = "";
    private bool _running = true;

    private void CaptureOutputs()
    {
        while (!_output.EndOfStream && _running)
        {
            string? line;

            lock (_output)
            {
                line = _output.ReadLine();
            }
            
            if (line != null)
            {
                lock (_outputQueue)
                {
                    _outputQueue.Enqueue(line);
                }

                lock (_queuedLog)
                {
                    try
                    {
                        var json = JsonSerializer.Deserialize<Dictionary<string, string>>(line);
                        if (json != null && json.TryGetValue("api", out string? value) && value == "cli_log")
                        {
                            _queuedLog = json["data"];
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
        }
    }
    
    private void CaptureErrors()
    {
        while (!_error.EndOfStream && _running)
        {
            lock (_error)
            {
                var line = _error.ReadLine();
                if (line != null)
                {
                    Console.WriteLine($"[{DateTime.Now}] Error from game process {GameId}: {line}");
                }
            }
        }
    }
    
    public GameProcess(Guid gameId, Process proc)
    {
        GameId = gameId;
        Console.WriteLine($"[{DateTime.Now}] Game process {gameId} started.");
        
        _proc = proc;
        _input = proc.StandardInput;
        _output = proc.StandardOutput;
        _error = proc.StandardError;
        
        Task.Run(CaptureOutputs);
        Task.Run(CaptureErrors);
    }

    public string GetQueuedLog()
    {
        lock (_queuedLog)
        {
            return _queuedLog;
        }
    }
    
    public bool SendInput(string input)
    {
        if (_proc.HasExited)
        {
            Console.WriteLine($"[{DateTime.Now}] Game process of {GameId} has probably crashed, failed Sending {input} to game process {GameId}.");
            return false;
        }
        try
        {
            lock (_input)
            {
                _input.WriteLine(input);
                _input.Flush();
                Console.WriteLine($"[{DateTime.Now}] Sent {input} to game process {GameId}.");
                return true;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[{DateTime.Now}] {e}, failed Sending {input} to game process {GameId}.");
            return false;
        }
    }
    
    public List<string> GetOutput()
    {
        List<string> outputs = [];
        lock (_outputQueue)
        {
            while (_outputQueue.Count > 0)
            {
                outputs.Add(_outputQueue.Dequeue());
            }
        }
        return outputs;
    }
    
    public void Kill()
    {
        _running = false;
        try
        {
            if (!_proc.HasExited)
            {
                _proc.Kill(true);
                lock(_input) _input.Dispose();
                lock(_output) _output.Dispose();
                lock(_error) _error.Dispose();
                _proc.Dispose();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[{DateTime.Now}] {e}, failed killing game process {GameId}.");
        }
    }
}