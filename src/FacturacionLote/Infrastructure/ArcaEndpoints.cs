namespace FacturacionLote.Infrastructure;

/// <summary>Direcciones de los web services de ARCA y las reglas para aceptar una dirección personalizada.</summary>
public static class ArcaEndpoints
{
    public const string WsaaHomologacion = "https://wsaahomo.afip.gov.ar/ws/services/LoginCms";
    public const string WsfeHomologacion = "https://wswhomo.afip.gov.ar/wsfev1/service.asmx";
    public const string WsaaProduccion = "https://wsaa.afip.gov.ar/ws/services/LoginCms";
    public const string WsfeProduccion = "https://servicios1.afip.gov.ar/wsfev1/service.asmx";

    // Solo se aceptan dominios oficiales: si alguien modifica settings.json, no puede desviar
    // el pedido firmado ni los tokens hacia un servidor ajeno.
    private static readonly string[] DominiosPermitidos = { ".afip.gov.ar", ".arca.gob.ar" };

    /// <summary>
    /// Devuelve las direcciones a usar. Si hay una dirección personalizada, la valida:
    /// tiene que ser https, de un dominio oficial y coincidir con el ambiente
    /// (homologación = host con "homo"; producción = host sin "homo").
    /// Lanza InvalidOperationException si no cumple.
    /// </summary>
    public static (string Wsaa, string Wsfe) Resolver(bool produccion, string? wsaaPersonalizada, string? wsfePersonalizada)
    {
        var wsaa = string.IsNullOrWhiteSpace(wsaaPersonalizada)
            ? (produccion ? WsaaProduccion : WsaaHomologacion)
            : Validar(wsaaPersonalizada.Trim(), produccion, "ArcaUrlWsaa");

        var wsfe = string.IsNullOrWhiteSpace(wsfePersonalizada)
            ? (produccion ? WsfeProduccion : WsfeHomologacion)
            : Validar(wsfePersonalizada.Trim(), produccion, "ArcaUrlWsfe");

        return (wsaa, wsfe);
    }

    private static string Validar(string url, bool produccion, string nombreAjuste)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"{nombreAjuste} debe ser una dirección https:// válida.");

        var host = uri.Host.ToLowerInvariant();
        if (!DominiosPermitidos.Any(d => host.EndsWith(d, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"{nombreAjuste} apunta a un dominio no permitido ({host}). Solo se aceptan dominios oficiales de ARCA/AFIP.");

        var esHomologacion = host.Contains("homo", StringComparison.Ordinal);
        if (produccion && esHomologacion)
            throw new InvalidOperationException($"{nombreAjuste} es de homologación pero el ambiente configurado es PRODUCCION.");
        if (!produccion && !esHomologacion)
            throw new InvalidOperationException($"{nombreAjuste} es de producción pero el ambiente configurado es HOMOLOGACION.");

        return uri.AbsoluteUri;
    }
}
