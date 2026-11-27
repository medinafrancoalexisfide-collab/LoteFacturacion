using FacturacionLote.Domain;
using FacturacionLote.Infrastructure;

namespace FacturacionLote.Services;

/// <summary>Resultado final de un lote, para mostrarlo al usuario.</summary>
public sealed record ResumenLote(int Emitidas, int Errores, int Inciertas, bool Cancelado);

public sealed partial class BillingService
{
    // ------------------------------------------------------------------
    //  Emisión del lote
    // ------------------------------------------------------------------

    public async Task<ResumenLote> EmitirLoteAsync(IReadOnlyList<FilaLote> filas, DateOnly emision,
        IProgress<string> log, CancellationToken cancelacion)
    {
        // Antes de emitir nada, se aclaran los comprobantes que hayan quedado en estado incierto antes.
        await ReconciliarEnviandoAsync(log);
        if (_db.ListarEnviando(_s.Ambiente).Count > 0)
            throw new InvalidOperationException(
                "Hay comprobantes en estado incierto que todavía no se pudieron verificar con ARCA. " +
                "Reintentá cuando haya conexión.");

        var (desde, hasta, vto) = CalcularPeriodo(emision);
        var aEmitir = filas.Where(f => f.Facturar && f.Habilitada).ToList();
        var loteId = _db.CrearLote(_s.Ambiente, emision, desde, hasta, vto, aEmitir.Count);

        int emitidas = 0, errores = 0, inciertas = 0;
        var cancelado = false;

        foreach (var fila in aEmitir)
        {
            // El usuario puede cancelar, pero solo ENTRE facturas, nunca a mitad de una.
            if (cancelacion.IsCancellationRequested)
            {
                cancelado = true;
                log.Report("Lote cancelado por el usuario. Las facturas restantes no se emitieron.");
                break;
            }

            fila.Estado = "Emitiendo…";
            log.Report($"→ {fila.Cliente}: emitiendo…");

            try
            {
                // Tipo de comprobante (A, B o C) y alícuota de IVA (solo Responsable Inscripto)
                var cbteTipo = TiposComprobante.CodigoArca(fila.TipoFactura);
                if (cbteTipo == 0)
                    throw new InvalidOperationException("Falta definir el tipo de factura (condición de IVA del cliente).");

                var alicuota = _s.EmisorResponsableInscripto ? _s.AlicuotaIva : 0m;
                var alicuotaId = _s.EmisorResponsableInscripto ? Catalogos.IdAlicuota(alicuota) : 0;
                if (_s.EmisorResponsableInscripto && alicuotaId == 0)
                    throw new InvalidOperationException($"La alícuota de IVA {alicuota:0.##} % no es una de las admitidas por ARCA.");

                // 1) Siguiente número = último autorizado + 1 (cada tipo de factura tiene su propia numeración)
                var numero = await _arca.UltimoAutorizadoAsync(_s.PuntoVenta, cbteTipo) + 1;

                // 2) Guardamos la factura como "Enviando" ANTES de pedir el CAE
                var id = _db.ReservarFactura(loteId, _s.Ambiente, fila.ClienteId, fila.Cliente, fila.Cuit, fila.CondIva,
                    fila.Importe, fila.Neto, fila.Iva, alicuota, _s.PuntoVenta, cbteTipo, numero, desde, hasta, vto, emision);

                if (id is null)
                {
                    fila.Estado = "Ya facturada (omitida)";
                    log.Report($"  {fila.Cliente}: ya tiene una factura para este período; se omite.");
                    continue;
                }

                // 3) Pedimos el CAE a ARCA
                var solicitud = new SolicitudFactura(
                    _s.PuntoVenta, cbteTipo, Catalogos.ConceptoServicios, Catalogos.DocTipoCuit,
                    long.Parse(fila.Cuit), fila.CondIva, numero, emision,
                    fila.Importe, fila.Neto, fila.Iva, alicuotaId, desde, hasta, vto);

                ResultadoCae? resultado;
                try
                {
                    resultado = await _arca.SolicitarCaeAsync(solicitud);
                }
                catch (Exception exEnvio)
                {
                    // Sin respuesta clara (corte, timeout…): NO reintentamos a ciegas; preguntamos a ARCA.
                    log.Report($"  Sin respuesta clara de ARCA ({exEnvio.Message}). Verificando si el comprobante {numero} se autorizó…");
                    var registro = _db.Obtener(id.Value)!;
                    resultado = await VerificarEnArcaAsync(registro);
                }

                if (resultado is null)
                {
                    // No se pudo emitir ni verificar: se frena el lote para no arriesgar números ni duplicados.
                    inciertas++;
                    fila.Estado = "INCIERTA: verificar en ARCA";
                    log.Report($"  {fila.Cliente}: estado incierto. Se detiene el lote; al volver a emitir se verificará con ARCA.");
                    break;
                }

                // 4) Guardamos el resultado
                if (resultado.Aprobado)
                {
                    _db.MarcarAutorizada(id.Value, resultado.Cae!, resultado.CaeVto);
                    // ARCA aceptó la factura con esta condición de IVA: queda confirmada para los próximos meses
                    _db.GuardarCondIva(fila.ClienteId, fila.CondIva);
                    emitidas++;
                    fila.Estado = $"Autorizada ({fila.TipoFactura} {_s.PuntoVenta:D5}-{numero:D8})";
                    log.Report($"  {fila.Cliente}: autorizada (Factura {fila.TipoFactura}), CAE {resultado.Cae}.");
                    GenerarPdfSeguro(id.Value, log);
                }
                else
                {
                    _db.MarcarError(id.Value, resultado.Mensaje);
                    errores++;
                    fila.Estado = "Rechazada: " + Corto(resultado.Mensaje);
                    log.Report($"  {fila.Cliente}: rechazada por ARCA. {resultado.Mensaje}");
                }
            }
            catch (Exception ex)
            {
                // Un error en un cliente no frena a los demás
                errores++;
                fila.Estado = "Error: " + Corto(ex.Message);
                log.Report($"  {fila.Cliente}: error. {ex.Message}");
            }
        }

        return new ResumenLote(emitidas, errores, inciertas, cancelado);
    }

