using System.ComponentModel;
using System.Diagnostics;
using FacturacionLote.Config;
using FacturacionLote.Domain;
using FacturacionLote.Infrastructure;
using FacturacionLote.Services;

namespace FacturacionLote.UI;

public sealed class MainForm : Form
{
    private AppSettings _s = SettingsStore.Load();
    private readonly Database _db = new();
    private DonGestionClient _dg = null!;
    private ArcaClient _arca = null!;
    private BillingService _billing = null!;

    private readonly Label _lblAmbiente = new();
    private readonly Label _lblEmisor = new();
    private readonly DateTimePicker _dtpEmision = new();
    private readonly Label _lblPeriodo = new();
    private readonly Button _btnConfig = new();
    private readonly Button _btnCargar = new();
    private readonly Button _btnFacturar = new();
    private readonly Button _btnCancelar = new();
    private readonly ComboBox _cmbFiltro = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly CheckBox _chkTodos = new();
    private readonly Label _lblResumen = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _log = new();

    private CancellationTokenSource? _cts;      // permite cancelar el lote entre facturas
    private List<FilaLote> _todas = new();      // todas las filas cargadas
    private BindingList<FilaLote> _filas = new(); // las filas visibles según el filtro
    private bool _suprimirTodos;                // evita que "marcar todas" se dispare al recargar la lista
    private bool _ocupado;

    public MainForm()
    {
        ReconstruirServicios();
        ConstruirUi();
        ActualizarBanner();
        ActualizarPeriodo();
        ActualizarResumen();

        // "Shown" ocurre cuando la ventana ya apareció: si falta configuración, la pide
        Shown += (_, _) =>
        {
            if (!_s.EstaCompleta(out _)) AbrirConfiguracion();
        };

        FormClosing += (_, e) =>
        {
            if (_ocupado)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Hay una operación en curso. Esperá a que termine para cerrar el programa.",
                    "Operación en curso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
    }

    // ------------------------------------------------------------------
    //  Construcción de la interfaz
    // ------------------------------------------------------------------

    private void ConstruirUi()
    {
        Text = "Facturación por lote (v0.3 demo)";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1200, 700);
        MinimumSize = new Size(960, 560);

        // Estructura vertical: barra superior / grilla / barra inferior / registro
        var raiz = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        Controls.Add(raiz);

        // ---- Barra superior ----
        var barra = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(6) };

        _lblAmbiente.AutoSize = false;
        _lblAmbiente.Size = new Size(300, 30);
        _lblAmbiente.TextAlign = ContentAlignment.MiddleCenter;
        _lblAmbiente.Font = new Font(Font, FontStyle.Bold);
        barra.Controls.Add(_lblAmbiente);

        _lblEmisor.AutoSize = true;
        _lblEmisor.Margin = new Padding(8, 8, 8, 0);
        barra.Controls.Add(_lblEmisor);

        ConfigurarBoton(_btnConfig, "Configuración", (_, _) => AbrirConfiguracion());
        barra.Controls.Add(_btnConfig);

        var btnHistorial = new Button();
        ConfigurarBoton(btnHistorial, "Historial", (_, _) =>
        {
            using var dlg = new HistorialForm(_s, _db, _billing);
            dlg.ShowDialog(this);
        });
        barra.Controls.Add(btnHistorial);

        var btnEmail = new Button();
        ConfigurarBoton(btnEmail, "Enviar facturas por e-mail", (_, _) =>
        {
            if (!ConfigOk()) return;
            using var dlg = new EnvioEmailForm(_s, _db, _dg, _billing);
            dlg.ShowDialog(this);
        });
        barra.Controls.Add(btnEmail);

        barra.Controls.Add(new Label { Text = "Fecha de emisión:", AutoSize = true, Margin = new Padding(14, 9, 4, 0) });
        _dtpEmision.Format = DateTimePickerFormat.Short;
        _dtpEmision.Width = 110;
        _dtpEmision.Value = DateTime.Today;
        // Regla de ARCA para servicios: la fecha del comprobante admite hasta 10 días antes o después de hoy
        _dtpEmision.MinDate = DateTime.Today.AddDays(-10);
        _dtpEmision.MaxDate = DateTime.Today.AddDays(10);
        _dtpEmision.ValueChanged += (_, _) => ActualizarPeriodo();
        barra.Controls.Add(_dtpEmision);

