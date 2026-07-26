using System.Diagnostics;
using System.Text;

namespace WereMFServer;

internal sealed class GameProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly SemaphoreSlim _inputLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private Task _outputTask = Task.CompletedTask;
    private Task _errorTask = Task.CompletedTask;
    public event Func<string, Task>? OutputReceived;
    public event Func<int, Task>? Exited;

    public GameProcess(string executable, string? config, int? seed)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
        };
        info.ArgumentList.Add("--api");
        if (!string.IsNullOrWhiteSpace(config)) { info.ArgumentList.Add("--config"); info.ArgumentList.Add(config); }
        if (seed is not null) { info.ArgumentList.Add("--seed"); info.ArgumentList.Add(seed.Value.ToString()); }
        info.StandardInputEncoding = new UTF8Encoding(false);
        info.StandardOutputEncoding = new UTF8Encoding(false);
        info.StandardErrorEncoding = new UTF8Encoding(false);
        _process = Process.Start(info) ?? throw new InvalidOperationException("Unable to start WereMF.");
        _process.EnableRaisingEvents = true;
        _process.Exited += async (_, _) => { if (Exited is not null) await Exited(_process.ExitCode); };
    }

    public void Start()
    {
        _outputTask = PumpAsync(_process.StandardOutput, false, _shutdown.Token);
        _errorTask = PumpAsync(_process.StandardError, true, _shutdown.Token);
    }

    private async Task PumpAsync(StreamReader reader, bool error, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (error) Console.Error.WriteLine($"[WereMF] {line}");
            else if (OutputReceived is not null)
            {
                try { await OutputReceived(line); }
                catch (Exception ex) { Console.Error.WriteLine($"[WereMF route] {ex}"); }
            }
        }
    }

    public async Task SendAsync(string input, CancellationToken ct = default)
    {
        if (_process.HasExited) throw new InvalidOperationException("Game process has exited.");
        await _inputLock.WaitAsync(ct);
        try { await _process.StandardInput.WriteLineAsync(input); await _process.StandardInput.FlushAsync(ct); }
        finally { _inputLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (!_process.HasExited) { _process.Kill(true); await _process.WaitForExitAsync(); }
        try { await Task.WhenAll(_outputTask, _errorTask); } catch (OperationCanceledException) { }
        _process.Dispose(); _shutdown.Dispose(); _inputLock.Dispose();
    }
}
