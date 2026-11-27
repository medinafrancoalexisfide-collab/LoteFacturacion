using FacturacionLote.Infrastructure;

namespace FacturacionLote.Tests;

public class RedactorTests
{
    // Resultados esperados calculados aparte con la misma expresión regular.
    [Theory]
    [InlineData("CUIT 20-12345678-6 emitida", "CUIT ***786 emitida")]
    [InlineData("DNI 12345678 y CUIT 20123456786", "DNI ***678 y CUIT ***786")]
    [InlineData("CAE 74123456789012", "CAE ***012")]
    [InlineData("Enviado a juan.perez@ejemplo.com.ar ok", "Enviado a [email] ok")]
    [InlineData("<ar:Token>abc123XYZ</ar:Token>", "<ar:Token>[oculto]</ar:Token>")]
    [InlineData("Authorization: Bearer abc.def-123", "Authorization: Bearer [oculto]")]
    [InlineData("Cliente 30-71234567-1: autorizada, CAE 74123456789012. Comprobante 00003-00000012.",
                "Cliente ***671: autorizada, CAE ***012. Comprobante 00003-00000012.")]
    public void Limpiar_OcultaDatosSensibles(string original, string esperado)
    {
        Assert.Equal(esperado, Redactor.Limpiar(original));
    }

    [Theory]
    [InlineData("Comprobante 00001-00000037 autorizado")]   // número de comprobante: se deja visible
    [InlineData("Importe 1234567.89")]                      // menos de 8 dígitos seguidos
    [InlineData("Total $ 15.000,50")]
    [InlineData("Fecha 05/10/2026")]
    [InlineData("Lote terminado: 12 emitidas, 0 con error")]
    public void Limpiar_NoTocaTextoCorriente(string texto)
    {
        Assert.Equal(texto, Redactor.Limpiar(texto));
    }

    [Fact]
    public void Limpiar_ConNuloOVacio_DevuelveVacio()
    {
        Assert.Equal("", Redactor.Limpiar(null));
        Assert.Equal("", Redactor.Limpiar(""));
    }
}
