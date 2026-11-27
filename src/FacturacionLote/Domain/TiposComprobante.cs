using System.Text.RegularExpressions;

namespace FacturacionLote.Domain;

/// <summary>Reglas sobre los tipos de comprobante (Factura A, B y C).</summary>
public static class TiposComprobante
{
    public const int FacturaA = 1;
    public const int FacturaB = 6;
    public const int FacturaC = 11;

    /// <summary>
    /// Un Responsable Inscripto emite Factura A a clientes Responsables Inscriptos o Monotributistas,
    /// y Factura B al resto (Consumidor Final, Exento, etc.). Conviene confirmarlo con el contador.
    /// </summary>
    public static string LetraPorCondicion(int condIvaCliente) =>
        condIvaCliente is 1 or 6 or 13 or 16 ? "A" : "B";

    /// <summary>Extrae la letra A o B de un texto como "Factura A". Devuelve "" si no hay.</summary>
    public static string LetraDeTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var m = Regex.Match(texto.Trim().ToUpperInvariant(), @"\b([AB])\b");
        return m.Success ? m.Groups[1].Value : "";
    }

    public static int CodigoArca(string letra) => letra switch
    {
        "A" => FacturaA,
        "B" => FacturaB,
        "C" => FacturaC,
        _ => 0
    };

    public static string LetraDeCodigo(int cbteTipo) => cbteTipo switch
    {
        FacturaA => "A",
        FacturaB => "B",
        FacturaC => "C",
        _ => "?"
    };

    /// <summary>Código que se imprime en el comprobante (001, 006, 011).</summary>
    public static string CodigoImpreso(int cbteTipo) => cbteTipo switch
    {
        FacturaA => "001",
        FacturaB => "006",
        FacturaC => "011",
        _ => "000"
    };
}
