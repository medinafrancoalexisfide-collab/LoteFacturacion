using System.Globalization;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using FacturacionLote.Config;

namespace FacturacionLote.Infrastructure;
/// <summary>Los datos de una factura que se le envían a ARCA.</summary>
public sealed record SolicitudFactura(
    int PtoVta, int CbteTipo, int Concepto, int DocTipo, long DocNro, int CondIvaReceptor,
    long Numero, DateOnly FechaEmision, decimal ImporteTotal, decimal ImporteNeto, decimal ImporteIva, int AlicuotaId,
    DateOnly ServDesde, DateOnly ServHasta, DateOnly VtoPago);

/// <summary>Respuesta definitiva de ARCA: aprobada (con CAE) o rechazada.</summary>
public sealed record ResultadoCae(bool Aprobado, string? Cae, DateOnly? CaeVto, string Mensaje);

/// <summary>Datos de un comprobante ya emitido, devueltos al consultarlo.</summary>
public sealed record ComprobanteArca(string? Cae, DateOnly? CaeVto, long DocNro, decimal ImpTotal);

/// <summary>Cliente mínimo de los web services de ARCA (WSAA + WSFEv1).</summary>
public sealed class ArcaClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private const string NsFe = "http://ar.gov.afip.dif.FEV1/";

    private readonly AppSettings _s;
    private (string Wsaa, string Wsfe)? _urls;

    private string? _token;
    private string? _sign;
    private DateTimeOffset _ticketVence;

    public ArcaClient(AppSettings settings)
    {
        _s = settings;
    }

    // Se calculan al usarlas (no al crear el cliente): si settings.json trae una dirección no permitida,
    // el error aparece como un mensaje normal y no cierra el programa al arrancar.
    private (string Wsaa, string Wsfe) Urls =>
        _urls ??= ArcaEndpoints.Resolver(_s.UsarProduccion, _s.ArcaUrlWsaa, _s.ArcaUrlWsfe);

    // ------------------------------------------------------------------
    //  WSAA (login)
    // ------------------------------------------------------------------

    // El ticket (token + sign) se guarda en un archivo para reutilizarlo hasta que venza.
    // ARCA entrega UN solo ticket vigente por CUIT y servicio: no hay que pedir uno nuevo en cada llamada.
    // El archivo se guarda CIFRADO con DPAPI (.dat): quien copie el archivo no puede usar el ticket.
    private string RutaTicket => Path.Combine(AppPaths.DatosDir, $"ta_{_s.Ambiente}_{_s.CuitEmisor}_wsfe.dat");

    private bool TryLeerTicketDeArchivo()
    {
        try
        {
            if (!File.Exists(RutaTicket)) return false;
            var xmlTicket = Encoding.UTF8.GetString(Secretos.DesprotegerBytes(File.ReadAllBytes(RutaTicket)));
            var doc = XmlSeguro.Parsear(xmlTicket);
            var token = Valor(doc.Root!, "token");
            var sign = Valor(doc.Root!, "sign");
            var exp = Valor(doc.Root!, "expirationTime");
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(sign)) return false;
            if (!DateTimeOffset.TryParse(exp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var vence)) return false;
            if (vence <= DateTimeOffset.UtcNow.AddMinutes(5)) return false; // vence pronto: pedimos otro
            _token = token;
            _sign = sign;
            _ticketVence = vence;
            return true;
        }
        catch (Exception)
        {
            return false; // archivo dañado o de otro usuario de Windows: se pide un ticket nuevo
        }
    }

    public async Task<(string Token, string Sign)> ObtenerTicketAsync(CancellationToken ct = default)
    {
        // 1) ¿Ya tenemos uno vigente en memoria o en el archivo?
        if (_token != null && _sign != null && _ticketVence > DateTimeOffset.UtcNow.AddMinutes(5))
            return (_token, _sign);
        if (TryLeerTicketDeArchivo()) return (_token!, _sign!);

        // 2) Si no, lo pedimos: armamos el pedido, lo firmamos con el certificado y lo enviamos
        var cms = FirmarTra(GenerarTra("wsfe"));
        var soap = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:wsa=""http://wsaa.view.sua.dvadac.desein.afip.gov.ar/"">
<soapenv:Header/>
<soapenv:Body><wsa:loginCms><wsa:in0>{cms}</wsa:in0></wsa:loginCms></soapenv:Body>
</soapenv:Envelope>";

        var (ok, body) = await PostSoapAsync(Urls.Wsaa, soap, "\"\"", ct);
        if (!ok) throw new InvalidOperationException($"WSAA rechazó la autenticación: {ExtraerFault(body)}");

        var ticketXml = Valor(XmlSeguro.Parsear(body).Root!, "loginCmsReturn")
            ?? throw new InvalidOperationException("WSAA no devolvió el ticket de acceso.");

        var ticket = XmlSeguro.Parsear(ticketXml);
        var token = Valor(ticket.Root!, "token");
        var sign = Valor(ticket.Root!, "sign");
        var exp = Valor(ticket.Root!, "expirationTime");
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(sign))
            throw new InvalidOperationException("No se pudo leer token/sign del ticket de acceso.");

        Directory.CreateDirectory(AppPaths.DatosDir);
        File.WriteAllBytes(RutaTicket, Secretos.ProtegerBytes(Encoding.UTF8.GetBytes(ticketXml)));

        _token = token;
        _sign = sign;
        _ticketVence = DateTimeOffset.TryParse(exp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var v)
            ? v : DateTimeOffset.UtcNow.AddHours(11);
        return (token, sign);
    }

    /// <summary>Arma el "Ticket de Requerimiento de Acceso" (TRA): el pedido de login en XML.</summary>
    private static string GenerarTra(string servicio)
    {
        // Hora de Argentina fija (UTC-3), sin depender de la zona horaria de la PC.
        var ahora = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).DateTime;
        var gen = ahora.AddMinutes(-10).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var exp = ahora.AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var id = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<loginTicketRequest version=""1.0"">
