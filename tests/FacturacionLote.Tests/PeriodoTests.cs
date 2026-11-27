using FacturacionLote.Services;

namespace FacturacionLote.Tests;

public class PeriodoTests
{
    // Regla: se factura el mes anterior a la emisión; vence el último día del mes de emisión.
    [Theory]
    [InlineData("2026-10-05", "2026-09-01", "2026-09-30", "2026-10-31")]
    [InlineData("2026-01-15", "2025-12-01", "2025-12-31", "2026-01-31")]   // cruza el año
    [InlineData("2024-03-10", "2024-02-01", "2024-02-29", "2024-03-31")]   // febrero bisiesto
    [InlineData("2026-03-01", "2026-02-01", "2026-02-28", "2026-03-31")]   // primer día del mes
    [InlineData("2026-12-31", "2026-11-01", "2026-11-30", "2026-12-31")]   // último día del año
    public void CalcularPeriodo_ServicioDelMesAnteriorYVencimientoFinDeMes(string emision, string desde, string hasta, string vto)
    {
        var (d, h, v) = BillingService.CalcularPeriodo(DateOnly.Parse(emision, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(DateOnly.Parse(desde, System.Globalization.CultureInfo.InvariantCulture), d);
        Assert.Equal(DateOnly.Parse(hasta, System.Globalization.CultureInfo.InvariantCulture), h);
        Assert.Equal(DateOnly.Parse(vto, System.Globalization.CultureInfo.InvariantCulture), v);
    }
}
