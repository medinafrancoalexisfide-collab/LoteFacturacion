using System.Text;
using System.Text.Json;
using FacturacionLote.Config;

namespace FacturacionLote.Tests;

// Estas pruebas usan DPAPI de Windows, por eso el proyecto de pruebas apunta a net10.0-windows.
public class SecretosTests
{
    [Fact]
    public void Proteger_Y_Desproteger_DevuelvenElTextoOriginal()
    {
        var protegido = Secretos.Proteger("clave-secreta-123");

        Assert.NotNull(protegido);
        Assert.DoesNotContain("clave-secreta-123", protegido);
        Assert.Equal("clave-secreta-123", Secretos.Desproteger(protegido));
    }

    [Fact]
    public void Proteger_VacioDevuelveNull_Y_DesprotegerVacioDevuelveVacio()
    {
        Assert.Null(Secretos.Proteger(""));
        Assert.Null(Secretos.Proteger(null));
        Assert.Equal("", Secretos.Desproteger(null));
        Assert.Equal("", Secretos.Desproteger(""));
    }

    [Fact]
    public void Desproteger_ConBasuraNoLanzaExcepcion()
    {
        Assert.Equal("", Secretos.Desproteger("esto no es un secreto cifrado"));
        Assert.Equal("", Secretos.Desproteger("AAAA"));   // base64 válido pero no es un dato cifrado con DPAPI
    }

    [Fact]
    public void ProtegerBytes_CifraYDevuelveLosMismosBytes()
    {
        var original = Encoding.UTF8.GetBytes("<ticket><token>abc</token></ticket>");

        var cifrado = Secretos.ProtegerBytes(original);

        Assert.NotEqual(original, cifrado);
        Assert.Equal(original, Secretos.DesprotegerBytes(cifrado));
    }

    [Fact]
    public void Configuracion_NoGuardaLosSecretosEnTextoPlano()
    {
        var ajustes = new AppSettings
        {
            DgPassword = "pass-dg-ultra-secreta",
            DgClientSecret = "secret-dg-ultra-secreto",
            CertPassword = "pass-cert-ultra-secreta",
            SmtpPassword = "pass-smtp-ultra-secreta",
            DgUsuario = "usuario.visible"
        };

        var json = JsonSerializer.Serialize(ajustes);

        Assert.DoesNotContain("pass-dg-ultra-secreta", json);
        Assert.DoesNotContain("secret-dg-ultra-secreto", json);
        Assert.DoesNotContain("pass-cert-ultra-secreta", json);
        Assert.DoesNotContain("pass-smtp-ultra-secreta", json);
        Assert.Contains("usuario.visible", json);   // lo que no es secreto sí se guarda

        var leido = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal("pass-dg-ultra-secreta", leido.DgPassword);
        Assert.Equal("secret-dg-ultra-secreto", leido.DgClientSecret);
        Assert.Equal("pass-cert-ultra-secreta", leido.CertPassword);
        Assert.Equal("pass-smtp-ultra-secreta", leido.SmtpPassword);
    }

    [Fact]
    public void Configuracion_PorDefectoEsHomologacionYNuncaEnviaAClientes()
    {
        var ajustes = new AppSettings();

        Assert.False(ajustes.UsarProduccion);
        Assert.False(ajustes.EmailEnviarAClientes);
        Assert.Equal("HOMOLOGACION", ajustes.Ambiente);
        Assert.True(ajustes.ConfirmacionReforzada);   // la confirmación escrita viene activada
        Assert.Equal("", ajustes.ArcaUrlWsaa);
        Assert.Equal("", ajustes.ArcaUrlWsfe);
    }
}
