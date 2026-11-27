using System.Text;
using FacturacionLote.Config;

namespace FacturacionLote.Infrastructure;

/// <summary>
/// Registro de actividad en archivo (un archivo por día, en la carpeta de datos del programa).
/// Todo lo que se escribe pasa antes por <see cref="Redactor"/>, así que el archivo no contiene
/// e-mails, CUIT completos ni tokens. Escribir en el registro NUNCA interrumpe el programa.
/// </summary>
public static class Registro
{
    private static readonly object Candado = new();
    private const int DiasDeConservacion = 30;

    public static string Carpeta => Path.Combine(AppPaths.DatosDir, "logs");

    public static void Escribir(string mensaje)
    {
        try
        {
            lock (Candado)
            {
                Directory.CreateDirectory(Carpeta);
                var archivo = Path.Combine(Carpeta, $"{DateTime.Now:yyyy-MM-dd}.log");
                var linea = $"[{DateTime.Now:HH:mm:ss}] {Redactor.Limpiar(mensaje)}{Environment.NewLine}";
                File.AppendAllText(archivo, linea, new UTF8Encoding(false));
            }
        }
        catch (Exception)
        {
            // Si no se puede escribir (disco lleno, permisos), el programa sigue funcionando.
        }
    }

    /// <summary>Registra una excepción: el tipo y el mensaje, sin la traza completa.</summary>
    public static void Error(string contexto, Exception ex) =>
        Escribir($"ERROR en {contexto}: {ex.GetType().Name}: {ex.Message}");

    /// <summary>Borra los archivos de registro con más de 30 días.</summary>
    public static void LimpiarAntiguos()
    {
        try
        {
            if (!Directory.Exists(Carpeta)) return;
            var limite = DateTime.Now.AddDays(-DiasDeConservacion);
            foreach (var archivo in Directory.EnumerateFiles(Carpeta, "*.log"))
            {
                if (File.GetLastWriteTime(archivo) < limite) File.Delete(archivo);
            }
        }
        catch (Exception)
        {
            // Limpiar es opcional: ignorar cualquier problema.
        }
    }
}
