using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FacturacionLote.Domain;

/// <summary>Para filtrar la grilla: pendientes, ya facturadas o con problemas.</summary>
public enum GrupoFila
{
    Pendiente,
    Facturada,
    Problema
}

/// <summary>Una fila de la grilla de facturación (un cliente = una factura).</summary>
public sealed class FilaLote : INotifyPropertyChanged
{
    public const string EstadoSinCondicion = "Definir condición de IVA";
    public const string EstadoSugerida = "Pendiente · confirmar condición de IVA";

    public int ClienteId { get; init; }
    public string Cliente { get; init; } = "";
    /// <summary>CUIT validado (11 dígitos) o vacío si no se encontró uno válido.</summary>
    public string Cuit { get; init; } = "";
    public int Ventas { get; init; }
    public decimal Neto { get; init; }
    public decimal Iva { get; init; }
    /// <summary>Total de la factura (con IVA).</summary>
    public decimal Importe { get; init; }
    /// <summary>true si el emisor es Responsable Inscripto (emite A y B); false si es Monotributista (emite C).</summary>
    public bool EmisorInscripto { get; init; }
    /// <summary>true = no se puede facturar (ya facturada, sin CUIT, importe no válido, IVA a revisar...).</summary>
    public bool Bloqueada { get; init; }
    /// <summary>Selección guardada de este cliente (se usa cuando la fila pasa a estar habilitada).</summary>
    public bool MarcadoPorDefecto { get; init; }

    private int _condIva;
    /// <summary>Valor inicial de la condición de IVA (no dispara avisos ni guardados).</summary>
    public int CondIvaInicial { init => _condIva = value; }

    /// <summary>Condición de IVA del cliente (código de ARCA). 0 = sin definir.</summary>
    public int CondIva
    {
        get => _condIva;
        set
        {
            if (_condIva == value) return;
            var eraIndefinida = _condIva == 0;
            _condIva = value;
            Notificar();
            Notificar(nameof(TipoFactura));
            Notificar(nameof(Habilitada));
            AjustarEstado();
            if (eraIndefinida && value != 0 && !Bloqueada && MarcadoPorDefecto) Facturar = true;
        }
    }

    /// <summary>Se puede facturar: no está bloqueada y tiene la condición de IVA definida.</summary>
    public bool Habilitada => !Bloqueada && _condIva != 0;

    /// <summary>Letra del comprobante: A o B si el emisor es Responsable Inscripto (según el cliente), C si es Monotributista.</summary>
    public string TipoFactura =>
        _condIva == 0 ? "" : (EmisorInscripto ? TiposComprobante.LetraPorCondicion(_condIva) : "C");

    private bool _facturar;
    public bool Facturar
    {
        get => _facturar;
        set { if (_facturar != value) { _facturar = value; Notificar(); } }
    }

    private string _estado = "";
    public string Estado
    {
        get => _estado;
        set { if (_estado != value) { _estado = value; Notificar(); } }
    }

    public GrupoFila Grupo
    {
        get
        {
            if (Estado.StartsWith("Ya facturada") || Estado.StartsWith("Autorizada")) return GrupoFila.Facturada;
            if (Bloqueada || Estado.StartsWith("Rechazada") || Estado.StartsWith("Error") || Estado.StartsWith("INCIERTA"))
                return GrupoFila.Problema;
            return GrupoFila.Pendiente;
        }
    }

    // Mantiene coherente el texto de estado cuando se define o se cambia la condición de IVA
    private void AjustarEstado()
    {
        if (Bloqueada) return;
        if (_condIva == 0)
        {
            if (Estado.StartsWith("Pendiente")) Estado = EstadoSinCondicion;
        }
        else if (Estado == EstadoSinCondicion || Estado == EstadoSugerida)
        {
            Estado = "Pendiente";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // [CallerMemberName] completa solo el nombre de la propiedad que llamó
    private void Notificar([CallerMemberName] string? nombre = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nombre));
}

/// <summary>Resultado de armar la lista previa: las filas y un resumen para el registro.</summary>
public sealed record ResultadoPreparacion(List<FilaLote> Filas, string Resumen);
