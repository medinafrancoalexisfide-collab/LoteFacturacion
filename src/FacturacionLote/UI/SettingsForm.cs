using System.Globalization;
using FacturacionLote.Config;
using FacturacionLote.Domain;

namespace FacturacionLote.UI;

public sealed class SettingsForm : Form
{
    private readonly AppSettings _actual;

    // Un control por cada dato a cargar
    private readonly TextBox _dgUrl = new();
    private readonly TextBox _dgClientId = new();
    private readonly TextBox _dgSecret = new() { UseSystemPasswordChar = true };
    private readonly TextBox _dgUsuario = new();
    private readonly TextBox _dgPassword = new() { UseSystemPasswordChar = true };

    private readonly TextBox _cuit = new();
    private readonly TextBox _ptoVta = new();
    private readonly TextBox _cert = new();
    private readonly TextBox _certPass = new() { UseSystemPasswordChar = true };
    private readonly ComboBox _condIva = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly TextBox _razon = new();
    private readonly TextBox _domicilio = new();
    private readonly ComboBox _emisor = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _alicuota = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _iibb = new();
    private readonly TextBox _inicio = new();
    private readonly TextBox _descripcion = new();

    private readonly TextBox _smtpHost = new();
    private readonly TextBox _smtpPuerto = new();
    private readonly TextBox _smtpUsuario = new();
    private readonly TextBox _smtpPass = new() { UseSystemPasswordChar = true };
    private readonly TextBox _emailPrueba = new();

    private int _fila; // número de fila actual de la tabla al ir agregando controles

    /// <summary>La configuración nueva, disponible cuando el usuario aprieta Guardar.</summary>
    public AppSettings Resultado { get; private set; }

    public SettingsForm(AppSettings actual)
    {
        _actual = actual;
        Resultado = actual;

        Text = "Configuración";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(660, 660);

        // Estructura: arriba un panel con scroll (los campos), abajo los botones
        var raiz = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(raiz);

        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
        raiz.Controls.Add(panel, 0, 0);

        var tabla = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        tabla.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        tabla.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(tabla);

        Titulo(tabla, "Don Gestión");
        Agregar(tabla, "URL (https://…)", _dgUrl);
        Agregar(tabla, "Client ID", _dgClientId);
        Agregar(tabla, "Client secret", _dgSecret);
        Agregar(tabla, "Usuario API", _dgUsuario);
        Agregar(tabla, "Contraseña API", _dgPassword);

        Titulo(tabla, "ARCA (web services)");
        Agregar(tabla, "CUIT del emisor", _cuit);
        Agregar(tabla, "Punto de venta", _ptoVta);

        // El certificado lleva una caja de texto y un botón "Examinar…"
        var filaCert = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        filaCert.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filaCert.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _cert.Dock = DockStyle.Fill;
        var btnExaminar = new Button { Text = "Examinar…", AutoSize = true };
        btnExaminar.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Certificado (*.pfx;*.p12)|*.pfx;*.p12|Todos|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _cert.Text = dlg.FileName;
        };
        filaCert.Controls.Add(_cert, 0, 0);
        filaCert.Controls.Add(btnExaminar, 1, 0);
        Agregar(tabla, "Certificado (.pfx)", filaCert);

        Agregar(tabla, "Contraseña del certificado", _certPass);

        _condIva.DataSource = Catalogos.CondicionesIva;
        _condIva.DisplayMember = nameof(CondIvaItem.Texto);
        _condIva.ValueMember = nameof(CondIvaItem.Id);
        Agregar(tabla, "Cond. IVA cliente por defecto", _condIva);

        Titulo(tabla, "Datos del emisor (para el PDF)");
        Agregar(tabla, "Razón social", _razon);
        Agregar(tabla, "Domicilio comercial", _domicilio);

        _emisor.DataSource = Catalogos.OpcionesEmisor;
        _emisor.DisplayMember = nameof(OpcionEmisor.Texto);
        _emisor.ValueMember = nameof(OpcionEmisor.EsResponsableInscripto);
        Agregar(tabla, "Condición del emisor", _emisor);

        _alicuota.DataSource = Catalogos.Alicuotas;
        _alicuota.DisplayMember = nameof(AlicuotaItem.Texto);
        _alicuota.ValueMember = nameof(AlicuotaItem.Porcentaje);
        Agregar(tabla, "Alícuota de IVA", _alicuota);

        Agregar(tabla, "Ingresos Brutos", _iibb);
        Agregar(tabla, "Inicio de actividades", _inicio);
        Agregar(tabla, "Descripción del servicio", _descripcion);

        Titulo(tabla, "Envío de facturas por e-mail (SMTP)");
        Agregar(tabla, "Servidor SMTP", _smtpHost);
        Agregar(tabla, "Puerto", _smtpPuerto);
        Agregar(tabla, "Usuario (tu e-mail)", _smtpUsuario);
        Agregar(tabla, "Contraseña de aplicación", _smtpPass);
        Agregar(tabla, "E-mail de prueba", _emailPrueba);

