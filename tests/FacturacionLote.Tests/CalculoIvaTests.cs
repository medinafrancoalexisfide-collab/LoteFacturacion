using FacturacionLote.Domain;

namespace FacturacionLote.Tests;

public class CalculoIvaTests
{
    // Valores calculados aparte (Python, redondeo "mitad hacia arriba") para no depender del propio código.
    [Theory]
    [InlineData("121.00", "21", "100.00", "21.00")]
    [InlineData("1210.00", "21", "1000.00", "210.00")]
    [InlineData("1000.00", "21", "826.45", "173.55")]
    [InlineData("1.00", "21", "0.83", "0.17")]
    [InlineData("50.00", "10.5", "45.25", "4.75")]
    public void Descomponer_DevuelveNetoEIvaExactos(string total, string alicuota, string netoEsperado, string ivaEsperado)
    {
        var (neto, iva) = CalculoIva.Descomponer(Dec(total), Dec(alicuota));

        Assert.Equal(Dec(netoEsperado), neto);
        Assert.Equal(Dec(ivaEsperado), iva);
    }

    [Fact]
    public void Descomponer_SinCoincidenciaExacta_ElIvaAbsorbeLaDiferenciaDeCentavos()
    {
        // Con 100,00 al 21 % no existe un neto cuyo IVA redondeado complete exactamente el total.
        var (neto, iva) = CalculoIva.Descomponer(100.00m, 21m);

        Assert.Equal(82.64m, neto);
        Assert.Equal(17.36m, iva);
        Assert.Equal(100.00m, neto + iva);
    }

    [Theory]
    [InlineData("100.00", "0")]    // sin IVA
    [InlineData("0.00", "21")]     // total cero
    [InlineData("-5.00", "21")]    // total negativo
    public void Descomponer_SinIva_DevuelveElTotalComoNeto(string total, string alicuota)
    {
        var (neto, iva) = CalculoIva.Descomponer(Dec(total), Dec(alicuota));

        Assert.Equal(Dec(total), neto);
        Assert.Equal(0m, iva);
    }

    [Theory]
    [InlineData("21")]
    [InlineData("10.5")]
    [InlineData("27")]
    public void Descomponer_SiempreSumaExactamenteElTotal(string alicuota)
    {
        // Para TODOS los totales de $0,01 a $2.000,00: neto + IVA == total, neto > 0 e IVA >= 0.
        for (var centavos = 1; centavos <= 200_000; centavos++)
        {
            var total = centavos / 100m;
            var (neto, iva) = CalculoIva.Descomponer(total, Dec(alicuota));

            Assert.True(neto + iva == total, $"neto + IVA no suma el total para {total}");
            Assert.True(neto > 0m, $"neto no positivo para {total}");
            Assert.True(iva >= 0m, $"IVA negativo para {total}");
        }
    }

    private static decimal Dec(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
}
