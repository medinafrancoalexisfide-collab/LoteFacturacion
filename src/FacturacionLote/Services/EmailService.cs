using System.Globalization;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using FacturacionLote.Config;
using FacturacionLote.Domain;

namespace FacturacionLote.Services;

/// <summary>
/// Envía las facturas por e-mail (SMTP, con MailKit).
/// REGLA DE SEGURIDAD: mientras EnviarAClientesReales sea false, TODO se envía a la casilla de prueba
/// y nunca al cliente. Para activar el envío real hacen falta dos cosas en settings.json:
/// "UsarProduccion": true y "EmailEnviarAClientes": true.
/// </summary>
public sealed class EmailService
{
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");
    private readonly AppSettings _s;

    public EmailService(AppSettings settings) => _s = settings;

    /// <summary>true solo si se habilitó el envío real Y se está en producción.</summary>
    public bool EnviarAClientesReales => _s.UsarProduccion && _s.EmailEnviarAClientes;

    /// <summary>"REAL" o "PRUEBA": se guarda junto con cada envío.</summary>
    public string Modo => EnviarAClientesReales ? "REAL" : "PRUEBA";

    /// <summary>
    /// Analiza el e-mail que viene de Don Gestión (puede traer varios, separados por coma o punto y coma).
    /// Devuelve si es válido y el texto normalizado ("a@x.com, b@y.com").
    /// </summary>
    public static (bool Valido, string Normalizado) AnalizarEmail(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return (false, "");

        var limpio = texto.Replace(';', ',').Trim();
        if (InternetAddressList.TryParse(limpio, out var lista) && lista.Mailboxes.Any())
            return (true, string.Join(", ", lista.Mailboxes.Select(m => m.Address)));

        return (false, texto.Trim());
    }

    /// <summary>A qué casilla se enviará realmente la factura de un cliente.</summary>
    public string DestinoReal(string emailClienteNormalizado) =>
        EnviarAClientesReales ? emailClienteNormalizado : _s.EmailPruebaDestino.Trim();

    /// <summary>Envía una factura (con su PDF adjunto). Lanza una excepción si algo falla.</summary>
    public async Task EnviarFacturaAsync(FacturaRow f, string emailClienteNormalizado, string rutaPdf,
        CancellationToken ct = default)
    {
        Validar();
        if (!File.Exists(rutaPdf))
            throw new FileNotFoundException($"No se encuentra el PDF de la factura: {rutaPdf}");

        var destino = DestinoReal(emailClienteNormalizado);
        var msg = CrearMensaje(destino);
        var enPrueba = !EnviarAClientesReales;

        var letra = TiposComprobante.LetraDeCodigo(f.CbteTipo);
        msg.Subject = (enPrueba ? "[PRUEBA] " : "") + $"Factura {letra} {f.Comprobante} - {_s.RazonSocial}";

        var texto = new StringBuilder();
        if (enPrueba)
        {
            texto.AppendLine("*** MENSAJE DE PRUEBA ***");
            texto.AppendLine($"Email del cliente: {emailClienteNormalizado}");
            texto.AppendLine("Este mensaje NO se envió al cliente: llegó a esta casilla de prueba.");
            texto.AppendLine();
        }
        texto.AppendLine($"Estimado/a {f.Cliente}:");
        texto.AppendLine();
        texto.AppendLine($"Le enviamos la Factura {letra} Nº {f.Comprobante}, correspondiente al servicio del período " +
                         $"{f.PeriodoDesde:dd/MM/yyyy} al {f.PeriodoHasta:dd/MM/yyyy}.");
        texto.AppendLine($"Importe total: $ {f.Importe.ToString("N2", Ar)}");
        texto.AppendLine($"Vencimiento del pago: {f.FechaVtoPago:dd/MM/yyyy}");
        texto.AppendLine();
        texto.AppendLine("El comprobante se encuentra adjunto en formato PDF.");
        texto.AppendLine();
        texto.AppendLine("Saludos cordiales,");
        texto.AppendLine(_s.RazonSocial);

        var cuerpo = new BodyBuilder { TextBody = texto.ToString() };
        cuerpo.Attachments.Add(rutaPdf);
        msg.Body = cuerpo.ToMessageBody();

        await EnviarAsync(msg, ct);
    }

    /// <summary>Envía un mensaje corto, sin factura, a la casilla de prueba (sirve para verificar la configuración SMTP).</summary>
    public async Task EnviarPruebaAsync(CancellationToken ct = default)
    {
        Validar();
        if (string.IsNullOrWhiteSpace(_s.EmailPruebaDestino))
            throw new InvalidOperationException("Falta cargar el e-mail de prueba en Configuración.");

        var msg = CrearMensaje(_s.EmailPruebaDestino.Trim());
        msg.Subject = "[PRUEBA] Prueba de envío de e-mail";
        msg.Body = new TextPart("plain")
        {
            Text = "Este es un mensaje de prueba del programa de facturación por lote.\r\n" +
                   "Si lo estás leyendo, la configuración de e-mail funciona."
        };
        await EnviarAsync(msg, ct);
    }

    private void Validar()
    {
        if (!_s.EmailConfigurado(out var falta))
            throw new InvalidOperationException($"Falta completar en Configuración (e-mail): {falta}.");
    }

    private MimeMessage CrearMensaje(string destino)
    {
        // El remitente es la propia cuenta de correo configurada
        var remitente = _s.SmtpUsuario.Trim();
        if (!MailboxAddress.TryParse(remitente, out _))
            throw new InvalidOperationException("El usuario de e-mail debe ser una dirección completa (por ejemplo, nombre@gmail.com).");
        var nombre = string.IsNullOrWhiteSpace(_s.RazonSocial) ? "Facturación" : _s.RazonSocial.Trim();

        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(nombre, remitente));

        if (!InternetAddressList.TryParse(destino.Replace(';', ','), out var lista) || !lista.Mailboxes.Any())
            throw new InvalidOperationException($"La casilla de destino no es válida: \"{destino}\".");
        msg.To.AddRange(lista.Mailboxes);
        return msg;
    }

    private async Task EnviarAsync(MimeMessage msg, CancellationToken ct)
    {
        using var client = new SmtpClient();
        client.Timeout = 30000;   // 30 segundos
        await client.ConnectAsync(_s.SmtpHost.Trim(), _s.SmtpPort, SecureSocketOptions.Auto, ct);
        await client.AuthenticateAsync(_s.SmtpUsuario.Trim(), _s.SmtpPassword, ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
}
