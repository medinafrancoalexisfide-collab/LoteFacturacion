using System.Globalization;
using Microsoft.Data.Sqlite;
using FacturacionLote.Config;
using FacturacionLote.Domain;

namespace FacturacionLote.Infrastructure;

/// <summary>Base SQLite local: guarda lo que HACE la extensión (lotes, facturas, preferencias por cliente).</summary>
public sealed class Database
{
    // Lista de columnas que se leen siempre en este orden (debe coincidir con el método Leer)
    private const string Columnas =
        "Id, Ambiente, ClienteId, Cliente, Cuit, CondIva, ImporteCentavos, PtoVta, CbteTipo, CbteNro, Cae, CaeVto, " +
        "PeriodoDesde, PeriodoHasta, FechaVtoPago, FechaEmision, Estado, Error, PdfPath, CreadoLocal, " +
        "ImporteNetoCentavos, ImporteIvaCentavos, AlicuotaBp";

    private readonly string _cs;

    public Database()
    {
        Directory.CreateDirectory(AppPaths.DatosDir);
        _cs = new SqliteConnectionStringBuilder { DataSource = AppPaths.DbPath }.ToString();
        Inicializar();
    }

    private SqliteConnection Abrir()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        return c;
    }

    /// <summary>Crea las tablas si todavía no existen (se ejecuta cada vez que arranca el programa).</summary>
    private void Inicializar()
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Lote (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Ambiente TEXT NOT NULL,
    FechaEmision TEXT NOT NULL,
    PeriodoDesde TEXT NOT NULL,
    PeriodoHasta TEXT NOT NULL,
    FechaVtoPago TEXT NOT NULL,
    Cantidad INTEGER NOT NULL,
    CreadoLocal TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Factura (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    LoteId INTEGER NULL,
    Ambiente TEXT NOT NULL,
    ClienteId INTEGER NOT NULL,
    Cliente TEXT NOT NULL,
    Cuit TEXT NOT NULL,
    CondIva INTEGER NOT NULL,
    ImporteCentavos INTEGER NOT NULL,
    PtoVta INTEGER NOT NULL,
    CbteTipo INTEGER NOT NULL,
    CbteNro INTEGER NOT NULL,
    Cae TEXT NULL,
    CaeVto TEXT NULL,
    PeriodoDesde TEXT NOT NULL,
    PeriodoHasta TEXT NOT NULL,
    FechaVtoPago TEXT NOT NULL,
    FechaEmision TEXT NOT NULL,
    Estado TEXT NOT NULL,
    Error TEXT NULL,
    PdfPath TEXT NULL,
    CreadoLocal TEXT NOT NULL,
    ActualizadoLocal TEXT NOT NULL,
    ImporteNetoCentavos INTEGER NOT NULL DEFAULT 0,
    ImporteIvaCentavos INTEGER NOT NULL DEFAULT 0,
    AlicuotaBp INTEGER NOT NULL DEFAULT 0
);
-- Impide dos facturas activas (Enviando/Autorizada) del mismo cliente y período en el mismo ambiente.
CREATE UNIQUE INDEX IF NOT EXISTS ux_factura_activa
    ON Factura (Ambiente, ClienteId, PeriodoDesde)
    WHERE Estado IN ('Enviando', 'Autorizada');
-- Preferencias por cliente: condición de IVA y si se factura por defecto.
CREATE TABLE IF NOT EXISTS ClienteConfig (
    ClienteId INTEGER PRIMARY KEY,
    CondIva INTEGER NULL,
    Facturar INTEGER NOT NULL DEFAULT 1
);
-- Registro de cada envío de factura por e-mail (para no perder el rastro y poder reintentar).
CREATE TABLE IF NOT EXISTS EnvioEmail (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    FacturaId INTEGER NOT NULL,
    EmailCliente TEXT NULL,
    Destino TEXT NOT NULL,
    Modo TEXT NOT NULL,
    Estado TEXT NOT NULL,
    Error TEXT NULL,
    CreadoLocal TEXT NOT NULL
);";
        cmd.ExecuteNonQuery();

        // Bases creadas con versiones anteriores: se agregan las columnas nuevas (neto, IVA y alícuota).
        if (AsegurarColumna(c, "Factura", "ImporteNetoCentavos", "INTEGER NOT NULL DEFAULT 0"))
        {
            AsegurarColumna(c, "Factura", "ImporteIvaCentavos", "INTEGER NOT NULL DEFAULT 0");
            AsegurarColumna(c, "Factura", "AlicuotaBp", "INTEGER NOT NULL DEFAULT 0");

            // Las facturas anteriores eran Factura C: el neto es igual al total y no hay IVA discriminado
            using var actualizar = c.CreateCommand();
            actualizar.CommandText = "UPDATE Factura SET ImporteNetoCentavos = ImporteCentavos WHERE ImporteNetoCentavos = 0;";
            actualizar.ExecuteNonQuery();
        }
    }

    // ------------------------------------------------------------------
    //  Lotes
    // ------------------------------------------------------------------

    public long CrearLote(string ambiente, DateOnly emision, DateOnly desde, DateOnly hasta, DateOnly vto, int cantidad)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO Lote (Ambiente, FechaEmision, PeriodoDesde, PeriodoHasta, FechaVtoPago, Cantidad, CreadoLocal)
