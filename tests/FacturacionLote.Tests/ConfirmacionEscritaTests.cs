using FacturacionLote.UI;

namespace FacturacionLote.Tests;

public class ConfirmacionEscritaTests
{
    [Theory]
    [InlineData("PRODUCCION", true)]
    [InlineData("  PRODUCCION  ", true)]
    [InlineData("produccion", false)]    // distingue mayúsculas
    [InlineData("PRODUCCION!", false)]
    [InlineData("PRODUCCIÓN", false)]    // con tilde no coincide
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EsCorrecto_ExigeLaPalabraExacta(string? escrito, bool esperado)
    {
        Assert.Equal(esperado, ConfirmacionEscrita.EsCorrecto(escrito, "PRODUCCION"));
    }
}
