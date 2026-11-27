using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FacturacionLote.Domain;

/// <summary>Lee un valor JSON que puede venir como texto o como número y lo devuelve como texto.
/// Don Gestión a veces manda "123" y a veces 123 para el mismo campo.</summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l)
                ? l.ToString(CultureInfo.InvariantCulture)
                : reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => throw new JsonException("Se esperaba texto o número.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

public sealed class ClienteDg
{
    // [JsonPropertyName] enlaza el nombre del JSON (con_guiones_bajos) con el de C# (MayúsculaInicial)
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("supplier_business_name")]
    public string? SupplierBusinessName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>En Don Gestión suele ser un código del cliente; en este negocio parece contener el CUIT.</summary>
    [JsonPropertyName("contact_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? ContactId { get; set; }

    [JsonPropertyName("tax_number")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? TaxNumber { get; set; }

    [JsonPropertyName("default_cuit")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? DefaultCuit { get; set; }

    /// <summary>Tipo de factura habitual del cliente en Don Gestión (por ejemplo "Factura A"), si está cargado.</summary>
    [JsonPropertyName("default_invoice_type")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? DefaultInvoiceType { get; set; }

    /// <summary>Nombre para mostrar: razón social si existe, si no el nombre.</summary>
    public string NombreMostrar =>
        !string.IsNullOrWhiteSpace(SupplierBusinessName) ? SupplierBusinessName!.Trim()
        : !string.IsNullOrWhiteSpace(Name) ? Name!.Trim()
        : $"Cliente {Id}";
}

public sealed class VentaDg
{
    [JsonPropertyName("id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int Id { get; set; }

    /// <summary>ID interno del cliente en Don Gestión.</summary>
    [JsonPropertyName("contact_id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? ContactId { get; set; }

    [JsonPropertyName("final_total")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal FinalTotal { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("payment_status")]
    public string? PaymentStatus { get; set; }

    [JsonPropertyName("transaction_date")]
    public string? TransactionDate { get; set; }

    [JsonPropertyName("invoice_no")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? InvoiceNo { get; set; }

    /// <summary>Renglones de la venta (sirven para controlar el IVA).</summary>
    [JsonPropertyName("sell_lines")]
    public List<LineaVentaDg>? SellLines { get; set; }

    /// <summary>El contact_id de la venta convertido a número (o null si no se puede).</summary>
    public int? ClienteIdInterno =>
        int.TryParse(ContactId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}

/// <summary>Un renglón de una venta de Don Gestión.</summary>
public sealed class LineaVentaDg
{
    [JsonPropertyName("quantity")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Quantity { get; set; }

    /// <summary>Precio unitario sin impuestos.</summary>
    [JsonPropertyName("unit_price")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? UnitPrice { get; set; }

    /// <summary>Precio unitario con impuestos.</summary>
    [JsonPropertyName("unit_price_inc_tax")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? UnitPriceIncTax { get; set; }
}

/// <summary>La API envuelve las listas así: { "data": [ ... ] }</summary>
public sealed class RespuestaLista<T>
{
    [JsonPropertyName("data")]
    public List<T>? Data { get; set; }
}