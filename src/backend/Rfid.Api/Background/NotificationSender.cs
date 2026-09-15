using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Rfid.Application.Services;
using Rfid.Domain;
using Rfid.Domain.Entities;

namespace Rfid.Api.Background;

/// <summary>SMTP e-mail, Twilio-compatible SMS, Microsoft Teams / Slack incoming webhooks and generic JSON webhooks.</summary>
public class HttpNotificationSender : INotificationSender
{
    private readonly HttpClient _http; private readonly ILogger<HttpNotificationSender> _log;
    public HttpNotificationSender(HttpClient http, ILogger<HttpNotificationSender> log) { _http = http; _log = log; _http.Timeout = TimeSpan.FromSeconds(20); }

    private static string? C(NotificationChannel ch, string key) => ch.Config.TryGetValue(key, out var v) && v != null ? v.ToString() : null;
    private static string Need(NotificationChannel ch, string key) => C(ch, key) ?? throw new InvalidOperationException($"Channel '{ch.Name}' is missing '{key}'");

    public async Task<string> SendAsync(NotificationChannel ch, NotificationMessage m, CancellationToken ct = default)
    {
        switch (ch.Kind)
        {
            case NotificationKind.Email: return await EmailAsync(ch, m, ct);
            case NotificationKind.Sms: return await SmsAsync(ch, m, ct);
            case NotificationKind.Teams: return await PostJsonAsync(Need(ch, "url"), TeamsCard(m), ct);
            case NotificationKind.Slack: return await PostJsonAsync(Need(ch, "url"), new { text = $"*{m.Subject}*\n{m.Body}" }, ct);
            case NotificationKind.Webhook: return await PostJsonAsync(Need(ch, "url"), new { m.Subject, m.Body, Severity = m.Severity.ToString(), m.AlertId, m.ItemId, m.Link, At = DateTime.UtcNow }, ct);
            default: throw new InvalidOperationException("Unsupported channel kind");
        }
    }

    private static object TeamsCard(NotificationMessage m) => new
    {
        type = "message",
        attachments = new[] { new { contentType = "application/vnd.microsoft.card.adaptive", content = new {
            type = "AdaptiveCard", version = "1.4", body = new object[] {
                new { type = "TextBlock", size = "Medium", weight = "Bolder", text = m.Subject, color = m.Severity == Severity.Critical ? "Attention" : m.Severity == Severity.Warning ? "Warning" : "Default" },
                new { type = "TextBlock", text = m.Body, wrap = true } },
            actions = m.Link == null ? Array.Empty<object>() : new object[] { new { type = "Action.OpenUrl", title = "Open RFID Platform", url = m.Link.TrimEnd('/') + "/alerts" } } } } },
    };

    private async Task<string> PostJsonAsync(string url, object payload, CancellationToken ct)
    {
        using var res = await _http.PostAsync(url, new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"), ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)res.StatusCode} from {new Uri(url).Host}");
        return new Uri(url).Host;
    }

    private async Task<string> SmsAsync(NotificationChannel ch, NotificationMessage m, CancellationToken ct)
    {
        // Twilio-compatible: POST {apiBase}/Accounts/{sid}/Messages.json with Basic auth (sid:token)
        var sid = Need(ch, "accountSid"); var token = Need(ch, "authToken"); var from = Need(ch, "from"); var to = Need(ch, "to");
        var apiBase = (C(ch, "apiBase") ?? "https://api.twilio.com/2010-04-01").TrimEnd('/');
        var text = m.Subject + (m.Body.Length > 0 ? "\n" + m.Body : ""); if (text.Length > 1500) text = text[..1500];
        foreach (var number in to.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{apiBase}/Accounts/{sid}/Messages.json") { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["From"] = from, ["To"] = number, ["Body"] = text }) };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{sid}:{token}")));
            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"SMS to {number}: HTTP {(int)res.StatusCode}");
        }
        return to;
    }

    private static async Task<string> EmailAsync(NotificationChannel ch, NotificationMessage m, CancellationToken ct)
    {
        var host = Need(ch, "host"); var port = int.TryParse(C(ch, "port"), out var p) ? p : 587; var to = Need(ch, "to"); var from = C(ch, "from") ?? "rfid-platform@localhost";
        using var client = new SmtpClient(host, port) { EnableSsl = !string.Equals(C(ch, "useTls"), "false", StringComparison.OrdinalIgnoreCase) };
        if (C(ch, "username") is { } user && user != "") client.Credentials = new NetworkCredential(user, C(ch, "password"));
        using var msg = new MailMessage { From = new MailAddress(from), Subject = m.Subject, Body = m.Body + (m.Link != null ? $"\n\n{m.Link.TrimEnd('/')}/alerts" : "") };
        foreach (var addr in to.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) msg.To.Add(addr);
        await client.SendMailAsync(msg, ct);
        return to;
    }
}
