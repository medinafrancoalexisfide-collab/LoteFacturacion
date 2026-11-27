namespace FacturacionLote.Domain;

/// <summary>Último envío de e-mail registrado para una factura.</summary>
public sealed record EnvioEmailRow(long FacturaId, string Destino, string Modo, string Estado, string? Error, string CreadoLocal);
