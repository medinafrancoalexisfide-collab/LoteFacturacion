using FacturacionLote.Infrastructure;
using FacturacionLote.UI;

namespace FacturacionLote;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Evita abrir dos copias a la vez (dos copias emitiendo podrían pisarse los números)
        using var mutex = new Mutex(true, @"Local\FacturacionLote.UnicaInstancia", out var esLaPrimera);
        if (!esLaPrimera)
        {
            MessageBox.Show("El programa ya está abierto.", "Facturación por lote",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            // Si algo falla de forma inesperada, se muestra el mensaje en vez de cerrar el programa sin avisar
            Application.ThreadException += (_, e) =>
            {
                Registro.Error("error inesperado", e.Exception);
                MessageBox.Show(e.Exception.Message, "Error inesperado", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Registro.LimpiarAntiguos();
            Registro.Escribir("Programa iniciado.");

            Application.Run(new MainForm());
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
