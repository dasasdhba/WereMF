using System.Diagnostics;

namespace WereMFServer;

public class GameProcess
{
    public Guid GameId { get; }
    private readonly Process _proc;
    private readonly StreamWriter _input;
    private readonly StreamReader _output;
    private readonly Queue<string> _outputQueue = new();
    private bool _running = true;
    
    public GameProcess(Guid gameId, Process proc)
    {
        GameId = gameId;
        Console.WriteLine($"[{DateTime.Now}] Game process {gameId} started.");
        
        _proc = proc;
        _input = proc.StandardInput;
        _output = proc.StandardOutput;
        
        var outputThread = new Thread(() =>
        {
            while (!_output.EndOfStream && _running)
            {
                var line = _output.ReadLine();
                if (line != null)
                {
                    lock (_outputQueue)
                    {
                        _outputQueue.Enqueue(line);
                    }
                }
            }
        });
        outputThread.Start();
    }
    
    public bool SendInput(string input)
    {
        if (_proc.HasExited) return false;
        try
        {
            _input.WriteLine(input);
            _input.Flush();
            Console.WriteLine($"[{DateTime.Now}] Sent {input} to game process {GameId}.");
            return true;
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
                _input.Dispose();
                _output.Dispose();
                _proc.Dispose();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[{DateTime.Now}] {e}, failed killing game process {GameId}.");
        }
    }
}