using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FacturacionLote.Config;

/// <summary>Rutas donde la aplicación guarda sus datos (fuera de la carpeta del programa).</summary>
public static class AppPaths
{
    public static string DatosDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FacturacionLote");

    public static string SettingsPath => Path.Combine(DatosDir, "settings.json");
    public static string DbPath => Path.Combine(DatosDir, "datos.db");

    private static string DocumentosDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FacturacionLote");

    public static string FacturasDir(string ambiente) => Path.Combine(DocumentosDir, "Facturas", ambiente);
}

/// <summary>Cifra y descifra textos con DPAPI de Windows.
/// Solo el mismo usuario de Windows, en la misma PC, puede descifrarlos.</summary>
public static class Secretos
{
    /// <summary>Cifra bytes con DPAPI (mismo usuario de Windows, misma PC). Lanza excepción si falla.</summary>
    public static byte[] ProtegerBytes(byte[] datos) =>
        ProtectedData.Protect(datos, null, DataProtectionScope.CurrentUser);

    /// <summary>Descifra bytes cifrados con <see cref="ProtegerBytes"/>. Lanza CryptographicException si no se puede.</summary>
    public static byte[] DesprotegerBytes(byte[] cifrado) =>
        ProtectedData.Unprotect(cifrado, null, DataProtectionScope.CurrentUser);

    public static string? Proteger(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return null;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(texto), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string Desproteger(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(base64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception)
        {
            // Otro usuario/PC o archivo dañado: habrá que volver a cargar el secreto.
            return "";
        }
    }
}

public sealed class AppSettings
{
    // ---- Don Gestión ----
    public string DgBaseUrl { get; set; } = "";
    public string DgClientId { get; set; } = "1";
    public string DgUsuario { get; set; } = "";

    // Secretos: en memoria viven en texto normal, pero NO se guardan así en el archivo ([JsonIgnore]).
    [JsonIgnore] public string DgClientSecret { get; set; } = "";
    [JsonIgnore] public string DgPassword { get; set; } = "";
    [JsonIgnore] public string CertPassword { get; set; } = "";

    // ---- ARCA ----
    public long CuitEmisor { get; set; }
    public int PuntoVenta { get; set; } = 1;
    public string RutaCertificado { get; set; } = "";
    /// <summary>Condición IVA del receptor si el cliente no tiene una propia (5 = Consumidor Final).</summary>
    public int CondIvaReceptorPorDefecto { get; set; } = 5;
    /// <summary>Solo se cambia a mano en settings.json. Por defecto SIEMPRE homologación.</summary>
    public bool UsarProduccion { get; set; }
    /// <summary>Opcional, solo en settings.json: dirección alternativa del WSAA. Debe ser https, de un dominio
    /// oficial de ARCA/AFIP y coincidir con el ambiente. Vacío = la dirección oficial.</summary>
    public string ArcaUrlWsaa { get; set; } = "";
    /// <summary>true (recomendado) = en producción hay que escribir PRODUCCION (o ENVIAR para e-mails reales) para confirmar.
    /// false = se usa el cartel normal Sí/No. Solo se cambia a mano en settings.json.</summary>
    public bool ConfirmacionReforzada { get; set; } = true;
    /// <summary>Opcional, solo en settings.json: dirección alternativa del WSFE (mismas reglas que ArcaUrlWsaa).</summary>
    public string ArcaUrlWsfe { get; set; } = "";

    // ---- Datos del emisor (para el PDF) ----
    public string RazonSocial { get; set; } = "";
    public string Domicilio { get; set; } = "";
    /// <summary>true = el emisor es Responsable Inscripto (emite Factura A y B); false = Monotributista (emite Factura C).</summary>
    public bool EmisorResponsableInscripto { get; set; }
    /// <summary>Alícuota de IVA en porcentaje, para Responsable Inscripto (por defecto 21).</summary>
    public decimal AlicuotaIva { get; set; } = 21m;
    public string IngresosBrutos { get; set; } = "";
    public string InicioActividades { get; set; } = "";
    public string DescripcionServicio { get; set; } = "Servicio mensual";

