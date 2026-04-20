using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace CodexBar.Runtime;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Semaphore _lock;
    private readonly bool _ownsLock;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task? _listenTask;

    public event Action<string[]>? ArgumentsReceived;

    public bool IsPrimary { get; }

    public SingleInstanceService(string name = "Local\\CodexBarWin")
    {
        _pipeName = BuildPipeName(name);
        _lock = new Semaphore(initialCount: 1, maximumCount: 1, name);
        _ownsLock = _lock.WaitOne(0);
        IsPrimary = _ownsLock;

        if (IsPrimary)
        {
            _listenTask = Task.Run(ListenLoopAsync);
        }
    }

    public bool TryNotifyPrimary(string[] args, int timeoutMs = 1500)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                var remaining = Math.Max(100, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                client.Connect(remaining);
                using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: false);
                writer.Write(JsonSerializer.Serialize(args));
                writer.Flush();
                return true;
            }
            catch when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
        }

        return false;
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try
        {
            _listenTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _shutdown.Dispose();
        }

        if (_ownsLock)
        {
            _lock.Release();
        }

        _lock.Dispose();
    }

    private async Task ListenLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await server.WaitForConnectionAsync(_shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var payload = await reader.ReadToEndAsync(_shutdown.Token);
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            try
            {
                var args = JsonSerializer.Deserialize<string[]>(payload) ?? [];
                ArgumentsReceived?.Invoke(args);
            }
            catch
            {
            }
        }
    }

    private static string BuildPipeName(string name)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return $"CodexBarWin-{Convert.ToHexString(bytes.AsSpan(0, 8))}";
    }
}
