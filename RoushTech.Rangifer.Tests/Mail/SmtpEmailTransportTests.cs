namespace RoushTech.Rangifer.Tests.Mail;

using System.Net;
using System.Net.Sockets;
using System.Text;
using MailKit.Security;
using MimeKit;
using NUnit.Framework;
using RoushTech.Rangifer.Mail;

public class SmtpEmailTransportTests
{
    [Test]
    public async Task SendAsync_DeliversMessageAndReturnsServerResponse()
    {
        using var server = new FakeSmtpServer();
        var serverTask = server.AcceptOneAsync();
        var transport = new SmtpEmailTransport(new SmtpOptions { Host = "127.0.0.1", Port = server.Port, Security = SecureSocketOptions.None, Timeout = TimeSpan.FromSeconds(10) });
        var message = new MimeMessage { Subject = "Hello" };
        message.From.Add(new MailboxAddress("Sender", "sender@example.com"));
        message.To.Add(new MailboxAddress("Ada", "ada@example.com"));
        message.Body = new TextPart("html") { Text = "<p>Hi</p>" };
        OneClickUnsubscribe.AddTo(message, "https://example.com/u?t=1");

        var response = await transport.SendAsync(message);
        var data = await serverTask;

        Assert.Multiple(() =>
        {
            Assert.That(response, Does.Contain("queued as ABC123"));
            Assert.That(data, Does.Contain("Subject: Hello"));
            Assert.That(data, Does.Contain("List-Unsubscribe: <https://example.com/u?t=1>"));
            Assert.That(data, Does.Contain("<p>Hi</p>"));
        });
    }

    [Test]
    public void SendAsync_WithoutHost_Throws()
    {
        var transport = new SmtpEmailTransport(new SmtpOptions());

        Assert.That(() => transport.SendAsync(new MimeMessage()), Throws.InvalidOperationException);
    }

    [Test]
    public void SendAsync_ReadsOptionsOnEverySend()
    {
        var options = new SmtpOptions { Host = "localhost" };
        var transport = new SmtpEmailTransport(() => options);
        options = new SmtpOptions();

        Assert.That(() => transport.SendAsync(new MimeMessage()), Throws.InvalidOperationException.With.Message.Contains("host"));
    }

    // Just enough SMTP for one message: no extensions advertised, so the client sends commands one at a time.
    private sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

        public FakeSmtpServer() => _listener.Start();

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public async Task<string> AcceptOneAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            var data = new StringBuilder();

            await writer.WriteLineAsync("220 localhost ready");
            while (await reader.ReadLineAsync() is { } line)
            {
                var command = line.Split(' ')[0].ToUpperInvariant();
                switch (command)
                {
                    case "EHLO" or "HELO":
                        await writer.WriteLineAsync("250 localhost");
                        break;
                    case "DATA":
                        await writer.WriteLineAsync("354 go ahead");
                        while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                        {
                            data.AppendLine(dataLine);
                        }

                        await writer.WriteLineAsync("250 2.0.0 Ok: queued as ABC123");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 bye");
                        return data.ToString();
                    default:
                        await writer.WriteLineAsync("250 ok");
                        break;
                }
            }

            return data.ToString();
        }

        public void Dispose() => _listener.Stop();
    }
}
