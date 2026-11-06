using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Starter.Modules.Notifications.Tests.Infrastructure;

/// <summary>One message as the relay received it.</summary>
/// <param name="MailFrom">The MAIL FROM path, as sent (<c>&lt;address&gt;</c>).</param>
/// <param name="Recipients">The RCPT TO paths.</param>
/// <param name="Data">The DATA section (headers, blank line, body), dot-unstuffed.</param>
internal sealed record SmtpReceived(string MailFrom, IReadOnlyList<string> Recipients, string Data);

/// <summary>
/// A minimal in-process SMTP relay on the loopback interface (an ephemeral port) for the SMTP sender's success path:
/// EHLO/HELO, MAIL, RCPT, DATA, RSET, NOOP and QUIT. It never offers STARTTLS (a STARTTLS command gets 454) and needs
/// no credentials, like the internal relay. Each accepted message is recorded in <see cref="Received"/>.
/// </summary>
internal sealed class SmtpStub : IAsyncDisposable
{
    /// <summary>The address the stub listens on.</summary>
    public const string Host = "127.0.0.1";

    private const string CrLf = "\r\n";
    private const string EndOfData = ".";
    private const int VerbLength = 4;

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private SmtpStub(TcpListener listener)
    {
        _listener = listener;
        _loop = Task.Run(AcceptLoopAsync);
    }

    /// <summary>The port the stub listens on.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>The messages accepted so far.</summary>
    public ConcurrentQueue<SmtpReceived> Received { get; } = new();

    /// <summary>Starts listening on an ephemeral loopback port.</summary>
    public static SmtpStub Start()
    {
        var listener = new TcpListener(IPAddress.Parse(Host), port: 0);
        listener.Start();
        return new SmtpStub(listener);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
            // stopping
        }
        catch (SocketException)
        {
            // the listener was stopped while accepting
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            try
            {
                await ServeAsync(client.GetStream());
            }
            catch (IOException)
            {
                // the client hung up (for example after the missing STARTTLS)
            }
        }
    }

    private async Task ServeAsync(NetworkStream stream)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = CrLf, AutoFlush = true };
        await writer.WriteLineAsync("220 stub ESMTP");
        var session = new Session();
        while (await reader.ReadLineAsync(_stop.Token) is { } line)
        {
            if (!await AnswerAsync(line, session, reader, writer))
            {
                return;
            }
        }
    }

    private async Task<bool> AnswerAsync(string line, Session session, StreamReader reader, StreamWriter writer)
    {
        var verb = (line.Length >= VerbLength ? line[..VerbLength] : line).ToUpperInvariant();
        switch (verb)
        {
            case "EHLO":
                await writer.WriteLineAsync("250-stub");
                await writer.WriteLineAsync("250 8BITMIME");
                return true;
            case "MAIL":
                session.Reset(PathOf(line));
                await writer.WriteLineAsync("250 OK");
                return true;
            case "RCPT":
                session.Recipients.Add(PathOf(line));
                await writer.WriteLineAsync("250 OK");
                return true;
            case "DATA":
                await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                Received.Enqueue(new SmtpReceived(session.MailFrom, [.. session.Recipients], await ReadDataAsync(reader)));
                await writer.WriteLineAsync("250 OK queued");
                return true;
            case "QUIT":
                await writer.WriteLineAsync("221 Bye");
                return false;
            case "STAR":
                await writer.WriteLineAsync("454 TLS not available");
                return true;
            default:
                await writer.WriteLineAsync(verb is "HELO" or "NOOP" or "RSET" ? "250 OK" : "502 Command not implemented");
                return true;
        }
    }

    private static string PathOf(string line) => line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim();

    private async Task<string> ReadDataAsync(StreamReader reader)
    {
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(_stop.Token) is { } line && line != EndOfData)
        {
            data.Append(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line).Append(CrLf);
        }

        return data.ToString();
    }

    private sealed class Session
    {
        public string MailFrom { get; private set; } = string.Empty;

        public List<string> Recipients { get; } = [];

        public void Reset(string mailFrom)
        {
            MailFrom = mailFrom;
            Recipients.Clear();
        }
    }
}
