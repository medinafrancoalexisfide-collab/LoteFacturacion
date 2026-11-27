using System.ComponentModel;
using FacturacionLote.Config;
using FacturacionLote.Domain;
using FacturacionLote.Infrastructure;
using FacturacionLote.Services;

namespace FacturacionLote.UI;

/// <summary>
/// Muestra las facturas autorizadas con el e-mail de cada cliente (leído de Don Gestión) y las envía.
/// En modo prueba NINGUNA factura se envía al cliente: todas van a la casilla de prueba.
/// </summary>
public sealed class EnvioEmailForm : Form
{
    private readonly AppSettings _s;
    private readonly Database _db;
    private readonly DonGestionClient _dg;
    private readonly BillingService _billing;
    private readonly EmailService _email;

    private readonly Label _lblModo = new();
    private readonly Label _lblResumen = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _log = new();
    private readonly Button _btnActualizar = new();
    private readonly Button _btnPrueba = new();
    private readonly Button _btnEnviar = new();

    private BindingList<FilaEmail> _filas = new();
    private bool _ocupado;

    public EnvioEmailForm(AppSettings settings, Database db, DonGestionClient dg, BillingService billing)
    {
        _s = settings;
        _db = db;
        _dg = dg;
        _billing = billing;
        _email = new EmailService(settings);

        Text = "Enviar facturas por e-mail";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1100, 640);

        // Estructura vertical: cartel de modo / botones / grilla / registro
        var raiz = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        raiz.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        Controls.Add(raiz);

        // ---- Cartel que dice si el envío es de PRUEBA o REAL ----
        _lblModo.Dock = DockStyle.Fill;
        _lblModo.AutoSize = false;
        _lblModo.Height = 34;
        _lblModo.TextAlign = ContentAlignment.MiddleCenter;
        _lblModo.Font = new Font(Font, FontStyle.Bold);
        _lblModo.ForeColor = Color.White;
        raiz.Controls.Add(_lblModo, 0, 0);

