namespace FacturacionLote.UI;

/// <summary>Entrega cada mensaje de progreso al instante (Progress&lt;T&gt; los entrega con retraso).</summary>
internal sealed class ProgresoDirecto : IProgress<string>
{
    private readonly Action<string> _alReportar;

    public ProgresoDirecto(Action<string> alReportar) => _alReportar = alReportar;

    public void Report(string value) => _alReportar(value);
}