    // ------------------------------------------------------------------
    //  Verificación de comprobantes inciertos
    // ------------------------------------------------------------------

    /// <summary>Verifica con ARCA las facturas que quedaron en "Enviando" (por un corte, cierre del programa, etc.).</summary>
    public async Task<int> ReconciliarEnviandoAsync(IProgress<string> log)
    {
        var pendientes = _db.ListarEnviando(_s.Ambiente);
        var resueltas = 0;

        foreach (var f in pendientes)
        {
            try
            {
                log.Report($"Verificando comprobante {f.Comprobante} ({f.Cliente}) en ARCA…");
                var res = await VerificarEnArcaAsync(f);
                if (res is null)
                {
                    log.Report("  No se pudo verificar (sin conexión o respuesta ambigua). Queda pendiente.");
                    continue;
                }

                if (res.Aprobado)
                {
                    _db.MarcarAutorizada(f.Id, res.Cae!, res.CaeVto);
                    log.Report($"  Estaba autorizada (CAE {res.Cae}). Registrada.");
                    GenerarPdfSeguro(f.Id, log);
                }
                else
                {
                    _db.MarcarError(f.Id, res.Mensaje);
                    log.Report($"  No quedó autorizada: {res.Mensaje}");
                }
                resueltas++;
            }
            catch (Exception ex)
            {
                log.Report($"  Error al verificar: {ex.Message}");
            }
        }
        return resueltas;
    }

    /// <summary>
    /// Pregunta a ARCA por el comprobante reservado. Devuelve:
    ///  - Aprobado = true  → ARCA lo tiene autorizado y coincide (mismo CUIT e importe);
    ///  - Aprobado = false → ARCA no lo tiene, o ese número lo tomó otra factura distinta;
    ///  - null             → no se pudo saber (error de red u otra duda).
    /// </summary>
    private async Task<ResultadoCae?> VerificarEnArcaAsync(FacturaRow f)
    {
        try
        {
            var comp = await _arca.ConsultarComprobanteAsync(f.PtoVta, f.CbteTipo, f.Numero);
            if (comp is null)
                return new ResultadoCae(false, null, null, "ARCA no registra el comprobante: no se autorizó. Se puede reintentar.");

            var coincide = comp.DocNro.ToString() == f.Cuit
                           && comp.ImpTotal == f.Importe
                           && !string.IsNullOrWhiteSpace(comp.Cae);
            return coincide
                ? new ResultadoCae(true, comp.Cae, comp.CaeVto, "Recuperada tras verificación.")
                : new ResultadoCae(false, null, null, "Ese número fue tomado por otra factura distinta. Reintentar emite con un número nuevo.");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Genera el PDF de una factura autorizada. Si falla, la factura sigue siendo válida en ARCA.</summary>
    private void GenerarPdfSeguro(long id, IProgress<string> log)
    {
        try
        {
            var registro = _db.Obtener(id)!;
            var ruta = _pdf.Generar(registro);
            _db.GuardarPdf(id, ruta);
        }
        catch (Exception ex)
        {
            log.Report($"  Aviso: la factura quedó autorizada pero no se pudo generar el PDF ({ex.Message}).");
        }
    }

    /// <summary>Genera los PDF de las facturas autorizadas que todavía no lo tienen. Devuelve cuántos generó.</summary>
    public int GenerarPdfsFaltantes(IProgress<string> log)
    {
        var faltantes = _db.Historial(_s.Ambiente, 1000)
            .Where(f => f.Estado == "Autorizada" && string.IsNullOrEmpty(f.PdfPath))
            .ToList();

        foreach (var f in faltantes)
            GenerarPdfSeguro(f.Id, log);

        return faltantes.Count;
    }
    private static string Corto(string s) => s.Length <= 80 ? s : s[..80] + "…";
}