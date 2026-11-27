using FacturacionLote.Domain;

namespace FacturacionLote.Tests;

public class CuitTests
{
    // CUIT inventados con dígito verificador correcto (calculado aparte).
    [Theory]
    [InlineData("20123456786", "20123456786")]
    [InlineData("20-12345678-6", "20123456786")]   // acepta guiones
    [InlineData(" 30-71234567-1 ", "30712345671")]  // acepta espacios
    [InlineData("20200000030", "20200000030")]      // verificador 0
    public void Validar_AceptaCuitCorrectos(string texto, string esperado)
    {
        Assert.Equal(esperado, Cuit.Validar(texto));
    }

    [Theory]
    [InlineData("20123456785")]   // dígito verificador incorrecto
    [InlineData("2012345678")]    // 10 dígitos
    [InlineData("201234567866")]  // 12 dígitos
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validar_RechazaCuitIncorrectos(string? texto)
    {
        Assert.Null(Cuit.Validar(texto));
    }

    [Fact]
    public void Resolver_DevuelveElPrimerCandidatoValido()
    {
        Assert.Equal("20123456786", Cuit.Resolver(null, "no es un cuit", "20-12345678-6", "30712345671"));
    }

    [Fact]
    public void Resolver_SinCandidatosValidos_DevuelveNull()
    {
        Assert.Null(Cuit.Resolver(null, "", "123"));
    }
}
