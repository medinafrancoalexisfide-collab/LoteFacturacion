using System.Xml;
using System.Xml.Linq;

namespace FacturacionLote.Infrastructure;

/// <summary>Lectura de XML con las opciones más estrictas: sin DTD ni resolución de entidades externas.</summary>
public static class XmlSeguro
{
    /// <summary>
    /// Convierte un texto en XDocument rechazando cualquier DOCTYPE (DTD).
    /// Las respuestas de ARCA nunca llevan DTD, así que si aparece una, es algo raro y se corta.
    /// Lanza XmlException si el texto no es XML válido o trae un DOCTYPE.
    /// </summary>
    public static XDocument Parsear(string xml)
    {
        var opciones = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
            MaxCharactersInDocument = 10_000_000
        };

        using var texto = new StringReader(xml);
        using var lector = XmlReader.Create(texto, opciones);
        return XDocument.Load(lector);
    }
}
