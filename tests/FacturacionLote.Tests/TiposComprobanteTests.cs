using FacturacionLote.Domain;

namespace FacturacionLote.Tests;

public class TiposComprobanteTests
{
    [Theory]
    [InlineData(1, "A")]    // Responsable Inscripto
    [InlineData(6, "A")]    // Monotributo
    [InlineData(13, "A")]   // Monotributista Social
    [InlineData(16, "A")]   // Monotributista Trabajador Independiente Promovido
    [InlineData(5, "B")]    // Consumidor Final
    [InlineData(4, "B")]    // Exento
    [InlineData(15, "B")]   // No Alcanzado
    [InlineData(0, "B")]
    public void LetraPorCondicion_SegunLaCondicionDelCliente(int condIva, string esperada)
    {
        Assert.Equal(esperada, TiposComprobante.LetraPorCondicion(condIva));
    }

    [Theory]
    [InlineData("Factura A", "A")]
    [InlineData("factura b", "B")]
    [InlineData("A", "A")]
    [InlineData("Factura C", "")]
    [InlineData("FACTURA", "")]   // las letras A dentro de una palabra no cuentan
    [InlineData("", "")]
    [InlineData(null, "")]
    public void LetraDeTexto_ExtraeSoloLaLetraAOB(string? texto, string esperada)
    {
        Assert.Equal(esperada, TiposComprobante.LetraDeTexto(texto));
    }

    [Theory]
    [InlineData("A", 1)]
    [InlineData("B", 6)]
    [InlineData("C", 11)]
    [InlineData("X", 0)]
    public void CodigoArca_SegunLaLetra(string letra, int esperado)
    {
        Assert.Equal(esperado, TiposComprobante.CodigoArca(letra));
    }

    [Theory]
    [InlineData(1, "A", "001")]
    [InlineData(6, "B", "006")]
    [InlineData(11, "C", "011")]
    [InlineData(99, "?", "000")]
    public void LetraYCodigoImpreso_SegunElCodigoDeArca(int cbteTipo, string letra, string impreso)
    {
        Assert.Equal(letra, TiposComprobante.LetraDeCodigo(cbteTipo));
        Assert.Equal(impreso, TiposComprobante.CodigoImpreso(cbteTipo));
    }

    [Theory]
    [InlineData("21", 5)]
    [InlineData("10.5", 4)]
    [InlineData("27", 6)]
    [InlineData("5", 8)]
    [InlineData("2.5", 9)]
    [InlineData("0", 3)]
    [InlineData("13", 0)]   // alícuota que ARCA no admite en este programa
    public void Catalogos_IdAlicuota_CodigosDeArca(string porcentaje, int esperado)
    {
        var valor = decimal.Parse(porcentaje, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(esperado, Catalogos.IdAlicuota(valor));
    }
}