<header><uniqueId>{id}</uniqueId><generationTime>{gen}</generationTime><expirationTime>{exp}</expirationTime></header>
<service>{servicio}</service>
</loginTicketRequest>";
    }

    /// <summary>Firma el pedido con el certificado (.pfx). Así ARCA sabe que sos vos.</summary>
    private string FirmarTra(string traXml)
    {
        if (!File.Exists(_s.RutaCertificado))
            throw new FileNotFoundException($"No se encuentra el certificado: {_s.RutaCertificado}");

        using var cert = X509CertificateLoader.LoadPkcs12FromFile(
            _s.RutaCertificado, _s.CertPassword, X509KeyStorageFlags.DefaultKeySet);

        var cms = new SignedCms(new ContentInfo(new UTF8Encoding(false).GetBytes(traXml)));
        cms.ComputeSignature(new CmsSigner(cert) { IncludeOption = X509IncludeOption.EndCertOnly });
        return Convert.ToBase64String(cms.Encode());
    }

    // ------------------------------------------------------------------
    //  WSFE (facturación)
    // ------------------------------------------------------------------

    /// <summary>Último número de comprobante autorizado para un punto de venta y tipo de comprobante.</summary>
    public async Task<long> UltimoAutorizadoAsync(int ptoVta, int cbteTipo, CancellationToken ct = default)
    {
        var (token, sign) = await ObtenerTicketAsync(ct);
        var cuerpo = $"<ar:FECompUltimoAutorizado>{Auth(token, sign)}<ar:PtoVta>{ptoVta}</ar:PtoVta><ar:CbteTipo>{cbteTipo}</ar:CbteTipo></ar:FECompUltimoAutorizado>";
        var doc = await LlamarWsfeAsync("FECompUltimoAutorizado", cuerpo, ct);

        var errores = Errores(doc);
        if (errores.Count > 0) throw new InvalidOperationException("ARCA: " + string.Join(" | ", errores));

        return long.TryParse(Valor(doc.Root!, "CbteNro"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : throw new InvalidOperationException("ARCA no devolvió el último número de comprobante.");
    }

    /// <summary>
    /// Pide a ARCA que autorice una factura (A, B o C) y devuelve el resultado.
    /// Devuelve un resultado SOLO si ARCA respondió de forma clara (aprobada o rechazada).
    /// Ante un corte de red o una respuesta rara lanza una excepción: en ese caso el estado de la
    /// factura es INCIERTO y hay que verificarlo con ConsultarComprobanteAsync antes de reintentar.
    /// </summary>
    public async Task<ResultadoCae> SolicitarCaeAsync(SolicitudFactura r, CancellationToken ct = default)
    {
        var (token, sign) = await ObtenerTicketAsync(ct);
        var total = r.ImporteTotal.ToString("0.00", CultureInfo.InvariantCulture);
        var neto = r.ImporteNeto.ToString("0.00", CultureInfo.InvariantCulture);
        var iva = r.ImporteIva.ToString("0.00", CultureInfo.InvariantCulture);

        // Factura A y B: se informa el IVA (alícuota, base imponible e importe).
        // Factura C: sin IVA discriminado (ImpNeto = ImpTotal, ImpIVA = 0, sin nodo Iva).
        var ivaXml = r.AlicuotaId > 0
            ? $"<ar:Iva><ar:AlicIva><ar:Id>{r.AlicuotaId}</ar:Id><ar:BaseImp>{neto}</ar:BaseImp><ar:Importe>{iva}</ar:Importe></ar:AlicIva></ar:Iva>"
            : "";

        var cuerpo = $@"<ar:FECAESolicitar>{Auth(token, sign)}
<ar:FeCAEReq>
<ar:FeCabReq><ar:CantReg>1</ar:CantReg><ar:PtoVta>{r.PtoVta}</ar:PtoVta><ar:CbteTipo>{r.CbteTipo}</ar:CbteTipo></ar:FeCabReq>
<ar:FeDetReq><ar:FECAEDetRequest>
<ar:Concepto>{r.Concepto}</ar:Concepto>
<ar:DocTipo>{r.DocTipo}</ar:DocTipo>
<ar:DocNro>{r.DocNro}</ar:DocNro>
<ar:CondicionIVAReceptorId>{r.CondIvaReceptor}</ar:CondicionIVAReceptorId>
<ar:CbteDesde>{r.Numero}</ar:CbteDesde>
<ar:CbteHasta>{r.Numero}</ar:CbteHasta>
<ar:CbteFch>{Fecha(r.FechaEmision)}</ar:CbteFch>
<ar:ImpTotal>{total}</ar:ImpTotal>
<ar:ImpTotConc>0.00</ar:ImpTotConc>
<ar:ImpNeto>{neto}</ar:ImpNeto>
<ar:ImpOpEx>0.00</ar:ImpOpEx>
<ar:ImpTrib>0.00</ar:ImpTrib>
<ar:ImpIVA>{iva}</ar:ImpIVA>
<ar:FchServDesde>{Fecha(r.ServDesde)}</ar:FchServDesde>
<ar:FchServHasta>{Fecha(r.ServHasta)}</ar:FchServHasta>
<ar:FchVtoPago>{Fecha(r.VtoPago)}</ar:FchVtoPago>
<ar:MonId>PES</ar:MonId>
<ar:MonCotiz>1</ar:MonCotiz>
{ivaXml}
</ar:FECAEDetRequest></ar:FeDetReq>
</ar:FeCAEReq>
</ar:FECAESolicitar>";

        var doc = await LlamarWsfeAsync("FECAESolicitar", cuerpo, ct);

        // Leemos la respuesta: resultado ("A" = aprobada), CAE, vencimiento del CAE y mensajes
        var det = Elementos(doc.Root!, "FECAEDetResponse").FirstOrDefault();
        var mensajes = new List<string>(Errores(doc));
        if (det != null)
            foreach (var o in Elementos(det, "Obs"))
                mensajes.Add($"[{Valor(o, "Code")}] {Valor(o, "Msg")}");

        var resultado = det == null ? null : Valor(det, "Resultado");
        var cae = det == null ? null : Valor(det, "CAE");
        var vto = det == null ? null : Valor(det, "CAEFchVto");

        if (resultado == "A" && !string.IsNullOrWhiteSpace(cae))
            return new ResultadoCae(true, cae, ParseFecha(vto), string.Join(" | ", mensajes));

        if (det == null && mensajes.Count == 0)
            throw new InvalidOperationException("La respuesta de ARCA no se pudo interpretar.");

        return new ResultadoCae(false, null, null,
            mensajes.Count > 0 ? string.Join(" | ", mensajes) : "Rechazada por ARCA sin detalle.");
    }

    /// <summary>Consulta un comprobante ya emitido. Devuelve null si ARCA informa que no existe.</summary>
    public async Task<ComprobanteArca?> ConsultarComprobanteAsync(int ptoVta, int cbteTipo, long numero, CancellationToken ct = default)
    {
        var (token, sign) = await ObtenerTicketAsync(ct);
        var cuerpo = $"<ar:FECompConsultar>{Auth(token, sign)}<ar:FeCompConsReq><ar:CbteTipo>{cbteTipo}</ar:CbteTipo><ar:CbteNro>{numero}</ar:CbteNro><ar:PtoVta>{ptoVta}</ar:PtoVta></ar:FeCompConsReq></ar:FECompConsultar>";
        var doc = await LlamarWsfeAsync("FECompConsultar", cuerpo, ct);

        var res = Elementos(doc.Root!, "ResultGet").FirstOrDefault();
        if (res == null)
        {
            var errs = Elementos(doc.Root!, "Err").ToList();
            if (errs.Any(e => Valor(e, "Code") == "602")) return null; // 602 = "No existen datos en nuestros registros"
            throw new InvalidOperationException("ARCA: " + (errs.Count > 0
                ? string.Join(" | ", Errores(doc))
                : "respuesta no interpretable al consultar el comprobante."));
        }

        _ = long.TryParse(Valor(res, "DocNro"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var docNro);
        _ = decimal.TryParse(Valor(res, "ImpTotal"), NumberStyles.Number, CultureInfo.InvariantCulture, out var impTotal);
        return new ComprobanteArca(Valor(res, "CodAutorizacion"), ParseFecha(Valor(res, "FchVto")), docNro, impTotal);
    }

    // ------------------------------------------------------------------
    //  Utilidades
    // ------------------------------------------------------------------

    private string Auth(string token, string sign) =>
        $"<ar:Auth><ar:Token>{token}</ar:Token><ar:Sign>{sign}</ar:Sign><ar:Cuit>{_s.CuitEmisor}</ar:Cuit></ar:Auth>";

    private async Task<XDocument> LlamarWsfeAsync(string accion, string cuerpo, CancellationToken ct)
    {
        var envelope = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:ar=""{NsFe}"">
<soap:Header/>
<soap:Body>{cuerpo}</soap:Body>
</soap:Envelope>";

        var (ok, body) = await PostSoapAsync(Urls.Wsfe, envelope, NsFe + accion, ct);
        if (!ok) throw new InvalidOperationException($"WSFE ({accion}) respondió con error: {ExtraerFault(body)}");
        return XmlSeguro.Parsear(body);
    }

    private static async Task<(bool Ok, string Body)> PostSoapAsync(string url, string xml, string soapAction, CancellationToken ct)
    {
        using var content = new StringContent(xml, new UTF8Encoding(false), "text/xml");
        content.Headers.Add("SOAPAction", soapAction);
        using var resp = await Http.PostAsync(url, content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        return (resp.IsSuccessStatusCode, body);
    }

    // Los XML de ARCA usan "namespaces"; estas dos funciones buscan elementos solo por su nombre local.
    private static IEnumerable<XElement> Elementos(XElement raiz, string nombreLocal) =>
        raiz.Descendants().Where(e => e.Name.LocalName == nombreLocal);

    private static string? Valor(XElement raiz, string nombreLocal) =>
        Elementos(raiz, nombreLocal).FirstOrDefault()?.Value;

    private static List<string> Errores(XDocument doc) =>
        Elementos(doc.Root!, "Err").Select(e => $"[{Valor(e, "Code")}] {Valor(e, "Msg")}").ToList();

    private static string ExtraerFault(string body)
    {
        try
        {
            var f = Valor(XmlSeguro.Parsear(body).Root!, "faultstring");
            if (!string.IsNullOrWhiteSpace(f)) return f;
        }
        catch (Exception)
        {
            // el cuerpo no era XML
        }
        return body.Length <= 300 ? body : body[..300] + "…";
    }

    // ARCA usa fechas con formato yyyyMMdd (ej.: 20260930)
    private static string Fecha(DateOnly d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static DateOnly? ParseFecha(string? s) =>
        DateOnly.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}