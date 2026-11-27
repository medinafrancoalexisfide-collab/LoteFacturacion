using System.Xml;
using FacturacionLote.Infrastructure;

namespace FacturacionLote.Tests;

public class XmlSeguroTests
{
    [Fact]
    public void Parsear_XmlNormal()
    {
        var doc = XmlSeguro.Parsear("<a><b>hola</b></a>");

        Assert.Equal("a", doc.Root!.Name.LocalName);
        Assert.Equal("hola", doc.Root.Element("b")!.Value);
    }

    [Fact]
    public void Parsear_RechazaDoctype()
    {
        const string conDtd = "<?xml version=\"1.0\"?><!DOCTYPE a [<!ENTITY x \"hola\">]><a>&x;</a>";

        Assert.Throws<XmlException>(() => XmlSeguro.Parsear(conDtd));
    }

    [Fact]
    public void Parsear_RechazaXmlMalFormado()
    {
        Assert.Throws<XmlException>(() => XmlSeguro.Parsear("<a><b></a>"));
    }
}