        // ---- Botones ----
        var barra = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(6) };
        ConfigurarBoton(_btnActualizar, "Actualizar", async (_, _) => await CargarAsync());
        ConfigurarBoton(_btnPrueba, "Enviar e-mail de prueba (sin factura)", OnPrueba);
        ConfigurarBoton(_btnEnviar, "Enviar facturas por e-mail", OnEnviar);
        _btnEnviar.Font = new Font(Font, FontStyle.Bold);
        _lblResumen.AutoSize = true;
        _lblResumen.Margin = new Padding(14, 8, 0, 0);
        barra.Controls.Add(_btnActualizar);
        barra.Controls.Add(_btnPrueba);
        barra.Controls.Add(_btnEnviar);
        barra.Controls.Add(_lblResumen);
        raiz.Controls.Add(barra, 0, 1);

        // ---- Grilla ----
        ConfigurarGrilla();
        raiz.Controls.Add(_grid, 0, 2);

        // ---- Registro de actividad ----
        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Font = new Font("Consolas", 9F);
        raiz.Controls.Add(_log, 0, 3);

        ActualizarBanner();

        // Al abrir, carga las facturas y los e-mails
        Shown += async (_, _) => await CargarAsync();

        FormClosing += (_, e) =>
        {
            if (_ocupado)
            {
                e.Cancel = true;
                MessageBox.Show(this, "Hay un envío en curso. Esperá a que termine para cerrar la ventana.",
                    "Operación en curso", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
    }

    // ------------------------------------------------------------------
    //  Interfaz
    // ------------------------------------------------------------------

    private static void ConfigurarBoton(Button b, string texto, EventHandler alClic)
    {
        b.Text = texto;
        b.AutoSize = true;
        b.Padding = new Padding(6, 2, 6, 2);
        b.Click += alClic;
    }

    private void ConfigurarGrilla()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = SystemColors.Window;

        _grid.Columns.Add(new DataGridViewCheckBoxColumn
            { DataPropertyName = nameof(FilaEmail.Enviar), HeaderText = "Enviar", Width = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { DataPropertyName = nameof(FilaEmail.Cliente), HeaderText = "Cliente", ReadOnly = true, Width = 220 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { DataPropertyName = nameof(FilaEmail.Comprobante), HeaderText = "Comprobante", ReadOnly = true, Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { DataPropertyName = nameof(FilaEmail.EmailCliente), HeaderText = "Email del cliente", ReadOnly = true, Width = 230 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { DataPropertyName = nameof(FilaEmail.Destino), HeaderText = "Se enviará a", ReadOnly = true, Width = 210 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(FilaEmail.Estado),
            HeaderText = "Estado",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });

        // Las casillas confirman su cambio al instante
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        // Las filas que no se pueden enviar (sin PDF o sin e-mail válido) no se editan y se ven en gris
        _grid.CellBeginEdit += (_, e) =>
        {
            if (_grid.Rows[e.RowIndex].DataBoundItem is FilaEmail f && !f.Habilitada) e.Cancel = true;
        };
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is FilaEmail f && !f.Habilitada)
                e.CellStyle!.ForeColor = Color.Gray;
        };
        _grid.DataError += (_, e) => e.ThrowException = false;
    }

    private void ActualizarBanner()
    {
        if (_email.EnviarAClientesReales)
        {
            _lblModo.Text = "ENVÍO REAL: las facturas se enviarán a los e-mails de los clientes";
            _lblModo.BackColor = Color.Firebrick;
        }
        else
        {
            var destino = string.IsNullOrWhiteSpace(_s.EmailPruebaDestino) ? "(sin configurar)" : _s.EmailPruebaDestino.Trim();
            _lblModo.Text = $"MODO PRUEBA: no se envía nada a los clientes. Todo se envía a: {destino}";
            _lblModo.BackColor = Color.DarkOrange;
        }
    }

    private void ActualizarResumen()
    {
        var elegidas = _filas.Count(f => f.Enviar && f.Habilitada);
        _lblResumen.Text = $"{elegidas} seleccionadas de {_filas.Count} facturas autorizadas";
        _btnEnviar.Enabled = !_ocupado && elegidas > 0;
    }

    private void SetOcupado(bool ocupado)
    {
        _ocupado = ocupado;
        _btnActualizar.Enabled = !ocupado;
        _btnPrueba.Enabled = !ocupado;
        _grid.Enabled = !ocupado;
        if (ocupado) _btnEnviar.Enabled = false;
        UseWaitCursor = ocupado;
    }

    private void Log(string mensaje)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {mensaje}{Environment.NewLine}");
        Registro.Escribir(mensaje);
    }

    private static string Corto(string s) => s.Length <= 80 ? s : s[..80] + "…";

    // ------------------------------------------------------------------
    //  Cargar las facturas y los e-mails de los clientes
    // ------------------------------------------------------------------

    private async Task CargarAsync()
    {
        SetOcupado(true);
        try
        {
            // Se asegura de que cada factura autorizada tenga su PDF
            var generados = _billing.GenerarPdfsFaltantes(new ProgresoDirecto(Log));
            if (generados > 0) Log($"Se generaron {generados} PDF pendientes.");

            // E-mails de los clientes, tomados de Don Gestión
            var emails = new Dictionary<int, string?>();
            try
            {
                foreach (var c in await _dg.ListarClientesAsync())
                    emails[c.Id] = c.Email;
                var conEmail = emails.Count(e => !string.IsNullOrWhiteSpace(e.Value));
                Log($"Don Gestión: {emails.Count} clientes leídos, {conEmail} con e-mail cargado.");
            }
            catch (Exception ex)
            {
                Log($"No se pudo leer Don Gestión ({ex.Message}). No se pueden obtener los e-mails de los clientes.");
            }

            var ultimos = _db.UltimosEnviosEmail();
            var filas = new List<FilaEmail>();

            foreach (var f in _db.Historial(_s.Ambiente, 1000).Where(x => x.Estado == "Autorizada"))
            {
                emails.TryGetValue(f.ClienteId, out var crudo);
                var (valido, normalizado) = EmailService.AnalizarEmail(crudo);
                var tienePdf = !string.IsNullOrEmpty(f.PdfPath) && File.Exists(f.PdfPath);
                ultimos.TryGetValue(f.Id, out var ultimo);

                string estado;
                var habilitada = true;
                if (!tienePdf)
                {
                    estado = "Sin PDF";
                    habilitada = false;
                }
                else if (!valido)
                {
                    estado = string.IsNullOrWhiteSpace(crudo) ? "Sin e-mail cargado en Don Gestión" : "E-mail no válido en Don Gestión";
                    habilitada = false;
                }
                else if (ultimo is { Estado: "Enviado" })
                    estado = $"Enviado el {ultimo.CreadoLocal} a {ultimo.Destino} ({ultimo.Modo})";
                else if (ultimo is { Estado: "Error" })
                    estado = "Error en el último intento: " + Corto(ultimo.Error ?? "");
                else
                    estado = "Sin enviar";

                var yaEnviada = ultimo is { Estado: "Enviado" };

                filas.Add(new FilaEmail
                {
                    FacturaId = f.Id,
                    Cliente = f.Cliente,
                    Comprobante = $"{f.Tipo} {f.Comprobante}",
                    PdfPath = f.PdfPath ?? "",
                    EmailCliente = valido ? normalizado
                        : string.IsNullOrWhiteSpace(crudo) ? "(sin e-mail)" : crudo.Trim(),
                    EmailNormalizado = valido ? normalizado : "",
                    Destino = valido ? _email.DestinoReal(normalizado) : "",
                    Habilitada = habilitada,
                    Enviar = habilitada && !yaEnviada,   // por defecto, las que todavía no se enviaron
                    Estado = estado
                });
            }

            _filas = new BindingList<FilaEmail>(filas);
            foreach (var fila in _filas)
                fila.PropertyChanged += (_, _) => ActualizarResumen();
            _grid.DataSource = _filas;

            Log($"Facturas autorizadas: {filas.Count}. Con e-mail válido: {filas.Count(x => x.EmailNormalizado.Length > 0)}.");
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

    // ------------------------------------------------------------------
    //  Enviar
    // ------------------------------------------------------------------

    private async void OnPrueba(object? sender, EventArgs e)
    {
        if (!_s.EmailConfigurado(out var falta))
        {
            MessageBox.Show(this, $"Falta completar en Configuración (e-mail): {falta}.", "Configuración incompleta",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetOcupado(true);
        try
        {
            await _email.EnviarPruebaAsync();
            Log($"E-mail de prueba enviado a {_s.EmailPruebaDestino.Trim()}. Revisá la bandeja de entrada y el correo no deseado.");
            MessageBox.Show(this, $"Se envió un e-mail de prueba a {_s.EmailPruebaDestino.Trim()}.\n\nRevisá la bandeja de entrada y el correo no deseado.",
                "E-mail de prueba", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}");
            MessageBox.Show(this,
                ex.Message + "\n\nSi usás Gmail: activá la verificación en 2 pasos y creá una \"contraseña de aplicación\" " +
                "(no sirve la contraseña normal de la cuenta). Servidor smtp.gmail.com, puerto 587.",
                "No se pudo enviar", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetOcupado(false);
            ActualizarResumen();
        }
    }

    private async void OnEnviar(object? sender, EventArgs e)
    {
        var elegidas = _filas.Where(f => f.Enviar && f.Habilitada).ToList();
        if (elegidas.Count == 0)
        {
            MessageBox.Show(this, "No hay facturas seleccionadas.", "Enviar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_s.EmailConfigurado(out var falta))
        {
            MessageBox.Show(this, $"Falta completar en Configuración (e-mail): {falta}.", "Configuración incompleta",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var aDonde = _email.EnviarAClientesReales
            ? "a los e-mails de los CLIENTES"
            : $"a la casilla de PRUEBA ({_s.EmailPruebaDestino.Trim()}), no a los clientes";

        var mensajeEnvio = $"Se enviarán {elegidas.Count} facturas {aDonde}.";
        if (_email.EnviarAClientesReales && _s.ConfirmacionReforzada)
        {
            // Envío a clientes reales: también exige escribir una palabra (los e-mails no se pueden retirar)
            if (!ConfirmacionEscrita.Pedir(this, "Confirmar envío a CLIENTES", mensajeEnvio, "ENVIAR")) return;
        }
        else
        {
            var confirmar = MessageBox.Show(this, mensajeEnvio + "\n\n¿Continuar?",
                "Confirmar envío", MessageBoxButtons.YesNo,
                _email.EnviarAClientesReales ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirmar != DialogResult.Yes) return;
        }

        SetOcupado(true);
        int ok = 0, errores = 0;
        try
        {
            foreach (var fila in elegidas)
            {
                fila.Estado = "Enviando…";
                Log($"Email del cliente: {fila.EmailCliente}");

                var destino = _email.DestinoReal(fila.EmailNormalizado);
                try
                {
                    var registro = _db.Obtener(fila.FacturaId)
                                   ?? throw new InvalidOperationException("La factura ya no existe en la base.");
                    await _email.EnviarFacturaAsync(registro, fila.EmailNormalizado, fila.PdfPath);

                    Registrar(fila, destino, true, null);
                    ok++;
                    fila.Estado = $"Enviado a {destino} ({_email.Modo})";
                    Log($"  → {fila.Comprobante} ({fila.Cliente}): enviado a {destino} [{_email.Modo}].");
                }
                catch (Exception ex)
                {
                    Registrar(fila, destino, false, ex.Message);
                    errores++;
                    fila.Estado = "Error: " + Corto(ex.Message);
                    Log($"  → {fila.Comprobante} ({fila.Cliente}): error. {ex.Message}");
                }
            }
        }
        finally
        {
            SetOcupado(false);
            ActualizarResumen();
        }

        var cierre = $"Envío terminado ({_email.Modo}): {ok} enviados, {errores} con error.";
        Log(cierre);
        MessageBox.Show(this, cierre, "Resultado", MessageBoxButtons.OK,
            errores > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
    }

    // Guarda el resultado del envío; si fallara el guardado, no interrumpe el resto del lote
    private void Registrar(FilaEmail fila, string destino, bool ok, string? error)
    {
        try
        {
            _db.RegistrarEnvioEmail(fila.FacturaId, fila.EmailNormalizado, destino, _email.Modo, ok, error);
        }
        catch (Exception ex)
        {
            Log($"  Aviso: no se pudo guardar el registro del envío ({ex.Message}).");
        }
    }
}