VALUES ($amb, $em, $d, $h, $v, $cant, $now);";
        cmd.Parameters.AddWithValue("$amb", ambiente);
        cmd.Parameters.AddWithValue("$em", Iso(emision));
        cmd.Parameters.AddWithValue("$d", Iso(desde));
        cmd.Parameters.AddWithValue("$h", Iso(hasta));
        cmd.Parameters.AddWithValue("$v", Iso(vto));
        cmd.Parameters.AddWithValue("$cant", cantidad);
        cmd.Parameters.AddWithValue("$now", Ahora());
        cmd.ExecuteNonQuery();
        return UltimoId(c);
    }

    // ------------------------------------------------------------------
    //  Facturas
    // ------------------------------------------------------------------

    /// <summary>
    /// Reserva el número y deja la factura en estado "Enviando" ANTES de llamar a ARCA.
    /// Devuelve null si ya existe una factura activa de ese cliente y período (protección contra duplicados).
    /// </summary>
    public long? ReservarFactura(long loteId, string ambiente, int clienteId, string cliente, string cuit, int condIva,
        decimal importe, decimal neto, decimal iva, decimal alicuotaPct, int ptoVta, int cbteTipo, long numero,
        DateOnly desde, DateOnly hasta, DateOnly vtoPago, DateOnly emision)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO Factura
(LoteId, Ambiente, ClienteId, Cliente, Cuit, CondIva, ImporteCentavos, PtoVta, CbteTipo, CbteNro,
 PeriodoDesde, PeriodoHasta, FechaVtoPago, FechaEmision, Estado, CreadoLocal, ActualizadoLocal,
 ImporteNetoCentavos, ImporteIvaCentavos, AlicuotaBp)
