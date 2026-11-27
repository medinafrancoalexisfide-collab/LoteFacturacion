using FacturacionLote.Config;
using FacturacionLote.Domain;
using FacturacionLote.Infrastructure;

namespace FacturacionLote.Services;

public sealed partial class BillingService
{
    private readonly AppSettings _s;
    private readonly DonGestionClient _dg;
    private readonly ArcaClient _arca;
    private readonly Database _db;
    private readonly FacturaPdfService _pdf;

    public BillingService(AppSettings settings, DonGestionClient dg, ArcaClient arca, Database db, FacturaPdfService pdf)
    {
        _s = settings;
        _dg = dg;
        _arca = arca;
        _db = db;
        _pdf = pdf;
    }

    /// <summary>
    /// Regla de negocio: se factura el servicio del mes anterior a la fecha de emisión,
    /// y el vencimiento es el último día del mes de emisión.
    /// Ej.: emisión 05/10 → servicio 01/09 al 30/09, vencimiento 31/10.
    /// </summary>
    public static (DateOnly Desde, DateOnly Hasta, DateOnly Vencimiento) CalcularPeriodo(DateOnly emision)
    {
        var primeroDelMes = new DateOnly(emision.Year, emision.Month, 1);
        return (primeroDelMes.AddMonths(-1),             // 1° del mes anterior
                primeroDelMes.AddDays(-1),               // último día del mes anterior
                primeroDelMes.AddMonths(1).AddDays(-1)); // último día del mes de emisión
    }

