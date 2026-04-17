using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CodexBar.Auth;

public sealed class LoopbackCallbackServer
{
    public const int DefaultPort = 1455;

    public async Task<ManualCallbackParseResult> WaitForCallbackAsync(
        string expectedState,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var listener = new TcpListener(IPAddress.Loopback, DefaultPort);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            throw new IOException("Could not bind localhost:1455; use manual paste fallback.", ex);
        }

        try
        {
            using var client = await listener.AcceptTcpClientAsync(timeoutCts.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                throw new IOException("Empty OAuth callback request.");
            }

            var requestTarget = ParseRequestTarget(requestLine);
            var result = ManualCallbackParser.Parse("http://localhost:1455" + requestTarget);
            const string html = "<html><body><h1>CodexBar login complete</h1><p>You can close this tab.</p></body></html>";
            await WriteResponseAsync(stream, "200 OK", html, timeoutCts.Token);

            return result;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string ParseRequestTarget(string requestLine)
    {
        var pieces = requestLine.Split(' ');
        if (pieces.Length < 2 || !pieces[0].Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Unsupported OAuth callback request.");
        }

        if (!pieces[1].StartsWith("/auth/callback", StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("OAuth callback path was not /auth/callback.");
        }

        return pieces[1];
    }

    private static async Task WriteResponseAsync(Stream stream, string status, string html, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