VALUES ($lote, $amb, $cid, $cli, $cuit, $iva, $imp, $pv, $tipo, $nro, $d, $h, $v, $em, 'Enviando', $now, $now,
 $neto, $ivaimp, $alic);";
        cmd.Parameters.AddWithValue("$lote", loteId);
        cmd.Parameters.AddWithValue("$amb", ambiente);
        cmd.Parameters.AddWithValue("$cid", clienteId);
        cmd.Parameters.AddWithValue("$cli", cliente);
        cmd.Parameters.AddWithValue("$cuit", cuit);
        cmd.Parameters.AddWithValue("$iva", condIva);
        cmd.Parameters.AddWithValue("$imp", ACentavos(importe));
        cmd.Parameters.AddWithValue("$neto", ACentavos(neto));
        cmd.Parameters.AddWithValue("$ivaimp", ACentavos(iva));
        cmd.Parameters.AddWithValue("$alic", (long)Math.Round(alicuotaPct * 100m, MidpointRounding.AwayFromZero));
        cmd.Parameters.AddWithValue("$pv", ptoVta);
        cmd.Parameters.AddWithValue("$tipo", cbteTipo);
        cmd.Parameters.AddWithValue("$nro", numero);
        cmd.Parameters.AddWithValue("$d", Iso(desde));
        cmd.Parameters.AddWithValue("$h", Iso(hasta));
        cmd.Parameters.AddWithValue("$v", Iso(vtoPago));
        cmd.Parameters.AddWithValue("$em", Iso(emision));
        cmd.Parameters.AddWithValue("$now", Ahora());
        try
        {
            cmd.ExecuteNonQuery();
            return UltimoId(c);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // 19 = violación de restricción UNIQUE
        {
            return null;
        }
    }

    public void MarcarAutorizada(long id, string cae, DateOnly? caeVto)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE Factura SET Estado='Autorizada', Cae=$cae, CaeVto=$vto, Error=NULL, ActualizadoLocal=$now WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$cae", cae);
        cmd.Parameters.AddWithValue("$vto", caeVto.HasValue ? Iso(caeVto.Value) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$now", Ahora());
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void MarcarError(long id, string mensaje)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE Factura SET Estado='Error', Error=$err, ActualizadoLocal=$now WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$err", mensaje);
        cmd.Parameters.AddWithValue("$now", Ahora());
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void GuardarPdf(long id, string ruta)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE Factura SET PdfPath=$p, ActualizadoLocal=$now WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$p", ruta);
        cmd.Parameters.AddWithValue("$now", Ahora());
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public FacturaRow? Obtener(long id)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas} FROM Factura WHERE Id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Leer(r) : null;
    }

    public List<FacturaRow> ListarEnviando(string ambiente)
        => Consultar($"SELECT {Columnas} FROM Factura WHERE Ambiente=$amb AND Estado='Enviando' ORDER BY Id;", ambiente);

    public List<FacturaRow> Historial(string ambiente, int limite = 500)
        => Consultar($"SELECT {Columnas} FROM Factura WHERE Ambiente=$amb ORDER BY Id DESC LIMIT {limite};", ambiente);

    /// <summary>Facturas activas (Enviando/Autorizada) de un período, por cliente: sirve para marcar "Ya facturada".</summary>
    public Dictionary<int, FacturaRow> ActivasDelPeriodo(string ambiente, DateOnly desde)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT {Columnas} FROM Factura WHERE Ambiente=$amb AND PeriodoDesde=$d AND Estado IN ('Enviando','Autorizada');";
        cmd.Parameters.AddWithValue("$amb", ambiente);
        cmd.Parameters.AddWithValue("$d", Iso(desde));
        var dic = new Dictionary<int, FacturaRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var f = Leer(r);
            dic[f.ClienteId] = f;
        }
        return dic;
    }

    // ------------------------------------------------------------------
    //  Preferencias por cliente (condición de IVA y selección por defecto)
    // ------------------------------------------------------------------

    public Dictionary<int, int> CondicionesIva()
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ClienteId, CondIva FROM ClienteConfig WHERE CondIva IS NOT NULL;";
        var dic = new Dictionary<int, int>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) dic[r.GetInt32(0)] = r.GetInt32(1);
        return dic;
    }

    public void GuardarCondIva(int clienteId, int condIva)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO ClienteConfig (ClienteId, CondIva) VALUES ($c, $i)
ON CONFLICT(ClienteId) DO UPDATE SET CondIva = excluded.CondIva;";
        cmd.Parameters.AddWithValue("$c", clienteId);
        cmd.Parameters.AddWithValue("$i", condIva);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Clientes con selección guardada: true = se factura por defecto, false = destildado.
    /// Los clientes que no figuran acá son nuevos y se tildan por defecto.</summary>
    public Dictionary<int, bool> Selecciones()
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ClienteId, Facturar FROM ClienteConfig;";
        var dic = new Dictionary<int, bool>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) dic[r.GetInt32(0)] = r.GetInt32(1) != 0;
        return dic;
    }

    public void GuardarSeleccion(int clienteId, bool facturar)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO ClienteConfig (ClienteId, Facturar) VALUES ($c, $f)
