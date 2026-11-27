using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FacturacionLote.Domain;

/// <summary>Una fila de la grilla de envío de e-mails (una factura autorizada).</summary>
public sealed class FilaEmail : INotifyPropertyChanged
{
    public long FacturaId { get; init; }
    public string Cliente { get; init; } = "";
    public string Comprobante { get; init; } = "";
    public string PdfPath { get; init; } = "";
    /// <summary>Lo que se muestra en pantalla como "Email del cliente" (el que viene de Don Gestión).</summary>
    public string EmailCliente { get; init; } = "";
    /// <summary>E-mail ya validado y normalizado; vacío si no hay uno válido.</summary>
    public string EmailNormalizado { get; init; } = "";
    /// <summary>Casilla a la que realmente se enviará (en modo prueba, la de prueba).</summary>
    public string Destino { get; init; } = "";
    /// <summary>false = no se puede enviar (sin PDF o sin e-mail válido).</summary>
    public bool Habilitada { get; init; }

    private bool _enviar;
    public bool Enviar
    {
        get => _enviar;
        set { if (_enviar != value) { _enviar = value; Notificar(); } }
    }

    private string _estado = "";
    public string Estado
    {
        get => _estado;
        set { if (_estado != value) { _estado = value; Notificar(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notificar([CallerMemberName] string? nombre = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nombre));
}
