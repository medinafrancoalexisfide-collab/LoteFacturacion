namespace FacturacionLote.Domain;

/// <summary>Una factura guardada en la base SQLite.</summary>
public sealed class FacturaRow
{
    public long Id { get; set; }
    public string Ambiente { get; set; } = "";
    public int ClienteId { get; set; }
    public string Cliente { get; set; } = "";
    public string Cuit { get; set; } = "";
    public int CondIva { get; set; }
    public decimal Importe { get; set; }
    public decimal ImporteNeto { get; set; }
    public decimal ImporteIva { get; set; }
    /// <summary>Alícuota de IVA en porcentaje (21 = 21 %). 0 si no discrimina IVA.</summary>
    public decimal Alicuota { get; set; }
    public int PtoVta { get; set; }
    public int CbteTipo { get; set; }
    public long Numero { get; set; }
    public string? Cae { get; set; }
    public DateOnly? CaeVto { get; set; }
    public DateOnly PeriodoDesde { get; set; }
    public DateOnly PeriodoHasta { get; set; }
    public DateOnly FechaVtoPago { get; set; }
    public DateOnly FechaEmision { get; set; }
    public string Estado { get; set; } = "";   // Enviando, Autorizada o Error
    public string? Error { get; set; }
    public string? PdfPath { get; set; }
    public string CreadoLocal { get; set; } = "";

    // Propiedades calculadas, para mostrar en las grillas
    public string Tipo => TiposComprobante.LetraDeCodigo(CbteTipo);
    public string Comprobante => $"{PtoVta:D5}-{Numero:D8}";
    public string Periodo => $"{PeriodoDesde:dd/MM/yyyy} al {PeriodoHasta:dd/MM/yyyy}";
}