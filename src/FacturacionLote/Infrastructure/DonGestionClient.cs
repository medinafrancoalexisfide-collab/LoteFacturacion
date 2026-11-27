using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FacturacionLote.Config;
using FacturacionLote.Domain;

namespace FacturacionLote.Infrastructure;

public sealed class DonGestionClient
{
    // static: un único HttpClient para todo el programa (crear uno por llamada agota conexiones)
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly AppSettings _s;
    private string? _token;           // el token se guarda para no pedir uno nuevo en cada llamada
    private DateTime _tokenVenceUtc;

    public DonGestionClient(AppSettings settings) => _s = settings;

    // URL base normalizada; rechaza http:// para que las credenciales no viajen sin cifrar
    private string Base
    {
        get
        {
            var url = _s.DgBaseUrl.Trim().TrimEnd('/');
            if (!url.Contains("://", StringComparison.Ordinal)) url = "https://" + url;
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("La URL de Don Gestión debe usar https:// (las credenciales no deben viajar sin cifrar).");
            return url;
        }
    }

    /// <summary>Pide un token de acceso (OAuth2 "password grant") y lo reutiliza hasta que venza.</summary>
    private async Task<string> ObtenerTokenAsync(CancellationToken ct)
    {
        if (_token != null && DateTime.UtcNow < _tokenVenceUtc) return _token;

        using var form = new MultipartFormDataContent
        {
            { new StringContent("password"), "grant_type" },
            { new StringContent(_s.DgClientId), "client_id" },
            { new StringContent(_s.DgClientSecret), "client_secret" },
            { new StringContent(_s.DgUsuario), "username" },
            { new StringContent(_s.DgPassword), "password" },
            { new StringContent(""), "scope" }
        };

        var urlToken = $"{Base}/oauth/token";
        using var resp = await Http.PostAsync(urlToken, form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            // Si la respuesta es una página web (empieza con "<"), no mostramos su HTML
            var detalle = body.TrimStart().StartsWith('<')
                ? "(la respuesta es una página web, no un mensaje de la API)"
                : Recortar(body);

            var pista = resp.StatusCode == HttpStatusCode.NotFound
                ? "Esa dirección no existe: revisá que la URL sea la de tu instancia (solo https://nombre.dongestion.com, sin rutas)."
                : "Revisá usuario, contraseña y client secret.";

            throw new InvalidOperationException(
                $"No se pudo autenticar en Don Gestión (HTTP {(int)resp.StatusCode}) llamando a {urlToken}. {pista} {detalle}");
        }

        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.GetProperty("access_token").GetString();
        if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("Don Gestión devolvió un token vacío.");

        // "expires_in" son los segundos de vida del token; restamos un minuto de margen
        var segundos = 3000;
        if (doc.RootElement.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var s) && s > 120)
            segundos = s - 60;

        _token = token;
        _tokenVenceUtc = DateTime.UtcNow.AddSeconds(segundos);
        return token;
    }

    /// <summary>GET autenticado. Si el token venció (401), pide otro y reintenta una vez.</summary>
    private async Task<string> GetAsync(string rutaYQuery, CancellationToken ct)
    {
        for (var intento = 0; intento < 2; intento++)
        {
            var token = await ObtenerTokenAsync(ct);
            using var req = new HttpRequestMessage(HttpMethod.Get, Base + rutaYQuery);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await Http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.Unauthorized && intento == 0)
            {
                _token = null;
                continue;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Don Gestión respondió HTTP {(int)resp.StatusCode}. {Recortar(body)}");
            return body;
        }
        throw new InvalidOperationException("Don Gestión rechazó la autenticación.");
    }

    public async Task<List<ClienteDg>> ListarClientesAsync(CancellationToken ct = default)
    {
        var json = await GetAsync("/connector/api/contactapi?type=customer&per_page=-1", ct);
        return JsonSerializer.Deserialize<RespuestaLista<ClienteDg>>(json, Json)?.Data ?? new List<ClienteDg>();
    }

    public async Task<List<VentaDg>> ListarVentasAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default)
    {
        var d = desde.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var h = hasta.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var json = await GetAsync($"/connector/api/sell?start_date={d}&end_date={h}&per_page=-1", ct);
        return JsonSerializer.Deserialize<RespuestaLista<VentaDg>>(json, Json)?.Data ?? new List<VentaDg>();
    }

    /// <summary>Devuelve el JSON sin procesar de un endpoint (sirve para diagnosticar).</summary>
    public Task<string> ObtenerCrudoAsync(string rutaYQuery, CancellationToken ct = default)
        => GetAsync(rutaYQuery, ct);

    private static string Recortar(string s) => s.Length <= 300 ? s : s[..300] + "…";
}