    // ---- E-mail (SMTP) ----
    public string SmtpHost { get; set; } = "smtp.gmail.com";
    public int SmtpPort { get; set; } = 587;
    /// <summary>Usuario SMTP (en Gmail, la dirección completa).</summary>
    public string SmtpUsuario { get; set; } = "";
    /// <summary>Casilla de PRUEBA: mientras no se habilite el envío real, todas las facturas se envían acá.</summary>
    public string EmailPruebaDestino { get; set; } = "";
    /// <summary>Solo se cambia a mano en settings.json. Para enviar a clientes reales hace falta que ESTE valor
    /// y también UsarProduccion estén en true. Por defecto NUNCA se envía a clientes.</summary>
    public bool EmailEnviarAClientes { get; set; }

    [JsonIgnore] public string SmtpPassword { get; set; } = "";

    // ---- Versiones cifradas: esto es lo que realmente se escribe en el archivo ----
    [JsonPropertyName("DgClientSecretProtegido")]
    public string? DgClientSecretProtegido
    {
        get => Secretos.Proteger(DgClientSecret);
        set => DgClientSecret = Secretos.Desproteger(value);
    }

    [JsonPropertyName("DgPasswordProtegido")]
    public string? DgPasswordProtegido
    {
        get => Secretos.Proteger(DgPassword);
        set => DgPassword = Secretos.Desproteger(value);
    }

    [JsonPropertyName("CertPasswordProtegido")]
    public string? CertPasswordProtegido
    {
        get => Secretos.Proteger(CertPassword);
        set => CertPassword = Secretos.Desproteger(value);
    }

    [JsonPropertyName("SmtpPasswordProtegido")]
    public string? SmtpPasswordProtegido
    {
        get => Secretos.Proteger(SmtpPassword);
        set => SmtpPassword = Secretos.Desproteger(value);
    }

    /// <summary>Texto de la condición del emisor para el comprobante.</summary>
    [JsonIgnore] public string CondicionIvaEmisorTexto =>
        EmisorResponsableInscripto ? "IVA Responsable Inscripto" : "Responsable Monotributo";

    [JsonIgnore] public string Ambiente => UsarProduccion ? "PRODUCCION" : "HOMOLOGACION";

    /// <summary>Indica si ya se cargó lo mínimo para trabajar; si no, dice qué falta.</summary>
    public bool EstaCompleta(out string faltante)
    {
        if (string.IsNullOrWhiteSpace(DgBaseUrl)) { faltante = "URL de Don Gestión"; return false; }
        if (string.IsNullOrWhiteSpace(DgUsuario)) { faltante = "usuario de Don Gestión"; return false; }
        if (string.IsNullOrEmpty(DgPassword)) { faltante = "contraseña de Don Gestión"; return false; }
        if (string.IsNullOrEmpty(DgClientSecret)) { faltante = "client secret de Don Gestión"; return false; }
        if (CuitEmisor <= 0) { faltante = "CUIT del emisor"; return false; }
        if (PuntoVenta <= 0) { faltante = "punto de venta"; return false; }
        if (string.IsNullOrWhiteSpace(RutaCertificado)) { faltante = "certificado (.pfx)"; return false; }
        if (string.IsNullOrWhiteSpace(RazonSocial)) { faltante = "razón social del emisor"; return false; }
        faltante = "";
        return true;
    }

    /// <summary>Indica si ya se cargó lo mínimo para enviar e-mails; si no, dice qué falta.</summary>
    public bool EmailConfigurado(out string faltante)
    {
        if (string.IsNullOrWhiteSpace(SmtpHost)) { faltante = "servidor SMTP"; return false; }
        if (SmtpPort <= 0 || SmtpPort > 65535) { faltante = "puerto SMTP"; return false; }
        if (string.IsNullOrWhiteSpace(SmtpUsuario)) { faltante = "usuario SMTP (tu e-mail)"; return false; }
        if (string.IsNullOrEmpty(SmtpPassword)) { faltante = "contraseña SMTP (contraseña de aplicación)"; return false; }
        if (string.IsNullOrWhiteSpace(EmailPruebaDestino) && !(UsarProduccion && EmailEnviarAClientes))
        { faltante = "e-mail de prueba (casilla que recibe los envíos de prueba)"; return false; }
        faltante = "";
        return true;
    }
}

/// <summary>Lee y guarda la configuración en settings.json.</summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
            {
                var json = File.ReadAllText(AppPaths.SettingsPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<AppSettings>(json, Opciones) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // Archivo corrupto: se parte de una configuración vacía.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppPaths.DatosDir);
        File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(settings, Opciones), Encoding.UTF8);
    }
}