namespace FacturacionLote.Domain;

public static class Cuit
{
    /// <summary>Devuelve los 11 dígitos si el texto es un CUIT válido; si no, null.</summary>
    public static string? Validar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        // Nos quedamos solo con los dígitos (así acepta "20-12345678-9")
        var digitos = new string(texto.Where(char.IsDigit).ToArray());
        if (digitos.Length != 11) return null;

        // Dígito verificador: módulo 11 con estos ponderadores
        int[] ponderadores = { 5, 4, 3, 2, 7, 6, 5, 4, 3, 2 };
        var suma = 0;
        for (var i = 0; i < ponderadores.Length; i++)
            suma += (digitos[i] - '0') * ponderadores[i];

        var verificador = 11 - suma % 11;
        if (verificador == 11) verificador = 0;
        if (verificador == 10) verificador = 9;

        return digitos[10] - '0' == verificador ? digitos : null;
    }

    /// <summary>Prueba cada candidato en orden y devuelve el primer CUIT válido.</summary>
    public static string? Resolver(params string?[] candidatos)
    {
        foreach (var c in candidatos)
        {
            var v = Validar(c);
            if (v != null) return v;
        }
        return null;
    }
}
