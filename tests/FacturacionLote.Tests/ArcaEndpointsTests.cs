using FacturacionLote.Infrastructure;

namespace FacturacionLote.Tests;

public class ArcaEndpointsTests
{
    [Fact]
    public void SinPersonalizar_UsaLasDireccionesOficialesSegunElAmbiente()
    {
        var homo = ArcaEndpoints.Resolver(false, null, null);
        Assert.Equal("https://wsaahomo.afip.gov.ar/ws/services/LoginCms", homo.Wsaa);
        Assert.Equal("https://wswhomo.afip.gov.ar/wsfev1/service.asmx", homo.Wsfe);

        var prod = ArcaEndpoints.Resolver(true, "", "  ");
        Assert.Equal("https://wsaa.afip.gov.ar/ws/services/LoginCms", prod.Wsaa);
        Assert.Equal("https://servicios1.afip.gov.ar/wsfev1/service.asmx", prod.Wsfe);
    }

    [Fact]
    public void AceptaDireccionesOficialesCoherentesConElAmbiente()
    {
        var homo = ArcaEndpoints.Resolver(false,
            "https://wsaahomo.afip.gov.ar/ws/services/LoginCms",
            "https://wswhomo.arca.gob.ar/wsfev1/service.asmx");

        Assert.Equal("https://wsaahomo.afip.gov.ar/ws/services/LoginCms", homo.Wsaa);
        Assert.Equal("https://wswhomo.arca.gob.ar/wsfev1/service.asmx", homo.Wsfe);
    }

    [Theory]
    [InlineData("http://wsaahomo.afip.gov.ar/ws/services/LoginCms")]          // sin https
    [InlineData("https://servidor-ajeno.example.com/ws/services/LoginCms")]    // dominio ajeno
    [InlineData("https://wsaahomo.afip.gov.ar.example.com/ws")]                // dominio engañoso (sufijo)
    [InlineData("https://wsaahomoafip.gov.ar/ws")]                             // falta el punto antes de afip
    [InlineData("https://wsaa.afip.gov.ar/ws/services/LoginCms")]              // producción en ambiente de homologación
    [InlineData("no es una direccion")]
    public void EnHomologacion_RechazaDireccionesNoPermitidas(string url)
    {
        Assert.Throws<InvalidOperationException>(() => ArcaEndpoints.Resolver(false, url, null));
        Assert.Throws<InvalidOperationException>(() => ArcaEndpoints.Resolver(false, null, url));
    }

    [Fact]
    public void EnProduccion_RechazaDireccionesDeHomologacion()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ArcaEndpoints.Resolver(true, "https://wsaahomo.afip.gov.ar/ws/services/LoginCms", null));
    }
}