        var botones = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8)
        };
        var btnGuardar = new Button { Text = "Guardar", AutoSize = true };
        var btnCancelar = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
        btnGuardar.Click += OnGuardar;
        botones.Controls.Add(btnGuardar);
        botones.Controls.Add(btnCancelar);
        raiz.Controls.Add(botones, 0, 1);

        AcceptButton = btnGuardar;   // Enter = Guardar
        CancelButton = btnCancelar;  // Esc = Cancelar

        Cargar();
    }

    // Agrega un título de sección que ocupa las dos columnas
    private void Titulo(TableLayoutPanel t, string texto)
    {
        var lbl = new Label
        {
            Text = texto,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 14, 0, 4)
        };
        t.Controls.Add(lbl, 0, _fila);
        t.SetColumnSpan(lbl, 2);
        _fila++;
    }

    // Agrega una fila: etiqueta a la izquierda, control a la derecha
    private void Agregar(TableLayoutPanel t, string etiqueta, Control control)
    {
        var lbl = new Label
        {
            Text = etiqueta,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 8, 0)
        };
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 0, 4);
        t.Controls.Add(lbl, 0, _fila);
        t.Controls.Add(control, 1, _fila);
        _fila++;
    }

    // Pasa los valores guardados a las cajas de texto
    private void Cargar()
    {
        _dgUrl.Text = _actual.DgBaseUrl;
        _dgClientId.Text = _actual.DgClientId;
        _dgSecret.Text = _actual.DgClientSecret;
        _dgUsuario.Text = _actual.DgUsuario;
        _dgPassword.Text = _actual.DgPassword;

        _cuit.Text = _actual.CuitEmisor > 0 ? _actual.CuitEmisor.ToString(CultureInfo.InvariantCulture) : "";
        _ptoVta.Text = _actual.PuntoVenta.ToString(CultureInfo.InvariantCulture);
        _cert.Text = _actual.RutaCertificado;
        _certPass.Text = _actual.CertPassword;
        _condIva.SelectedValue = _actual.CondIvaReceptorPorDefecto;

        _razon.Text = _actual.RazonSocial;
        _domicilio.Text = _actual.Domicilio;
        _emisor.SelectedValue = _actual.EmisorResponsableInscripto;
        _alicuota.SelectedValue = _actual.AlicuotaIva;
        _iibb.Text = _actual.IngresosBrutos;
        _inicio.Text = _actual.InicioActividades;
        _descripcion.Text = _actual.DescripcionServicio;

        _smtpHost.Text = _actual.SmtpHost;
        _smtpPuerto.Text = _actual.SmtpPort.ToString(CultureInfo.InvariantCulture);
        _smtpUsuario.Text = _actual.SmtpUsuario;
        _smtpPass.Text = _actual.SmtpPassword;
        _emailPrueba.Text = _actual.EmailPruebaDestino;
    }

    // Valida lo cargado y arma la configuración nueva
    private void OnGuardar(object? sender, EventArgs e)
    {
        var cuit = Cuit.Validar(_cuit.Text);
        if (cuit == null) { Aviso("El CUIT del emisor no es válido (11 dígitos con dígito verificador correcto)."); return; }

        if (!int.TryParse(_ptoVta.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pto) || pto <= 0)
        { Aviso("El punto de venta debe ser un número mayor que cero."); return; }

        if (string.IsNullOrWhiteSpace(_dgUrl.Text)) { Aviso("Falta la URL de Don Gestión."); return; }
        if (_dgUrl.Text.Trim().StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        { Aviso("La URL de Don Gestión debe usar https://."); return; }

        if (!int.TryParse(_smtpPuerto.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var puertoSmtp)
            || puertoSmtp < 1 || puertoSmtp > 65535)
        { Aviso("El puerto SMTP debe ser un número entre 1 y 65535 (Gmail: 587)."); return; }

        if (!string.IsNullOrWhiteSpace(_emailPrueba.Text)
            && !System.Net.Mail.MailAddress.TryCreate(_emailPrueba.Text.Trim(), out _))
        { Aviso("El e-mail de prueba no es una dirección válida."); return; }

        Resultado = new AppSettings
        {
            DgBaseUrl = _dgUrl.Text.Trim(),
            DgClientId = string.IsNullOrWhiteSpace(_dgClientId.Text) ? "1" : _dgClientId.Text.Trim(),
            DgClientSecret = _dgSecret.Text,
            DgUsuario = _dgUsuario.Text.Trim(),
            DgPassword = _dgPassword.Text,

            CuitEmisor = long.Parse(cuit, CultureInfo.InvariantCulture),
            PuntoVenta = pto,
            RutaCertificado = _cert.Text.Trim(),
            CertPassword = _certPass.Text,
            CondIvaReceptorPorDefecto = _condIva.SelectedValue is int v ? v : 5,
            UsarProduccion = _actual.UsarProduccion, // solo se cambia a mano en settings.json
            ArcaUrlWsaa = _actual.ArcaUrlWsaa,       // idem: ajustes avanzados que no se editan en esta ventana
            ConfirmacionReforzada = _actual.ConfirmacionReforzada,
            ArcaUrlWsfe = _actual.ArcaUrlWsfe,

            RazonSocial = _razon.Text.Trim(),
            Domicilio = _domicilio.Text.Trim(),
            EmisorResponsableInscripto = _emisor.SelectedValue is bool esInscripto && esInscripto,
            AlicuotaIva = _alicuota.SelectedValue is decimal porcentaje ? porcentaje : 21m,
            IngresosBrutos = _iibb.Text.Trim(),
            InicioActividades = _inicio.Text.Trim(),
            DescripcionServicio = string.IsNullOrWhiteSpace(_descripcion.Text) ? "Servicio" : _descripcion.Text.Trim(),

            SmtpHost = string.IsNullOrWhiteSpace(_smtpHost.Text) ? "smtp.gmail.com" : _smtpHost.Text.Trim(),
            SmtpPort = puertoSmtp,
            SmtpUsuario = _smtpUsuario.Text.Trim(),
            SmtpPassword = _smtpPass.Text,
            EmailPruebaDestino = _emailPrueba.Text.Trim(),
            EmailEnviarAClientes = _actual.EmailEnviarAClientes   // solo se cambia a mano en settings.json
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    private void Aviso(string mensaje) =>
        MessageBox.Show(this, mensaje, "Configuración", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
