namespace RoushTech.Rangifer.Mail;

using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

public interface IEmailTransport
{
    /// <returns>The server's response to the message, which some relays use to carry their message id.</returns>
    Task<string> SendAsync(MimeMessage message, CancellationToken cancellationToken = default);
}

public sealed class SmtpOptions
{
    public string? Host { get; set; }

    /// <summary>Null picks the default port for <see cref="Security"/>.</summary>
    public int? Port { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.Auto;

    public TimeSpan? Timeout { get; set; }
}

public sealed class SmtpEmailTransport : IEmailTransport
{
    private readonly Func<SmtpOptions> _options;

    public SmtpEmailTransport(SmtpOptions options)
        : this(() => options)
    {
    }

    /// <param name="options">Read on every send, so settings changed at runtime apply to the next message.</param>
    public SmtpEmailTransport(Func<SmtpOptions> options)
    {
        _options = options;
    }

    public async Task<string> SendAsync(MimeMessage message, CancellationToken cancellationToken = default)
    {
        var options = _options();
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new InvalidOperationException("SMTP host isn't set.");
        }

        using var client = new SmtpClient();
        if (options.Timeout is { } timeout)
        {
            client.Timeout = (int)timeout.TotalMilliseconds;
        }

        await client.ConnectAsync(options.Host, options.Port ?? 0, options.Security, cancellationToken);
        if (!string.IsNullOrWhiteSpace(options.UserName))
        {
            await client.AuthenticateAsync(options.UserName, options.Password ?? string.Empty, cancellationToken);
        }

        var response = await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
        return response;
    }
}
