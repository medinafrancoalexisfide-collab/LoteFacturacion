using FacturacionLote.Services;

namespace FacturacionLote.Tests;

public class EmailAnalisisTests
{
    [Fact]
    public void AnalizarEmail_UnaDireccionValida()
    {
        var (valido, normalizado) = EmailService.AnalizarEmail("cliente@ejemplo.com");

        Assert.True(valido);
        Assert.Equal("cliente@ejemplo.com", normalizado);
    }

    [Fact]
    public void AnalizarEmail_VariasDireccionesSeparadasPorPuntoYComa()
    {
        var (valido, normalizado) = EmailService.AnalizarEmail("uno@ejemplo.com; dos@ejemplo.org");

        Assert.True(valido);
        Assert.Equal("uno@ejemplo.com, dos@ejemplo.org", normalizado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnalizarEmail_VacioNoEsValido(string? texto)
    {
        var (valido, normalizado) = EmailService.AnalizarEmail(texto);

        Assert.False(valido);
        Assert.Equal("", normalizado);
    }
}