        _lblPeriodo.AutoSize = true;
        _lblPeriodo.Margin = new Padding(14, 9, 14, 0);
        barra.Controls.Add(_lblPeriodo);

        ConfigurarBoton(_btnCargar, "Cargar clientes y ventas", OnCargar);
        barra.Controls.Add(_btnCargar);
        raiz.Controls.Add(barra, 0, 0);

        // ---- Grilla ----
        ConfigurarGrilla();
        raiz.Controls.Add(_grid, 0, 1);

        // ---- Barra inferior ----
        var abajo = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(6) };

        abajo.Controls.Add(new Label { Text = "Mostrar:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        _cmbFiltro.Items.AddRange(new object[] { "Mostrar todo", "Solo pendientes", "Solo facturadas", "Solo con problemas" });
        _cmbFiltro.SelectedIndex = 0;
        _cmbFiltro.SelectedIndexChanged += (_, _) => AplicarFiltro();
        _cmbFiltro.Margin = new Padding(0, 3, 14, 0);
        abajo.Controls.Add(_cmbFiltro);

        _chkTodos.Text = "Marcar / desmarcar todas (las visibles)";
        _chkTodos.AutoSize = true;
        _chkTodos.Margin = new Padding(0, 4, 14, 0);
        _chkTodos.CheckedChanged += (_, _) =>
        {
            if (_suprimirTodos) return;
            foreach (var f in _filas)
                if (f.Habilitada) f.Facturar = _chkTodos.Checked;
        };
        abajo.Controls.Add(_chkTodos);

        _lblResumen.AutoSize = true;
        _lblResumen.Margin = new Padding(0, 4, 14, 0);
        abajo.Controls.Add(_lblResumen);

        ConfigurarBoton(_btnFacturar, "Facturar lote", OnFacturar);
        _btnFacturar.Font = new Font(Font, FontStyle.Bold);
        _btnFacturar.Enabled = false;
        abajo.Controls.Add(_btnFacturar);

        ConfigurarBoton(_btnCancelar, "Cancelar", (_, _) =>
        {
            _cts?.Cancel();
            Log("Cancelando: se detendrá después de la factura en curso…");
        });
        _btnCancelar.Enabled = false;
        abajo.Controls.Add(_btnCancelar);

        var btnPdfs = new Button();
        ConfigurarBoton(btnPdfs, "Ver PDFs", OnVerPdfs);
        abajo.Controls.Add(btnPdfs);
        raiz.Controls.Add(abajo, 0, 2);

        // ---- Registro de actividad ----
        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Font = new Font("Consolas", 9F);
        raiz.Controls.Add(_log, 0, 3);
    }

    private static DataGridViewTextBoxColumn Columna(string propiedad, string titulo, int ancho,
        string? formato = null, DataGridViewContentAlignment? alineacion = null)
    {
        var col = new DataGridViewTextBoxColumn
        {
            DataPropertyName = propiedad,
            HeaderText = titulo,
            ReadOnly = true,
            Width = ancho
        };
        if (formato != null || alineacion != null)
        {
            col.DefaultCellStyle = new DataGridViewCellStyle
            {
                Format = formato ?? "",
                Alignment = alineacion ?? DataGridViewContentAlignment.MiddleLeft
            };
        }
        return col;
    }

    private void ConfigurarGrilla()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;   // las columnas las definimos nosotros
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = SystemColors.Window;

        // DataPropertyName enlaza cada columna con una propiedad de FilaLote
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
            { DataPropertyName = nameof(FilaLote.Facturar), HeaderText = "Facturar", Width = 60 });
        _grid.Columns.Add(Columna(nameof(FilaLote.Cliente), "Cliente", 230));
        _grid.Columns.Add(Columna(nameof(FilaLote.Cuit), "CUIT", 100));
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(FilaLote.CondIva),
            HeaderText = "Cond. IVA del cliente",
            DataSource = Catalogos.CondicionesIvaGrilla,
            DisplayMember = nameof(CondIvaItem.Texto),
            ValueMember = nameof(CondIvaItem.Id),
            FlatStyle = FlatStyle.Flat,
            Width = 190
        });
        _grid.Columns.Add(Columna(nameof(FilaLote.TipoFactura), "Tipo", 45, null, DataGridViewContentAlignment.MiddleCenter));
        _grid.Columns.Add(Columna(nameof(FilaLote.Ventas), "Ventas", 55, null, DataGridViewContentAlignment.MiddleRight));
        _grid.Columns.Add(Columna(nameof(FilaLote.Neto), "Neto", 95, "N2", DataGridViewContentAlignment.MiddleRight));
        _grid.Columns.Add(Columna(nameof(FilaLote.Iva), "IVA", 85, "N2", DataGridViewContentAlignment.MiddleRight));
        _grid.Columns.Add(Columna(nameof(FilaLote.Importe), "Total", 100, "N2", DataGridViewContentAlignment.MiddleRight));
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(FilaLote.Estado),
            HeaderText = "Estado",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        // Las casillas y listas confirman su cambio al instante (si no, esperan a salir de la celda)
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        // Las filas bloqueadas no se editan; la casilla "Facturar" solo se puede tildar si la fila está habilitada
        _grid.CellBeginEdit += (_, e) =>
        {
            if (_grid.Rows[e.RowIndex].DataBoundItem is not FilaLote f) return;
            var esFacturar = _grid.Columns[e.ColumnIndex].DataPropertyName == nameof(FilaLote.Facturar);
            if (f.Bloqueada || (esFacturar && !f.Habilitada)) e.Cancel = true;
        };

        // Colores: gris = bloqueada, naranja = falta confirmar algo, rojo = rechazada o con error
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not FilaLote f) return;
            if (f.Bloqueada) e.CellStyle!.ForeColor = Color.Gray;
            else if (f.Estado.StartsWith("Rechazada") || f.Estado.StartsWith("Error")) e.CellStyle!.ForeColor = Color.Firebrick;
            else if (!f.Habilitada || f.Estado.StartsWith("Pendiente ·")) e.CellStyle!.ForeColor = Color.DarkOrange;
        };
        _grid.DataError += (_, e) => e.ThrowException = false;
    }

    private static void ConfigurarBoton(Button b, string texto, EventHandler alClic)
    {
        b.Text = texto;
        b.AutoSize = true;
        b.Padding = new Padding(6, 2, 6, 2);
        b.Click += alClic;
    }

    // ------------------------------------------------------------------
    //  Cargar la lista y filtrarla
    // ------------------------------------------------------------------

    /// <summary>Consulta Don Gestión y arma la grilla. La usan el botón "Cargar" y el refresco automático después de un lote sin errores.</summary>
    private async Task CargarListaAsync()
    {
        var emision = DateOnly.FromDateTime(_dtpEmision.Value);
        Log("Consultando clientes y ventas en Don Gestión…");
        var res = await _billing.PrepararLoteAsync(emision, CancellationToken.None);

        _todas = res.Filas;

        // Cada vez que el usuario cambia algo en una fila, lo guardamos en SQLite
        foreach (var fila in _todas)
        {
            fila.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(FilaLote.Facturar))
                    _db.GuardarSeleccion(fila.ClienteId, fila.Facturar);
                else if (args.PropertyName == nameof(FilaLote.CondIva) && fila.CondIva != 0)
                    _db.GuardarCondIva(fila.ClienteId, fila.CondIva);
                ActualizarResumen();
            };
        }

        AplicarFiltro();
        Log(res.Resumen);
    }

    /// <summary>Muestra solo las filas que corresponden al filtro elegido ("lo que ves es lo que se factura").</summary>
    private void AplicarFiltro()
    {
        IEnumerable<FilaLote> visibles = _cmbFiltro.SelectedIndex switch
        {
            1 => _todas.Where(f => f.Grupo == GrupoFila.Pendiente),
            2 => _todas.Where(f => f.Grupo == GrupoFila.Facturada),
            3 => _todas.Where(f => f.Grupo == GrupoFila.Problema),
            _ => _todas
        };

        _filas = new BindingList<FilaLote>(visibles.ToList());
        _grid.DataSource = _filas;

        var habilitadas = _filas.Where(f => f.Habilitada).ToList();
        _suprimirTodos = true;
        _chkTodos.Checked = habilitadas.Count > 0 && habilitadas.All(f => f.Facturar);
        _suprimirTodos = false;

        ActualizarResumen();
    }

    // ------------------------------------------------------------------
    //  Acciones
    // ------------------------------------------------------------------

    private async void OnCargar(object? sender, EventArgs e)
    {
        if (!ConfigOk()) return;
        SetOcupado(true);
        try
        {
            await CargarListaAsync();
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "No se pudo cargar la lista", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetOcupado(false);
            ActualizarResumen();
        }
    }

    private void OnVerPdfs(object? sender, EventArgs e)
    {
        try
        {
            // Primero genera los PDF que falten (por ejemplo, facturas emitidas antes de tener esta función)
            var n = _billing.GenerarPdfsFaltantes(new ProgresoDirecto(Log));
            if (n > 0) Log($"Se generaron {n} PDF pendientes.");

            var carpeta = AppPaths.FacturasDir(_s.Ambiente);
            Directory.CreateDirectory(carpeta);
            Process.Start(new ProcessStartInfo(carpeta) { UseShellExecute = true });   // abre la carpeta en el Explorador
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "No se pudieron generar los PDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnFacturar(object? sender, EventArgs e)
    {
        if (!ConfigOk()) return;

        // Solo se facturan las filas VISIBLES y tildadas
        var elegidas = _filas.Where(f => f.Facturar && f.Habilitada).ToList();
        if (elegidas.Count == 0)
        {
            MessageBox.Show(this, "No hay facturas seleccionadas.", "Facturar lote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var emision = DateOnly.FromDateTime(_dtpEmision.Value);
        var (desde, hasta, vto) = BillingService.CalcularPeriodo(emision);
        var total = elegidas.Sum(f => f.Importe);
        var tipos = string.Join(", ", elegidas
            .GroupBy(f => f.TipoFactura)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Count()} Factura {g.Key}"));

        // Siempre se pide confirmación mostrando exactamente qué se va a emitir
        var texto = $"Se emitirán {elegidas.Count} facturas ({tipos}) por un total de $ {total:N2}.\n\n" +
                    $"Emisión: {emision:dd/MM/yyyy}\n" +
                    $"Servicio: {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}\n" +
                    $"Vencimiento: {vto:dd/MM/yyyy}\n" +
                    $"Ambiente: {_s.Ambiente}";
        if (_cmbFiltro.SelectedIndex != 0)
            texto += $"\n\nAtención: hay un filtro activo (\"{_cmbFiltro.SelectedItem}\"); solo se incluyen las filas que se ven.";

        if (_s.UsarProduccion && _s.ConfirmacionReforzada)
        {
            // En producción no alcanza con un "Sí": hay que escribir la palabra PRODUCCION
            texto += "\n\nPRODUCCIÓN: los comprobantes tendrán validez fiscal y NO se pueden deshacer.";
            if (!ConfirmacionEscrita.Pedir(this, "Confirmar emisión en PRODUCCIÓN", texto, "PRODUCCION")) return;
        }
        else
        {
            if (_s.UsarProduccion)
                texto += "\n\nPRODUCCIÓN: los comprobantes tendrán validez fiscal y NO se pueden deshacer.";
            var confirmar = MessageBox.Show(this, texto + "\n\n¿Continuar?", "Confirmar emisión",
                MessageBoxButtons.YesNo, _s.UsarProduccion ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);   // Button2 = "No" queda seleccionado por defecto
            if (confirmar != DialogResult.Yes) return;
        }
        Log($"Emisión confirmada: {elegidas.Count} facturas en {_s.Ambiente}.");

        _cts = new CancellationTokenSource();
        SetOcupado(true);
        var progreso = new ProgresoDirecto(Log);   // muestra cada mensaje al instante
        try
        {
            var r = await _billing.EmitirLoteAsync(elegidas, emision, progreso, _cts.Token);
            var cierre = $"Lote terminado: {r.Emitidas} emitidas, {r.Errores} con error, {r.Inciertas} inciertas" +
                         (r.Cancelado ? " (cancelado por el usuario)." : ".");
            Log(cierre);
            MessageBox.Show(this, cierre, "Resultado", MessageBoxButtons.OK,
                r.Errores + r.Inciertas > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            // Si salió todo bien, se refresca la lista: las facturas emitidas pasan a "Ya facturada".
            // Si hubo errores, no se refresca para no borrar de la grilla los motivos de rechazo.
            if (r.Errores + r.Inciertas == 0)
            {
                try { await CargarListaAsync(); }
                catch (Exception exRecarga) { Log($"No se pudo refrescar la lista: {exRecarga.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Error al procesar el lote", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetOcupado(false);
            ActualizarResumen();
        }
    }

    private void AbrirConfiguracion()
    {
        using var dlg = new SettingsForm(_s);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _s = dlg.Resultado;
        SettingsStore.Save(_s);
        ReconstruirServicios();
        ActualizarBanner();

        // La lista cargada puede haber quedado desactualizada (por ejemplo, si cambió el tipo de emisor)
        _todas = new List<FilaLote>();
        AplicarFiltro();
        Log("Configuración guardada (los secretos se almacenan cifrados). Volvé a cargar la lista.");
    }

    // ------------------------------------------------------------------
    //  Utilidades de la interfaz
    // ------------------------------------------------------------------

    // Se vuelve a crear cuando cambia la configuración, para que use los datos nuevos
    private void ReconstruirServicios()
    {
        _dg = new DonGestionClient(_s);
        _arca = new ArcaClient(_s);
        _billing = new BillingService(_s, _dg, _arca, _db, new FacturaPdfService(_s));
    }

    private bool ConfigOk()
    {
        try
        {
            ArcaEndpoints.Resolver(_s.UsarProduccion, _s.ArcaUrlWsaa, _s.ArcaUrlWsfe);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message + "\n\nRevisá settings.json.", "Dirección de ARCA no válida",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (_s.EstaCompleta(out var falta)) return true;
        MessageBox.Show(this, $"Falta completar en Configuración: {falta}.", "Configuración incompleta",
            MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private void ActualizarBanner()
    {
        if (_s.UsarProduccion)
        {
            _lblAmbiente.Text = "AMBIENTE: PRODUCCIÓN (validez fiscal)";
            _lblAmbiente.BackColor = Color.Firebrick;
        }
        else
        {
            _lblAmbiente.Text = "AMBIENTE: HOMOLOGACIÓN (pruebas)";
            _lblAmbiente.BackColor = Color.SeaGreen;
        }
        _lblAmbiente.ForeColor = Color.White;

        _lblEmisor.Text = _s.EmisorResponsableInscripto
            ? "Emisor: Responsable Inscripto (Facturas A y B)"
            : "Emisor: Monotributista (Factura C)";
    }

    private void ActualizarPeriodo()
    {
        var (d, h, v) = BillingService.CalcularPeriodo(DateOnly.FromDateTime(_dtpEmision.Value));
        _lblPeriodo.Text = $"Servicio: {d:dd/MM/yyyy} al {h:dd/MM/yyyy}   ·   Vencimiento: {v:dd/MM/yyyy}";
    }

    private void ActualizarResumen()
    {
        var elegidas = _filas.Where(f => f.Facturar && f.Habilitada).ToList();
        _lblResumen.Text = $"{_filas.Count} visibles · {elegidas.Count} seleccionadas · Total $ {elegidas.Sum(f => f.Importe):N2}";
        _btnFacturar.Enabled = !_ocupado && elegidas.Count > 0;
    }

    private void SetOcupado(bool ocupado)
    {
        _ocupado = ocupado;
        _btnCargar.Enabled = !ocupado;
        _btnConfig.Enabled = !ocupado;
        _cmbFiltro.Enabled = !ocupado;
        _btnCancelar.Enabled = ocupado && _cts != null;
        _grid.Enabled = !ocupado;
        _dtpEmision.Enabled = !ocupado;
        if (ocupado) _btnFacturar.Enabled = false;
        UseWaitCursor = ocupado;
    }

    // La ventana muestra el texto completo; el archivo de registro guarda una copia con los datos personales ocultos
    private void Log(string mensaje)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {mensaje}{Environment.NewLine}");
        Registro.Escribir(mensaje);
    }
}
