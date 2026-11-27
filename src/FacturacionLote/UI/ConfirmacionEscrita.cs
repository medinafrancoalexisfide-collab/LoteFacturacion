namespace FacturacionLote.UI;

/// <summary>
/// Ventana de confirmación reforzada: para continuar hay que ESCRIBIR una palabra (por ejemplo, PRODUCCION).
/// Se usa en acciones que no se pueden deshacer, para evitar un "Sí" apretado por costumbre.
/// </summary>
public sealed class ConfirmacionEscrita : Form
{
    private readonly TextBox _texto = new();
    private readonly Button _btnAceptar = new();
    private readonly string _palabra;

    private ConfirmacionEscrita(string titulo, string mensaje, string palabra)
    {
        _palabra = palabra;

        Text = titulo;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 300);

        var raiz = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
        raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(raiz);

        raiz.Controls.Add(new Label { Text = mensaje, Dock = DockStyle.Fill, AutoSize = false }, 0, 0);
        raiz.Controls.Add(new Label
        {
            Text = $"Para continuar, escribí {palabra} (en mayúsculas) y apretá Aceptar:",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4)
        }, 0, 1);

        _texto.Dock = DockStyle.Fill;
        _texto.TextChanged += (_, _) => _btnAceptar.Enabled = EsCorrecto(_texto.Text, _palabra);
        raiz.Controls.Add(_texto, 0, 2);

        var botones = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
        var btnCancelar = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
        _btnAceptar.Text = "Aceptar";
        _btnAceptar.AutoSize = true;
        _btnAceptar.Enabled = false;
        _btnAceptar.DialogResult = DialogResult.OK;
        botones.Controls.Add(btnCancelar);
        botones.Controls.Add(_btnAceptar);
        raiz.Controls.Add(botones, 0, 3);

        CancelButton = btnCancelar;   // Esc = cancelar (no hay botón "por defecto": Enter no confirma nada)
    }

    /// <summary>true si el texto escrito es exactamente la palabra pedida (ignorando espacios en los bordes).</summary>
    public static bool EsCorrecto(string? escrito, string palabra) =>
        string.Equals(escrito?.Trim(), palabra, StringComparison.Ordinal);

    /// <summary>Muestra la ventana. Devuelve true solo si el usuario escribió la palabra y aceptó.</summary>
    public static bool Pedir(IWin32Window propietario, string titulo, string mensaje, string palabra)
    {
        using var dlg = new ConfirmacionEscrita(titulo, mensaje, palabra);
        return dlg.ShowDialog(propietario) == DialogResult.OK;
    }
}
