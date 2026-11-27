namespace FacturacionLote.Domain;

/// <summary>Una opción de condición frente al IVA (código de ARCA + texto).</summary>
public sealed record CondIvaItem(int Id, string Texto);

/// <summary>Una alícuota de IVA con su código de ARCA.</summary>
public sealed record AlicuotaItem(decimal Porcentaje, int IdArca, string Texto);

/// <summary>Condición del emisor: Responsable Inscripto (emite A y B) o Monotributista (emite C).</summary>
public sealed record OpcionEmisor(bool EsResponsableInscripto, string Texto);

public static class Catalogos
{
    public static readonly AlicuotaItem[] Alicuotas =
    {
        new(21m, 5, "21 %"),
        new(10.5m, 4, "10,5 %"),
        new(27m, 6, "27 %"),
        new(5m, 8, "5 %"),
        new(2.5m, 9, "2,5 %"),
        new(0m, 3, "0 %"),
    };

    /// <summary>Código de ARCA de una alícuota (0 si no es una de las admitidas).</summary>
    public static int IdAlicuota(decimal porcentaje) =>
        Alicuotas.FirstOrDefault(a => a.Porcentaje == porcentaje)?.IdArca ?? 0;

    public static readonly OpcionEmisor[] OpcionesEmisor =
    {
        new(true, "Responsable Inscripto (emite Factura A y B)"),
        new(false, "Monotributista (emite Factura C)"),
    };

    /// <summary>Condiciones habituales del receptor para Factura C.
    /// La lista oficial se puede consultar a ARCA con FEParamGetCondicionIvaReceptor.</summary>
    public static readonly CondIvaItem[] CondicionesIva =
    {
        new(1, "IVA Responsable Inscripto"),
        new(4, "IVA Sujeto Exento"),
        new(5, "Consumidor Final"),
        new(6, "Responsable Monotributo"),
        new(13, "Monotributista Social"),
        new(15, "IVA No Alcanzado"),
    };

    /// <summary>Lista para la grilla de facturación: incluye "(elegir)" para clientes sin condición definida.</summary>
    public static readonly CondIvaItem[] CondicionesIvaGrilla =
        new[] { new CondIvaItem(0, "(elegir)") }.Concat(CondicionesIva).ToArray();

    public static string TextoCondIva(int id) =>
        CondicionesIva.FirstOrDefault(c => c.Id == id)?.Texto ?? $"Código {id}";

    public const int CbteTipoFacturaC = 11;   // código de ARCA para Factura C
    public const int DocTipoCuit = 80;        // el cliente se identifica con CUIT
    public const int ConceptoServicios = 2;   // 2 = Servicios (exige fechas de servicio y vencimiento)
}