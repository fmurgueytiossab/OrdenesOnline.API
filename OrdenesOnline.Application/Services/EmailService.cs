using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace OrdenesOnline.Application.Services;

public sealed class EmailService
{
    private readonly string _from;
    private readonly string _host;
    private readonly int _port;
    private readonly string _user;
    private readonly string _password;

    public EmailService(IConfiguration configuration)
    {
        _from = GetRequiredSetting(configuration, "Email:From");
        _host = GetRequiredSetting(configuration, "Email:Host");
        _user = GetRequiredSetting(configuration, "Email:User");
        _password = GetRequiredSetting(configuration, "Email:Pass");
        _port = int.TryParse(configuration["Email:Port"], out var port) && port > 0
            ? port
            : 587;
    }

    public async Task SendEmailAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Órdenes Online", _from));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = body };

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(_host, _port, SecureSocketOptions.StartTls, cancellationToken);
        await smtp.AuthenticateAsync(_user, _password, cancellationToken);
        await smtp.SendAsync(message, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
    }

    private static string GetRequiredSetting(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Falta la configuración obligatoria '{key}'.");
    }
}
