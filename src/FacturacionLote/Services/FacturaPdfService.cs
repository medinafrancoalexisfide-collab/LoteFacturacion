using System.Globalization;
using System.Text;
using System.Text.Json;
using FacturacionLote.Config;
using FacturacionLote.Domain;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FacturacionLote.Services;

/// <summary>Genera el PDF de una factura (A, B o C) ya autorizada, con el código QR exigido por ARCA.</summary>
public sealed class FacturaPdfService
{
    private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");
    private readonly AppSettings _s;

    static FacturaPdfService()
    {
        // Licencia Community de QuestPDF (gratuita para empresas con ingresos anuales menores a USD 1M).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public FacturaPdfService(AppSettings settings) => _s = settings;

    /// <summary>Crea el PDF y devuelve la ruta del archivo.</summary>
    public string Generar(FacturaRow f)
    {
        if (string.IsNullOrWhiteSpace(f.Cae))
            throw new InvalidOperationException("No se puede generar el PDF de una factura sin CAE.");

        var carpeta = AppPaths.FacturasDir(f.Ambiente);
        Directory.CreateDirectory(carpeta);
        var letra = TiposComprobante.LetraDeCodigo(f.CbteTipo);
        var codigo = TiposComprobante.CodigoImpreso(f.CbteTipo);
        var ruta = Path.Combine(carpeta, $"Factura{letra}_{f.PtoVta:D5}-{f.Numero:D8}_{f.Cuit}.pdf");

        var qr = GenerarQr(f);
        var esPrueba = f.Ambiente != "PRODUCCION";
        var descripcion = $"{_s.DescripcionServicio} - período {f.PeriodoDesde:dd/MM/yyyy} al {f.PeriodoHasta:dd/MM/yyyy}";

        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontSize(10));

                // ---------- Encabezado ----------
                page.Header().Column(col =>
                {
                    // En homologación se marca claramente que no tiene valor fiscal
                    if (esPrueba)
                    {
                        col.Item().Background(Colors.Red.Lighten4).Padding(4).AlignCenter()
                            .Text("HOMOLOGACIÓN - COMPROBANTE DE PRUEBA SIN VALOR FISCAL")
                            .Bold().FontColor(Colors.Red.Darken3);
                    }

                    col.Item().PaddingTop(6).Row(row =>
                    {
                        // Izquierda: datos del emisor
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(_s.RazonSocial).FontSize(14).Bold();
                            c.Item().Text($"Domicilio comercial: {_s.Domicilio}");
                            c.Item().Text($"Condición frente al IVA: {_s.CondicionIvaEmisorTexto}");
                        });

                        // Centro: letra del comprobante
                        row.ConstantItem(70).Border(1).Padding(4).Column(c =>
                        {
                            c.Item().AlignCenter().Text(letra).FontSize(28).Bold();
                            c.Item().AlignCenter().Text($"COD. {codigo}").FontSize(8);
                        });

                        // Derecha: datos del comprobante
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().AlignRight().Text("FACTURA").FontSize(14).Bold();
                            c.Item().AlignRight().Text($"Punto de venta: {f.PtoVta:D5}   Comp. Nro: {f.Numero:D8}");
                            c.Item().AlignRight().Text($"Fecha de emisión: {f.FechaEmision:dd/MM/yyyy}");
                            c.Item().AlignRight().Text($"CUIT: {_s.CuitEmisor}");
                            if (!string.IsNullOrWhiteSpace(_s.IngresosBrutos))
                                c.Item().AlignRight().Text($"Ingresos Brutos: {_s.IngresosBrutos}");
                            if (!string.IsNullOrWhiteSpace(_s.InicioActividades))
                                c.Item().AlignRight().Text($"Inicio de actividades: {_s.InicioActividades}");
                        });
                    });

                    col.Item().PaddingTop(6).LineHorizontal(1);
                });

                // ---------- Contenido ----------
                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(6);

                    col.Item().Border(0.5f).Padding(6).Column(c =>
                    {
                        c.Item().Text(t => { t.Span("Período facturado: ").Bold(); t.Span($"{f.PeriodoDesde:dd/MM/yyyy} al {f.PeriodoHasta:dd/MM/yyyy}"); });
                        c.Item().Text(t => { t.Span("Vencimiento del pago: ").Bold(); t.Span($"{f.FechaVtoPago:dd/MM/yyyy}"); });
                    });

                    col.Item().Border(0.5f).Padding(6).Column(c =>
                    {
                        c.Item().Text(t => { t.Span("CUIT: ").Bold(); t.Span(f.Cuit); });
                        c.Item().Text(t => { t.Span("Apellido y nombre / Razón social: ").Bold(); t.Span(f.Cliente); });
                        c.Item().Text(t => { t.Span("Condición frente al IVA: ").Bold(); t.Span(Catalogos.TextoCondIva(f.CondIva)); });
                        c.Item().Text(t => { t.Span("Condición de venta: ").Bold(); t.Span("Cuenta corriente"); });
                    });

                    // Factura A: se discriminan neto e IVA. Factura B y C: se muestra el total.
                    var discrimina = f.CbteTipo == TiposComprobante.FacturaA;

                    col.Item().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.ConstantColumn(110);
                        });

                        table.Header(h =>
                        {
                            h.Cell().Element(Encabezado).Text("Descripción").Bold();
                            h.Cell().Element(Encabezado).AlignRight().Text(discrimina ? "Importe neto" : "Importe").Bold();
                        });

                        table.Cell().Element(Celda).Text(descripcion);
                        table.Cell().Element(Celda).AlignRight().Text($"$ {Dinero(discrimina ? f.ImporteNeto : f.Importe)}");
                    });

                    if (discrimina)
                    {
                        col.Item().PaddingTop(8).AlignRight().Column(detalle =>
                        {
                            detalle.Item().AlignRight().Text($"Subtotal neto gravado: $ {Dinero(f.ImporteNeto)}");
                            detalle.Item().AlignRight().Text($"IVA {f.Alicuota:0.##} %: $ {Dinero(f.ImporteIva)}");
                        });
                    }

                    col.Item().PaddingTop(8).AlignRight()
                        .Text($"Importe total: $ {Dinero(f.Importe)}").FontSize(12).Bold();

                    if (f.CbteTipo == TiposComprobante.FacturaB && f.ImporteIva > 0)
                    {
                        col.Item().AlignRight()
                            .Text($"IVA contenido ({f.Alicuota:0.##} %): $ {Dinero(f.ImporteIva)}").FontSize(8);
                    }
                });

                // ---------- Pie: QR + CAE ----------
                page.Footer().Row(row =>
                {
                    row.ConstantItem(95).Height(95).Image(qr);
                    row.RelativeItem().PaddingLeft(10).Column(c =>
                    {
                        c.Item().Text(t => { t.Span("CAE Nº: ").Bold(); t.Span(f.Cae!); });
                        c.Item().Text(t => { t.Span("Vencimiento del CAE: ").Bold(); t.Span(f.CaeVto?.ToString("dd/MM/yyyy") ?? "-"); });
                        c.Item().PaddingTop(4).Text("Comprobante autorizado.").FontSize(8);
                    });
                });
            });
        }).GeneratePdf(ruta);

        return ruta;
    }

    // Estilos de las celdas de la tabla
    private static IContainer Encabezado(IContainer c) =>
        c.Background(Colors.Grey.Lighten3).Border(0.5f).Padding(5);

    private static IContainer Celda(IContainer c) =>
        c.Border(0.5f).Padding(5);

    private static string Dinero(decimal v) => v.ToString("N2", Ar);

    /// <summary>
    /// QR según la normativa de ARCA: una URL con los datos del comprobante
    /// en formato JSON, codificados en Base64.
    /// </summary>
    private byte[] GenerarQr(FacturaRow f)
    {
        var datos = new
        {
            ver = 1,
            fecha = f.FechaEmision.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            cuit = _s.CuitEmisor,
            ptoVta = f.PtoVta,
            tipoCmp = f.CbteTipo,
            nroCmp = f.Numero,
            importe = f.Importe,
            moneda = "PES",
            ctz = 1,
            tipoDocRec = Catalogos.DocTipoCuit,
            nroDocRec = long.Parse(f.Cuit, CultureInfo.InvariantCulture),
            tipoCodAut = "E",   // "E" = autorizado con CAE
            codAut = long.Parse(f.Cae!, CultureInfo.InvariantCulture)
        };

        var json = JsonSerializer.Serialize(datos);
        var url = "https://www.afip.gob.ar/fe/qr/?p=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        using var generador = new QRCodeGenerator();
        using var qrData = generador.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(qrData).GetGraphic(8);
    }
}