    /// <summary>Arma la lista de facturas posibles: clientes con ventas del mes anterior.</summary>
    public async Task<ResultadoPreparacion> PrepararLoteAsync(DateOnly emision, CancellationToken ct)
    {
        var (desde, hasta, _) = CalcularPeriodo(emision);

        var clientes = await _dg.ListarClientesAsync(ct);
        var ventas = await _dg.ListarVentasAsync(desde, hasta, ct);

        // Solo ventas finales (se excluyen borradores, cotizaciones, devoluciones).
        var ventasFinales = ventas
            .Where(v => string.IsNullOrEmpty(v.Status) || v.Status.Equals("final", StringComparison.OrdinalIgnoreCase))
            .Where(v => string.IsNullOrEmpty(v.Type) || v.Type.Equals("sell", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Índices para encontrar el cliente de cada venta: por código/CUIT y por ID interno
        var porId = new Dictionary<int, ClienteDg>();
        var porCodigo = new Dictionary<string, ClienteDg>();
        foreach (var c in clientes)
        {
            porId.TryAdd(c.Id, c);
            foreach (var codigo in new[] { c.ContactId, c.TaxNumber, c.DefaultCuit })
            {
                var clave = Clave(codigo);
                if (clave.Length > 0) porCodigo.TryAdd(clave, c);   // TryAdd: si ya existe, no pisa
            }
        }

        // Agrupamos las ventas por cliente
        var ventasPorCliente = new Dictionary<int, List<VentaDg>>();
        var sinAsociar = new List<VentaDg>();
        foreach (var v in ventasFinales)
        {
            var cliente = BuscarCliente(v, porCodigo, porId);
            if (cliente is null)
            {
                sinAsociar.Add(v);
                continue;
            }
            if (!ventasPorCliente.TryGetValue(cliente.Id, out var lista))
                ventasPorCliente[cliente.Id] = lista = new List<VentaDg>();
            lista.Add(v);
        }

        // Lo que ya sabemos de la base local
        var yaFacturadas = _db.ActivasDelPeriodo(_s.Ambiente, desde);
        var condiciones = _db.CondicionesIva();
        var selecciones = _db.Selecciones();

        var esInscripto = _s.EmisorResponsableInscripto;
        var filas = new List<FilaLote>();
        int sinVentas = 0, sugeridas = 0, sinCondicion = 0, ivaOk = 0, ivaSinDatos = 0, ivaDiferencias = 0;

        foreach (var c in clientes.OrderBy(c => c.NombreMostrar, StringComparer.CurrentCultureIgnoreCase))
        {
            if (!ventasPorCliente.TryGetValue(c.Id, out var susVentas))
            {
                sinVentas++;
                continue;
            }

            // Total con IVA incluido, tal como figura en Don Gestión
            var importe = Math.Round(susVentas.Sum(v => v.FinalTotal), 2);

            // Responsable Inscripto: se separa neto e IVA. Monotributista: Factura C, sin IVA discriminado.
            var (neto, iva) = esInscripto
                ? CalculoIva.Descomponer(importe, _s.AlicuotaIva)
                : (importe, 0m);

            // Control: el IVA que informa Don Gestión en los renglones debe coincidir con la alícuota configurada
            string? avisoIva = null;
            if (esInscripto)
            {
                var (resultadoIva, detectada) = RevisarIva(susVentas, _s.AlicuotaIva);
                if (resultadoIva == "Coincide") ivaOk++;
                else if (resultadoIva == "SinDatos") ivaSinDatos++;
                else
                {
                    ivaDiferencias++;
                    avisoIva = $"Revisar IVA: Don Gestión indica {detectada:0.##} % y en Configuración figura {_s.AlicuotaIva:0.##} %";
                }
            }

            // El CUIT puede estar en distintos campos: se usa el primero que sea un CUIT válido
            var cuit = Cuit.Resolver(c.TaxNumber, c.ContactId, c.DefaultCuit) ?? "";

            // Condición de IVA del cliente: la guardada; si no hay, la que sugiere el tipo de factura de Don Gestión
            int condIva;
            var sugerida = false;
            if (condiciones.TryGetValue(c.Id, out var guardada))
            {
                condIva = guardada;
            }
            else if (!esInscripto)
            {
                condIva = _s.CondIvaReceptorPorDefecto;
            }
            else
            {
                var letraDg = TiposComprobante.LetraDeTexto(c.DefaultInvoiceType);
                if (letraDg == "A") { condIva = 1; sugerida = true; }        // Factura A → Responsable Inscripto
                else if (letraDg == "B") { condIva = 5; sugerida = true; }   // Factura B → Consumidor Final
                else condIva = 0;                                            // sin dato: hay que elegirla
            }

            // ¿Se puede facturar? Si no, se explica por qué en la columna Estado
            var bloqueada = true;
            string estado;
            if (yaFacturadas.TryGetValue(c.Id, out var previa))
            {
                estado = previa.Estado == "Autorizada"
                    ? $"Ya facturada ({previa.Comprobante})"
                    : $"Pendiente de verificar en ARCA ({previa.Comprobante})";
            }
            else if (cuit.Length == 0)
            {
                estado = "Sin CUIT válido";
            }
            else if (importe <= 0)
            {
                estado = "Importe no válido";
            }
            else if (avisoIva != null)
            {
                estado = avisoIva;
            }
            else
            {
                bloqueada = false;
                if (condIva == 0) { estado = FilaLote.EstadoSinCondicion; sinCondicion++; }
                else if (sugerida) { estado = FilaLote.EstadoSugerida; sugeridas++; }
                else estado = "Pendiente";
            }

            // Selección guardada; si el cliente es nuevo (no figura), queda tildado
            var marcado = !selecciones.TryGetValue(c.Id, out var sel) || sel;

            filas.Add(new FilaLote
            {
                ClienteId = c.Id,
                Cliente = c.NombreMostrar,
                Cuit = cuit,
                Ventas = susVentas.Count,
                Neto = neto,
                Iva = iva,
                Importe = importe,
                EmisorInscripto = esInscripto,
                Bloqueada = bloqueada,
                MarcadoPorDefecto = marcado,
                CondIvaInicial = condIva,
                Facturar = !bloqueada && condIva != 0 && marcado,
                Estado = estado
            });
        }

        var resumen =
            $"Ventas finales del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}: {ventasFinales.Count} " +
            $"(asociadas a un cliente: {ventasFinales.Count - sinAsociar.Count}, sin asociar: {sinAsociar.Count}). " +
            $"Clientes en Don Gestión: {clientes.Count}. Con ventas: {filas.Count}. Sin ventas: {sinVentas}. " +
            $"No facturables: {filas.Count(f => f.Bloqueada)}.";

        if (esInscripto)
        {
            resumen += $" Facturas propuestas: A {filas.Count(f => f.TipoFactura == "A")}, B {filas.Count(f => f.TipoFactura == "B")}, " +
                       $"sin definir {sinCondicion}. Condición de IVA sugerida por Don Gestión (revisar): {sugeridas}. " +
                       $"Control de IVA al {_s.AlicuotaIva:0.##} %: coincide {ivaOk}, sin datos para controlar {ivaSinDatos}, con diferencias {ivaDiferencias}.";
        }

        // Diagnóstico (sin datos personales): describe la forma de los datos, no su contenido
        if (sinAsociar.Count > 0)
        {
            resumen += $" ATENCIÓN: hay ventas que no se pudieron asociar a un cliente. " +
                       $"contact_id de la venta: {Describir(sinAsociar[0].ContactId)}. " +
                       $"Clientes con contact_id cargado: {clientes.Count(c => !string.IsNullOrWhiteSpace(c.ContactId))}, " +
                       $"con tax_number: {clientes.Count(c => !string.IsNullOrWhiteSpace(c.TaxNumber))}, " +
                       $"con default_cuit: {clientes.Count(c => !string.IsNullOrWhiteSpace(c.DefaultCuit))}.";
        }

        return new ResultadoPreparacion(filas, resumen);
    }

    /// <summary>
    /// Compara el IVA de los renglones de Don Gestión con la alícuota configurada.
    /// Devuelve "Coincide", "SinDatos" (no hay renglones con precios para controlar) o "NoCoincide".
    /// </summary>
    private static (string Resultado, decimal? Detectada) RevisarIva(IEnumerable<VentaDg> ventas, decimal alicuotaEsperada)
    {
        decimal? distinta = null;
        var conDatos = false;

        foreach (var linea in ventas.SelectMany(v => v.SellLines ?? new List<LineaVentaDg>()))
        {
            if (linea.UnitPrice is not > 0m || linea.UnitPriceIncTax is null) continue;

            conDatos = true;
            var porcentaje = Math.Round((linea.UnitPriceIncTax.Value - linea.UnitPrice.Value) / linea.UnitPrice.Value * 100m, 1);
            if (Math.Abs(porcentaje - alicuotaEsperada) > 0.6m && distinta is null)
                distinta = porcentaje;
        }

        if (!conDatos) return ("SinDatos", null);
        return distinta is null ? ("Coincide", null) : ("NoCoincide", distinta);
    }

    /// <summary>Busca el cliente de una venta: primero por código/CUIT, después por ID interno.</summary>
    private static ClienteDg? BuscarCliente(VentaDg venta,
        Dictionary<string, ClienteDg> porCodigo, Dictionary<int, ClienteDg> porId)
    {
        var clave = Clave(venta.ContactId);
        if (clave.Length > 0 && porCodigo.TryGetValue(clave, out var porCod)) return porCod;

        if (venta.ClienteIdInterno is int id && porId.TryGetValue(id, out var porInterno)) return porInterno;

        return null;
    }

    /// <summary>Deja solo letras y números en mayúscula: "20-12345678-9" y "20123456789" quedan iguales.</summary>
    private static string Clave(string? texto) =>
        string.IsNullOrWhiteSpace(texto)
            ? ""
            : new string(texto.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>Describe la forma de un valor sin mostrarlo (para diagnóstico sin datos personales).</summary>
    private static string Describir(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "vacío";
        texto = texto.Trim();
        return texto.All(char.IsDigit) ? $"solo dígitos ({texto.Length})" : $"texto ({texto.Length} caracteres)";
    }
}
