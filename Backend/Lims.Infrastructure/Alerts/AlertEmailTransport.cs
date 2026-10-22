using System.Net;
using System.Net.Mail;
using System.Text;
using Lims.Contracts.Alerts;

namespace Lims.Infrastructure.Alerts;

public sealed class AlertMailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Sender { get; set; } = "";
    public string Authentication { get; set; } = "Password";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string TransportSecurity { get; set; } = "StartTls";
    public int MaxAttempts { get; set; } = 5;
    public MailServiceStatus Status()
    {
        var valid = !string.IsNullOrWhiteSpace(Host) && Host.Length <= 253 && !Host.Any(char.IsWhiteSpace) &&
            Port is > 0 and <= 65535 && Port != 465 && MailAddress.TryCreate(Sender, out _) && TransportSecurity == "StartTls" &&
            Authentication is "None" or "Windows" or "Password" &&
            (Authentication != "Password" || !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password)) && MaxAttempts is >= 1 and <= 10;
        return new(Enabled, valid, Host, Port, Sender, Authentication, TransportSecurity,
            !Enabled ? "Deshabilitado en el servidor" : valid ? "Configurado; entrega aún debe confirmarse por SMTP" : "Pendiente: revise SMTP, STARTTLS y autenticación con el proveedor");
    }
}

public interface IAlertEmailTransport
{
    Task SendAsync(Guid deliveryId, string recipient, string subject, string html, CancellationToken ct);
}

public sealed class SmtpAlertEmailTransport(AlertMailOptions options) : IAlertEmailTransport
{
    public async Task SendAsync(Guid deliveryId, string recipient, string subject, string html, CancellationToken ct)
    {
        if (!options.Enabled || !options.Status().Configured) throw new InvalidOperationException("SMTP no está configurado.");
        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = true, Timeout = 30000, DeliveryMethod = SmtpDeliveryMethod.Network };
        client.UseDefaultCredentials = options.Authentication == "Windows";
        if (options.Authentication == "Password") client.Credentials = new NetworkCredential(options.Username, options.Password);
        using var message = new MailMessage(options.Sender, recipient, subject, html) { IsBodyHtml = true, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        message.Headers.Add("Message-ID", $"<{deliveryId:N}@{new MailAddress(options.Sender).Host}>");
        await client.SendMailAsync(message, ct).ConfigureAwait(false);
    }
}

public static class AlertEmailTemplate
{
    private static string Encode(string text) => WebUtility.HtmlEncode(text);
    public static string Render(string title, string introduction, IEnumerable<AlertItem> alerts)
    {
        var rows = string.Join("", alerts.OrderByDescending(a => a.Priority).ThenBy(a => a.Expiration).Select(a =>
            $"<tr><td style='padding:12px;border-bottom:1px solid #dce3eb'><strong>{Encode(a.Title)} — {Encode(a.Material)}</strong><br>" +
            $"{Encode(a.Description)}<br><small>{Encode(a.MaterialType)} · Código {Encode(a.Code)} · Lote {Encode(a.Lot)} · Prioridad {a.Priority} · Alerta {a.Id:D}</small><br>{Encode(a.Recommendation)}</td></tr>"));
        return "<!doctype html><html lang='es'><body style='margin:0;background:#edf2f7;font:14px Segoe UI,Arial;color:#172b40'>" +
            $"<div style='max-width:760px;margin:24px auto;background:white;border-radius:12px;overflow:hidden'><div style='padding:24px;background:#102536;color:white'><strong>LIMS · Alertas y Notificaciones</strong><h2>{Encode(title)}</h2></div>" +
            $"<div style='padding:24px'><p>{Encode(introduction)}</p><table role='presentation' style='width:100%;border-collapse:collapse'>{rows}</table><p>Abra LIMS → Alertas y busque el código o lote para consultar la ficha y su historial.</p><small>Leer este correo no resuelve la condición del inventario.</small></div></div></body></html>";
    }
}