ON CONFLICT(ClienteId) DO UPDATE SET Facturar = excluded.Facturar;";
        cmd.Parameters.AddWithValue("$c", clienteId);
        cmd.Parameters.AddWithValue("$f", facturar ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    // ------------------------------------------------------------------
    //  Envíos de e-mail
    // ------------------------------------------------------------------

    /// <summary>Guarda el resultado de un envío (modo "PRUEBA" o "REAL"; estado "Enviado" o "Error").</summary>
    public void RegistrarEnvioEmail(long facturaId, string? emailCliente, string destino, string modo, bool ok, string? error)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"INSERT INTO EnvioEmail (FacturaId, EmailCliente, Destino, Modo, Estado, Error, CreadoLocal)
VALUES ($f, $ec, $d, $m, $e, $err, $now);";
        cmd.Parameters.AddWithValue("$f", facturaId);
        cmd.Parameters.AddWithValue("$ec", (object?)emailCliente ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$d", destino);
        cmd.Parameters.AddWithValue("$m", modo);
        cmd.Parameters.AddWithValue("$e", ok ? "Enviado" : "Error");
        cmd.Parameters.AddWithValue("$err", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", Ahora());
        cmd.ExecuteNonQuery();
    }

    /// <summary>Último envío registrado de cada factura (clave = id de la factura).</summary>
    public Dictionary<long, EnvioEmailRow> UltimosEnviosEmail()
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT FacturaId, Destino, Modo, Estado, Error, CreadoLocal FROM EnvioEmail
WHERE Id IN (SELECT MAX(Id) FROM EnvioEmail GROUP BY FacturaId);";
        var dic = new Dictionary<long, EnvioEmailRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            dic[r.GetInt64(0)] = new EnvioEmailRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5));
        return dic;
    }

    // ------------------------------------------------------------------
    //  Internos
    // ------------------------------------------------------------------

    /// <summary>Agrega una columna si la tabla todavía no la tiene. Devuelve true si la agregó.</summary>
    private static bool AsegurarColumna(SqliteConnection c, string tabla, string columna, string definicion)
    {
        using (var consulta = c.CreateCommand())
        {
            consulta.CommandText = $"PRAGMA table_info({tabla});";
            using var r = consulta.ExecuteReader();
            while (r.Read())
                if (string.Equals(r.GetString(1), columna, StringComparison.OrdinalIgnoreCase))
                    return false;
        }

        using var alterar = c.CreateCommand();
        alterar.CommandText = $"ALTER TABLE {tabla} ADD COLUMN {columna} {definicion};";
        alterar.ExecuteNonQuery();
        return true;
    }

    private List<FacturaRow> Consultar(string sql, string ambiente)
    {
        using var c = Abrir();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$amb", ambiente);
        var lista = new List<FacturaRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) lista.Add(Leer(r));
        return lista;
    }

    // Convierte una fila de la base en un objeto FacturaRow (mismo orden que "Columnas")
    private static FacturaRow Leer(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Ambiente = r.GetString(1),
        ClienteId = r.GetInt32(2),
        Cliente = r.GetString(3),
        Cuit = r.GetString(4),
        CondIva = r.GetInt32(5),
        Importe = r.GetInt64(6) / 100m,
        PtoVta = r.GetInt32(7),
        CbteTipo = r.GetInt32(8),
        Numero = r.GetInt64(9),
        Cae = r.IsDBNull(10) ? null : r.GetString(10),
        CaeVto = r.IsDBNull(11) ? null : ParseIso(r.GetString(11)),
        PeriodoDesde = ParseIso(r.GetString(12)),
        PeriodoHasta = ParseIso(r.GetString(13)),
        FechaVtoPago = ParseIso(r.GetString(14)),
        FechaEmision = ParseIso(r.GetString(15)),
        Estado = r.GetString(16),
        Error = r.IsDBNull(17) ? null : r.GetString(17),
        PdfPath = r.IsDBNull(18) ? null : r.GetString(18),
        CreadoLocal = r.GetString(19),
        ImporteNeto = r.GetInt64(20) / 100m,
        ImporteIva = r.GetInt64(21) / 100m,
        Alicuota = r.GetInt64(22) / 100m
    };

    private static long UltimoId(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid();";
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static long ACentavos(decimal importe) => (long)Math.Round(importe * 100m, MidpointRounding.AwayFromZero);
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateOnly ParseIso(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Ahora() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}