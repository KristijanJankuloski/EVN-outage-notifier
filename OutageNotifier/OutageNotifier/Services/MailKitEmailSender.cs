using System.Net;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using OutageNotifier.Configuration;
using OutageNotifier.Models;

namespace OutageNotifier.Services;

public sealed class MailKitEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public MailKitEmailSender(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendOutageNotificationAsync(IReadOnlyList<Outage> outages, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.From));
        foreach (var recipient in _options.To)
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }

        message.Subject = _options.Subject;
        message.Body = new TextPart(MimeKit.Text.TextFormat.Html)
        {
            Text = BuildHtmlBody(outages)
        };

        using var client = new SmtpClient();
        var socketOptions = _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;

        await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, socketOptions, cancellationToken);
        if (!string.IsNullOrEmpty(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private static string BuildHtmlBody(IReadOnlyList<Outage> outages)
    {
        var sb = new StringBuilder();
        sb.Append("<meta charset=\"utf-8\">");
        sb.Append("<h2>Нови известувања за прекини</h2>");
        sb.Append("<table border=\"1\" cellpadding=\"6\" cellspacing=\"0\">");
        sb.Append("<tr><th>Тип</th><th>Населено место</th><th>Адреса</th><th>Напонско ниво</th><th>Почеток</th><th>Крај</th></tr>");

        foreach (var outage in outages)
        {
            sb.Append("<tr>");
            sb.Append($"<td>{WebUtility.HtmlEncode(outage.TipPrekin)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(outage.NasMesto)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(outage.Adresa)}</td>");
            sb.Append($"<td>{WebUtility.HtmlEncode(outage.NapNivo)}</td>");
            sb.Append($"<td>{outage.Pocetok:yyyy-MM-dd HH:mm}</td>");
            sb.Append($"<td>{outage.Kraj:yyyy-MM-dd HH:mm}</td>");
            sb.Append("</tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }
}
