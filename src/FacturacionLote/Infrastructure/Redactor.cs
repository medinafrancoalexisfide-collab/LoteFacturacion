using System.Text.RegularExpressions;

namespace FacturacionLote.Infrastructure;

/// <summary>Oculta datos personales y sensibles antes de escribir un texto en el archivo de registro.</summary>
public static class Redactor
{
    // nombre@dominio.ext
    private static readonly Regex Email = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

    // Dos alternativas:
    //  - "comp": un número de comprobante (00001-00000037), que NO es un dato personal y se deja visible;
    //  - cualquier secuencia de 8 o más dígitos (CUIT, CAE, DNI, teléfonos), con o sin guiones: 20-12345678-6
    private static readonly Regex Numero = new(
        @"(?<comp>\b\d{5}-\d{8}\b)|\d(?:-?\d){7,}", RegexOptions.Compiled);

    // <Token>…</Token> y <Sign>…</Sign> (por si algún día se registra un pedido a ARCA)
    private static readonly Regex TokenSign = new(
        @"(<(?:\w+:)?(?:Token|Sign)>)[^<]*(</(?:\w+:)?(?:Token|Sign)>)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Authorization: Bearer xxxxx
    private static readonly Regex Bearer = new(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Reemplaza e-mails por [email], secuencias largas de dígitos por ***XYZ (solo quedan los últimos 3;
    /// los números de comprobante 00001-00000037 se dejan) y elimina tokens. Nunca lanza excepciones: si recibe null devuelve cadena vacía.
    /// </summary>
    public static string Limpiar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";

        var r = TokenSign.Replace(texto, "$1[oculto]$2");
        r = Bearer.Replace(r, "Bearer [oculto]");
        r = Email.Replace(r, "[email]");
        r = Numero.Replace(r, m =>
        {
            if (m.Groups["comp"].Success) return m.Value;
            var digitos = new string(m.Value.Where(char.IsDigit).ToArray());
            return "***" + digitos[^3..];
        });
        return r;
    }
}
