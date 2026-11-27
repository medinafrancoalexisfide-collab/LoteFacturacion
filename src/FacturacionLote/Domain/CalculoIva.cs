namespace FacturacionLote.Domain;

public static class CalculoIva
{
    /// <summary>
    /// Separa un total con IVA incluido en neto e IVA. ARCA exige que neto + IVA sea exactamente el total
    /// y que el IVA corresponda a la alícuota, así que se busca un neto cercano que cumpla ambas cosas.
    /// </summary>
    public static (decimal Neto, decimal Iva) Descomponer(decimal total, decimal alicuotaPorcentaje)
    {
        total = Math.Round(total, 2, MidpointRounding.AwayFromZero);
        if (alicuotaPorcentaje <= 0m || total <= 0m) return (total, 0m);

        var tasa = alicuotaPorcentaje / 100m;
        var aproximado = Math.Round(total / (1m + tasa), 2, MidpointRounding.AwayFromZero);

        foreach (var ajuste in new[] { 0m, 0.01m, -0.01m, 0.02m, -0.02m })
        {
            var neto = aproximado + ajuste;
            var iva = Math.Round(neto * tasa, 2, MidpointRounding.AwayFromZero);
            if (neto > 0m && neto + iva == total) return (neto, iva);
        }

        // Sin coincidencia exacta (pasa con algunos totales): el IVA absorbe la diferencia de centavos
        return (aproximado, total - aproximado);
    }
}
