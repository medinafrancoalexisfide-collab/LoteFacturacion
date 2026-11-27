using System.Diagnostics;
using FacturacionLote.Config;
using FacturacionLote.Domain;
using FacturacionLote.Infrastructure;
using FacturacionLote.Services;

namespace FacturacionLote.UI;

public sealed class HistorialForm : Form
{
    private readonly AppSettings _s;
    private readonly Database _db;
    private readonly BillingService _billing;
    private readonly DataGridView _grid = new();
    private readonly ComboBox _cmbFiltro = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly Label _lblConteo = new();

    private List<FacturaRow> _todas = new();

    public HistorialForm(AppSettings settings, Database db, BillingService billing)
    {
        _s = settings;
        _db = db;
        _billing = billing;

        Text = $"Historial de facturas ({_s.Ambiente})";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1200, 580);

        // Estructura: arriba la barra de botones y filtro, abajo la grilla
        var raiz = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(raiz);

        var barra = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(6) };
        barra.Controls.Add(new Label { Text = "Mostrar:", AutoSize = true, Margin = new Padding(0, 8, 4, 0) });
        _cmbFiltro.Items.AddRange(new object[] { "Todas", "Autorizadas", "Rechazadas / con error", "Pendientes de verificar" });
        _cmbFiltro.SelectedIndex = 0;
        _cmbFiltro.SelectedIndexChanged += (_, _) => AplicarFiltro();
        _cmbFiltro.Margin = new Padding(0, 4, 10, 0);
        barra.Controls.Add(_cmbFiltro);
        barra.Controls.Add(CrearBoton("Actualizar", (_, _) => Cargar()));
        barra.Controls.Add(CrearBoton("Abrir PDF", (_, _) => AbrirPdf()));
        barra.Controls.Add(CrearBoton("Reconciliar pendientes", OnReconciliar));
        _lblConteo.AutoSize = true;
        _lblConteo.Margin = new Padding(14, 8, 0, 0);
        barra.Controls.Add(_lblConteo);
        raiz.Controls.Add(barra, 0, 0);

        ConfigurarGrilla();
        raiz.Controls.Add(_grid, 0, 1);

        Cargar();
    }

    private static Button CrearBoton(string texto, EventHandler alClic)
    {
        var b = new Button { Text = texto, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        b.Click += alClic;
        return b;
    }

    private void ConfigurarGrilla()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.BackgroundColor = SystemColors.Window;

        // Función local para agregar columnas sin repetir tanto código
        void Col(string propiedad, string titulo, int ancho, string? formato = null)
        {
            var col = new DataGridViewTextBoxColumn { DataPropertyName = propiedad, HeaderText = titulo, Width = ancho };
            if (formato != null)
                col.DefaultCellStyle = new DataGridViewCellStyle
                { Format = formato, Alignment = DataGridViewContentAlignment.MiddleRight };
            _grid.Columns.Add(col);
        }

        Col(nameof(FacturaRow.CreadoLocal), "Fecha", 130);
        Col(nameof(FacturaRow.Cliente), "Cliente", 190);
        Col(nameof(FacturaRow.Cuit), "CUIT", 100);
        Col(nameof(FacturaRow.Tipo), "Tipo", 45);
        Col(nameof(FacturaRow.Comprobante), "Comprobante", 120);
        Col(nameof(FacturaRow.ImporteNeto), "Neto", 90, "N2");
        Col(nameof(FacturaRow.ImporteIva), "IVA", 80, "N2");
        Col(nameof(FacturaRow.Importe), "Total", 95, "N2");
        Col(nameof(FacturaRow.Periodo), "Período", 190);
        Col(nameof(FacturaRow.Cae), "CAE", 125);
        Col(nameof(FacturaRow.Estado), "Estado", 85);
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(FacturaRow.Error),
            HeaderText = "Detalle / error",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        // Los errores se ven en rojo y los pendientes de verificar en naranja
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not FacturaRow f) return;
            if (f.Estado == "Error") e.CellStyle!.ForeColor = Color.Firebrick;
            else if (f.Estado == "Enviando") e.CellStyle!.ForeColor = Color.DarkOrange;
        };

        // Doble click = abrir el PDF
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) AbrirPdf(); };
    }

    private void Cargar()
    {
        _todas = _db.Historial(_s.Ambiente, 1000);
        AplicarFiltro();
    }

    /// <summary>Muestra solo las facturas del estado elegido.</summary>
    private void AplicarFiltro()
    {
        IEnumerable<FacturaRow> visibles = _cmbFiltro.SelectedIndex switch
        {
            1 => _todas.Where(f => f.Estado == "Autorizada"),
            2 => _todas.Where(f => f.Estado == "Error"),
            3 => _todas.Where(f => f.Estado == "Enviando"),
            _ => _todas
        };

        var lista = visibles.ToList();
        _grid.DataSource = lista;

        _lblConteo.Text = $"Mostrando {lista.Count} de {_todas.Count} · " +
                          $"Autorizadas: {_todas.Count(f => f.Estado == "Autorizada")} · " +
                          $"Rechazadas / con error: {_todas.Count(f => f.Estado == "Error")} · " +
                          $"Pendientes de verificar: {_todas.Count(f => f.Estado == "Enviando")}";
    }

    private void AbrirPdf()
    {
        if (_grid.CurrentRow?.DataBoundItem is not FacturaRow f) return;

        if (string.IsNullOrEmpty(f.PdfPath) || !File.Exists(f.PdfPath) ||
            !string.Equals(Path.GetExtension(f.PdfPath), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this,
                "Esta factura no tiene PDF disponible. Si está autorizada, en la ventana principal el botón \"Ver PDFs\" lo genera.",
                "PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(f.PdfPath) { UseShellExecute = true });
    }

    // Pregunta a ARCA por las facturas que quedaron "Enviando" y las deja en su estado real
    private async void OnReconciliar(object? sender, EventArgs e)
    {
        var mensajes = new List<string>();
        try
        {
            UseWaitCursor = true;
            var n = await _billing.ReconciliarEnviandoAsync(new ProgresoDirecto(mensajes.Add));

            var texto = mensajes.Count == 0
                ? "No hay comprobantes pendientes de verificar."
                : $"Comprobantes aclarados: {n}.\n\n{string.Join("\n", mensajes)}";
            MessageBox.Show(this, texto, "Reconciliar pendientes", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Cargar();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }
}
