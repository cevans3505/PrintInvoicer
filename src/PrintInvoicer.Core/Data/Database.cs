using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;
using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;

namespace PrintInvoicer.Core.Data;

/// <summary>
/// SQLite storage. Money and grams are stored as invariant-culture text so decimals round-trip exactly.
/// Jobs are small, so a job is saved by rewriting its parts, plates and slots inside one transaction.
/// </summary>
public class Database
{
    private readonly string _connectionString;

    public string FilePath { get; }

    public Database(string filePath)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = filePath, ForeignKeys = true }.ToString();
        EnsureCreated();
        FreezeFinalizedJobs();
    }

    /// <summary>
    /// Invoices finalized before prices were frozen get today's prices as their snapshot, which are the prices they
    /// were last shown with, so from now on they stay as they are. Drafts have no frozen prices: a snapshot left on one
    /// (reverted to draft by a version that did not know about snapshots) is dropped, so it is priced at today's rates.
    /// </summary>
    private void FreezeFinalizedJobs()
    {
        using (var c = Open())
        {
            Exec(c, "UPDATE jobs SET price_snapshot = NULL, finalized_total = NULL WHERE finalized = 0 AND (price_snapshot IS NOT NULL OR finalized_total IS NOT NULL)");
            if (Scalar<long>(c, "SELECT COUNT(*) FROM jobs WHERE finalized = 1 AND price_snapshot IS NULL") == 0) return;
        }

        var book = LoadPriceBook();
        foreach (var job in GetJobs().Where(j => j.Finalized && j.PriceSnapshot is null))
        {
            PriceSnapshot.Freeze(job, book);
            using var c = Open();
            Exec(c, "UPDATE jobs SET price_snapshot = $s, finalized_total = $t WHERE id = $id",
                ("$id", job.Id), ("$s", job.PriceSnapshot), ("$t", Str(job.FinalizedTotal!.Value)));
        }
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PrintInvoicer", "printinvoicer.db");

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    private void EnsureCreated()
    {
        using var c = Open();
        Exec(c, """
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS filaments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                vendor TEXT NOT NULL, material TEXT NOT NULL, color_name TEXT NOT NULL, color_hex TEXT NOT NULL,
                spool_price TEXT NOT NULL, spool_grams TEXT NOT NULL, active INTEGER NOT NULL, notes TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS filament_mappings (key TEXT PRIMARY KEY, filament_id INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS jobs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL, customer TEXT NOT NULL, invoice_number TEXT, created_date TEXT NOT NULL,
                invoice_date TEXT, modeling_hours TEXT NOT NULL, post_processing INTEGER NOT NULL,
                paid INTEGER NOT NULL, paid_date TEXT, notes TEXT NOT NULL,
                printed INTEGER NOT NULL DEFAULT 0, printed_date TEXT,
                finalized INTEGER NOT NULL DEFAULT 0, finalized_date TEXT);
            CREATE TABLE IF NOT EXISTS parts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                job_id INTEGER NOT NULL REFERENCES jobs(id) ON DELETE CASCADE,
                sort INTEGER NOT NULL, name TEXT NOT NULL, charge_slicing INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS plates (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                part_id INTEGER NOT NULL REFERENCES parts(id) ON DELETE CASCADE,
                sort INTEGER NOT NULL, source_file TEXT NOT NULL, plate_index INTEGER NOT NULL, copies INTEGER NOT NULL,
                parts_per_plate INTEGER NOT NULL, sliced_seconds INTEGER NOT NULL, override_seconds INTEGER, thumbnail BLOB,
                has_failures INTEGER NOT NULL DEFAULT 0, failed_count INTEGER NOT NULL DEFAULT 0, failure_note TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS plate_filaments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                plate_id INTEGER NOT NULL REFERENCES plates(id) ON DELETE CASCADE,
                slot_id INTEGER NOT NULL, slicer_type TEXT NOT NULL, slicer_color_hex TEXT NOT NULL,
                slicer_vendor TEXT NOT NULL, slicer_profile TEXT NOT NULL, sliced_grams TEXT NOT NULL,
                override_grams TEXT, filament_id INTEGER, customer_supplied INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS printers (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL, technology INTEGER NOT NULL, machine_rate TEXT NOT NULL, watts TEXT NOT NULL,
                waste TEXT NOT NULL, markup TEXT NOT NULL, active INTEGER NOT NULL, notes TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS resins (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                vendor TEXT NOT NULL, name TEXT NOT NULL, color_name TEXT NOT NULL, color_hex TEXT NOT NULL,
                bottle_price TEXT NOT NULL, bottle_size TEXT NOT NULL, bottle_unit INTEGER NOT NULL, density TEXT NOT NULL,
                active INTEGER NOT NULL, notes TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS extra_costs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL, cost TEXT NOT NULL, basis INTEGER NOT NULL, applies_to INTEGER,
                active INTEGER NOT NULL, notes TEXT NOT NULL);
            """);

        bool brandNew = Scalar<long>(c, "SELECT COUNT(*) FROM jobs") == 0 && Scalar<long>(c, "SELECT COUNT(*) FROM settings") == 0
                        && Scalar<long>(c, "SELECT COUNT(*) FROM filaments") == 0;

        // Columns added after the first release; older databases get them here.
        AddColumnIfMissing(c, "jobs", "printed", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(c, "jobs", "printed_date", "TEXT");
        AddColumnIfMissing(c, "jobs", "finalized", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(c, "jobs", "finalized_date", "TEXT");
        AddColumnIfMissing(c, "jobs", "excluded_extra_costs", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(c, "jobs", "price_snapshot", "TEXT");
        AddColumnIfMissing(c, "jobs", "finalized_total", "TEXT");
        AddColumnIfMissing(c, "plates", "has_failures", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(c, "plates", "failed_count", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(c, "plates", "failure_note", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(c, "plates", "technology", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(c, "plates", "printer_id", "INTEGER");
        AddColumnIfMissing(c, "plates", "layer_count", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(c, "plates", "layer_height", "TEXT NOT NULL DEFAULT '0'");
        AddColumnIfMissing(c, "plates", "resin_profile", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(c, "plates", "resin_sliced_ml", "TEXT NOT NULL DEFAULT '0'");
        AddColumnIfMissing(c, "plates", "resin_override_ml", "TEXT");
        AddColumnIfMissing(c, "plates", "resin_id", "INTEGER");
        AddColumnIfMissing(c, "plates", "resin_customer_supplied", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(c, "plates", "parts_confirmed", "INTEGER NOT NULL DEFAULT 1");

        MovePrintingRatesToPrinter(c);

        // Before 3.5 the invoice counter had no year; treat an existing one as set this year, so a counter moved
        // ahead this year still counts until the year rolls over.
        if (Scalar<long>(c, "SELECT COUNT(*) FROM settings WHERE key = 'NextInvoiceNumber'") > 0)
            Exec(c, "INSERT INTO settings(key, value) VALUES ('NextInvoiceYear', $y) ON CONFLICT(key) DO NOTHING",
                ("$y", DateTime.Today.Year.ToString(CultureInfo.InvariantCulture)));

        if (Scalar<long>(c, "SELECT COUNT(*) FROM filaments") == 0)
        {
            foreach (var f in SeedData.Filaments()) SaveFilament(f, c);
        }
        if (brandNew)
        {
            foreach (var r in SeedData.Resins()) SaveResin(r, c);
        }
    }

    /// <summary>
    /// Before printers existed, machine rate, power, waste and markup were shop settings and every plate was FDM.
    /// An existing database gets one FDM printer with those rates, and its plates are assigned to it.
    /// </summary>
    private static void MovePrintingRatesToPrinter(SqliteConnection c)
    {
        if (Scalar<long>(c, "SELECT COUNT(*) FROM printers") > 0) return;
        bool hasHistory = Scalar<long>(c, "SELECT COUNT(*) FROM jobs") > 0 ||
                          Scalar<long>(c, "SELECT COUNT(*) FROM settings WHERE key = 'MachineRatePerHour'") > 0;
        if (!hasHistory) return;

        var legacy = new Dictionary<string, decimal>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT key, value FROM settings WHERE key IN ('MachineRatePerHour', 'PrinterWatts', 'WasteAllowance', 'MaterialMarkup')";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (decimal.TryParse(r.GetString(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) legacy[r.GetString(0)] = v;
        }

        var p = Printer.NewDefault(PrintTechnology.Fdm);
        p.MachineRatePerHour = legacy.GetValueOrDefault("MachineRatePerHour", p.MachineRatePerHour);
        p.Watts = legacy.GetValueOrDefault("PrinterWatts", p.Watts);
        p.WasteAllowance = legacy.GetValueOrDefault("WasteAllowance", p.WasteAllowance);
        p.MaterialMarkup = legacy.GetValueOrDefault("MaterialMarkup", p.MaterialMarkup);
        SavePrinter(p, c);
        Exec(c, "UPDATE plates SET printer_id = $id WHERE printer_id IS NULL AND technology = 0", ("$id", p.Id));
    }

    private static void AddColumnIfMissing(SqliteConnection c, string table, string column, string definition)
    {
        if (Scalar<long>(c, $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $col", ("$col", column)) == 0)
            Exec(c, $"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }

    // ---------------- Settings ----------------

    private static readonly PropertyInfo[] SettingProps = typeof(AppSettings).GetProperties()
        .Where(p => p.CanRead && p.CanWrite).ToArray();

    public AppSettings LoadSettings()
    {
        var s = new AppSettings();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM settings";
        using var r = cmd.ExecuteReader();
        var values = new Dictionary<string, string>();
        while (r.Read()) values[r.GetString(0)] = r.GetString(1);

        foreach (var p in SettingProps)
        {
            if (!values.TryGetValue(p.Name, out var v)) continue;
            object? parsed = p.PropertyType == typeof(decimal) ? decimal.Parse(v, CultureInfo.InvariantCulture)
                : p.PropertyType == typeof(int) ? int.Parse(v, CultureInfo.InvariantCulture)
                : p.PropertyType == typeof(string) ? v
                : p.PropertyType == typeof(bool) ? bool.Parse(v)
                : null;
            if (parsed != null) p.SetValue(s, parsed);
        }
        return s;
    }

    public void SaveSettings(AppSettings s)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var p in SettingProps)
        {
            var value = Convert.ToString(p.GetValue(s), CultureInfo.InvariantCulture) ?? "";
            Exec(c, "INSERT INTO settings(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                ("$k", p.Name), ("$v", value));
        }
        tx.Commit();
    }

    /// <summary>
    /// Hands out the next invoice number and advances the counter. Numbers already on a job (typed in by hand, or left
    /// behind if the counter was ever set back) are skipped, so this never returns a duplicate.
    /// </summary>
    public string AllocateInvoiceNumber(DateTime date)
    {
        var (number, seq) = NextInvoiceNumber(date);
        SetNextInvoiceNumber(seq + 1, date.Year);
        return number;
    }

    /// <summary>
    /// The number a new invoice would get, without using it up: one past the highest saved number with this
    /// year's prefix, or the Settings counter if that is higher (so the counter can still be used to jump ahead).
    /// With {yyyy} in the prefix the counter belongs to the year it was set in, so numbering restarts at 001 each
    /// January; without it, numbering simply continues.
    /// </summary>
    public string PeekNextInvoiceNumber(DateTime date) => NextInvoiceNumber(date).Number;

    private (string Number, int Sequence) NextInvoiceNumber(DateTime date)
    {
        var s = LoadSettings();
        string prefix = s.InvoicePrefixFor(date);

        int highest = 0;
        using (var c = Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT invoice_number FROM jobs WHERE invoice_number IS NOT NULL";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string n = r.GetString(0).Trim();
                if (n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(n[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int used))
                    highest = Math.Max(highest, used);
            }
        }

        bool counterApplies = !s.InvoicePrefix.Contains("{yyyy}") || s.NextInvoiceYear == date.Year;
        int seq = Math.Max(counterApplies ? Math.Max(s.NextInvoiceNumber, 1) : 1, highest + 1);
        string number = s.FormatInvoiceNumber(seq, date);
        while (InvoiceNumberInUse(number, excludeJobId: null))
            number = s.FormatInvoiceNumber(++seq, date);
        return (number, seq);
    }

    /// <param name="year">The year the counter is for; see <see cref="AppSettings.NextInvoiceYear"/>.</param>
    public void SetNextInvoiceNumber(int next, int year)
    {
        using var c = Open();
        foreach (var (key, value) in new[] { (nameof(AppSettings.NextInvoiceNumber), next), (nameof(AppSettings.NextInvoiceYear), year) })
            Exec(c, "INSERT INTO settings(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
                ("$k", key), ("$v", value.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>True if another job already has this invoice number (ignoring case and surrounding spaces).</summary>
    public bool InvoiceNumberInUse(string number, long? excludeJobId)
    {
        using var c = Open();
        return Scalar<long>(c, "SELECT COUNT(*) FROM jobs WHERE TRIM(invoice_number) = TRIM($n) COLLATE NOCASE AND id <> $id",
            ("$n", number), ("$id", excludeJobId ?? 0)) > 0;
    }

    // ---------------- Filaments ----------------

    public List<Filament> GetFilaments()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, vendor, material, color_name, color_hex, spool_price, spool_grams, active, notes FROM filaments ORDER BY vendor, material, color_name";
        using var r = cmd.ExecuteReader();
        var list = new List<Filament>();
        while (r.Read())
        {
            list.Add(new Filament
            {
                Id = r.GetInt64(0),
                Vendor = r.GetString(1),
                Material = r.GetString(2),
                ColorName = r.GetString(3),
                ColorHex = r.GetString(4),
                SpoolPrice = Dec(r.GetString(5)),
                SpoolGrams = Dec(r.GetString(6)),
                Active = r.GetInt64(7) != 0,
                Notes = r.GetString(8),
            });
        }
        return list;
    }

    public void SaveFilament(Filament f)
    {
        using var c = Open();
        SaveFilament(f, c);
    }

    private static void SaveFilament(Filament f, SqliteConnection c)
    {
        var args = new (string, object?)[]
        {
            ("$id", f.Id), ("$vendor", f.Vendor), ("$material", f.Material), ("$color", f.ColorName),
            ("$hex", f.ColorHex), ("$price", Str(f.SpoolPrice)), ("$grams", Str(f.SpoolGrams)),
            ("$active", f.Active ? 1 : 0), ("$notes", f.Notes),
        };
        if (f.Id == 0)
        {
            f.Id = Scalar<long>(c, """
                INSERT INTO filaments(vendor, material, color_name, color_hex, spool_price, spool_grams, active, notes)
                VALUES ($vendor, $material, $color, $hex, $price, $grams, $active, $notes) RETURNING id
                """, args);
        }
        else
        {
            Exec(c, """
                UPDATE filaments SET vendor=$vendor, material=$material, color_name=$color, color_hex=$hex,
                    spool_price=$price, spool_grams=$grams, active=$active, notes=$notes WHERE id=$id
                """, args);
        }
    }

    /// <summary>Deletes a filament, or deactivates it if a saved job still uses it. Returns true if it was deleted.</summary>
    public bool DeleteFilament(long id)
    {
        using var c = Open();
        if (Scalar<long>(c, "SELECT COUNT(*) FROM plate_filaments WHERE filament_id = $id", ("$id", id)) > 0)
        {
            Exec(c, "UPDATE filaments SET active = 0 WHERE id = $id", ("$id", id));
            return false;
        }
        // Resin choices share the table (keyed RESIN|...) and resin ids overlap filament ids, so leave those alone.
        Exec(c, "DELETE FROM filament_mappings WHERE filament_id = $id AND key NOT LIKE 'RESIN|%'", ("$id", id));
        Exec(c, "DELETE FROM filaments WHERE id = $id", ("$id", id));
        return true;
    }

    public Dictionary<string, long> GetMappings()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT key, filament_id FROM filament_mappings";
        using var r = cmd.ExecuteReader();
        var d = new Dictionary<string, long>();
        while (r.Read()) d[r.GetString(0)] = r.GetInt64(1);
        return d;
    }

    public void SaveMapping(string key, long filamentId)
    {
        using var c = Open();
        Exec(c, "INSERT INTO filament_mappings(key, filament_id) VALUES ($k, $f) ON CONFLICT(key) DO UPDATE SET filament_id = excluded.filament_id",
            ("$k", key), ("$f", filamentId));
    }

    // ---------------- Printers ----------------

    public List<Printer> GetPrinters()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, name, technology, machine_rate, watts, waste, markup, active, notes FROM printers ORDER BY technology, name, id";
        using var r = cmd.ExecuteReader();
        var list = new List<Printer>();
        while (r.Read())
        {
            list.Add(new Printer
            {
                Id = r.GetInt64(0),
                Name = r.GetString(1),
                Technology = (PrintTechnology)r.GetInt32(2),
                MachineRatePerHour = Dec(r.GetString(3)),
                Watts = Dec(r.GetString(4)),
                WasteAllowance = Dec(r.GetString(5)),
                MaterialMarkup = Dec(r.GetString(6)),
                Active = r.GetInt64(7) != 0,
                Notes = r.GetString(8),
            });
        }
        return list;
    }

    public void SavePrinter(Printer p)
    {
        using var c = Open();
        SavePrinter(p, c);
    }

    private static void SavePrinter(Printer p, SqliteConnection c)
    {
        var args = new (string, object?)[]
        {
            ("$id", p.Id), ("$name", p.Name), ("$tech", (int)p.Technology), ("$rate", Str(p.MachineRatePerHour)), ("$watts", Str(p.Watts)),
            ("$waste", Str(p.WasteAllowance)), ("$markup", Str(p.MaterialMarkup)), ("$active", p.Active ? 1 : 0), ("$notes", p.Notes),
        };
        if (p.Id == 0)
            p.Id = Scalar<long>(c, """
                INSERT INTO printers(name, technology, machine_rate, watts, waste, markup, active, notes)
                VALUES ($name, $tech, $rate, $watts, $waste, $markup, $active, $notes) RETURNING id
                """, args);
        else
            Exec(c, """
                UPDATE printers SET name=$name, technology=$tech, machine_rate=$rate, watts=$watts, waste=$waste, markup=$markup,
                    active=$active, notes=$notes WHERE id=$id
                """, args);
    }

    /// <summary>Deletes a printer, or deactivates it if a saved plate uses it. Returns true if it was deleted.</summary>
    public bool DeletePrinter(long id)
    {
        using var c = Open();
        if (Scalar<long>(c, "SELECT COUNT(*) FROM plates WHERE printer_id = $id", ("$id", id)) > 0)
        {
            Exec(c, "UPDATE printers SET active = 0 WHERE id = $id", ("$id", id));
            return false;
        }
        Exec(c, "DELETE FROM printers WHERE id = $id", ("$id", id));
        return true;
    }

    // ---------------- Resins ----------------

    public List<Resin> GetResins()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, vendor, name, color_name, color_hex, bottle_price, bottle_size, bottle_unit, density, active, notes FROM resins ORDER BY vendor, name, color_name";
        using var r = cmd.ExecuteReader();
        var list = new List<Resin>();
        while (r.Read())
        {
            list.Add(new Resin
            {
                Id = r.GetInt64(0),
                Vendor = r.GetString(1),
                Name = r.GetString(2),
                ColorName = r.GetString(3),
                ColorHex = r.GetString(4),
                BottlePrice = Dec(r.GetString(5)),
                BottleSize = Dec(r.GetString(6)),
                BottleUnit = (BottleUnit)r.GetInt32(7),
                Density = Dec(r.GetString(8)),
                Active = r.GetInt64(9) != 0,
                Notes = r.GetString(10),
            });
        }
        return list;
    }

    public void SaveResin(Resin r)
    {
        using var c = Open();
        SaveResin(r, c);
    }

    private static void SaveResin(Resin r, SqliteConnection c)
    {
        var args = new (string, object?)[]
        {
            ("$id", r.Id), ("$vendor", r.Vendor), ("$name", r.Name), ("$color", r.ColorName), ("$hex", r.ColorHex),
            ("$price", Str(r.BottlePrice)), ("$size", Str(r.BottleSize)), ("$unit", (int)r.BottleUnit), ("$density", Str(r.Density)),
            ("$active", r.Active ? 1 : 0), ("$notes", r.Notes),
        };
        if (r.Id == 0)
            r.Id = Scalar<long>(c, """
                INSERT INTO resins(vendor, name, color_name, color_hex, bottle_price, bottle_size, bottle_unit, density, active, notes)
                VALUES ($vendor, $name, $color, $hex, $price, $size, $unit, $density, $active, $notes) RETURNING id
                """, args);
        else
            Exec(c, """
                UPDATE resins SET vendor=$vendor, name=$name, color_name=$color, color_hex=$hex, bottle_price=$price, bottle_size=$size,
                    bottle_unit=$unit, density=$density, active=$active, notes=$notes WHERE id=$id
                """, args);
    }

    /// <summary>Deletes a resin, or deactivates it if a saved plate uses it. Returns true if it was deleted.</summary>
    public bool DeleteResin(long id)
    {
        using var c = Open();
        if (Scalar<long>(c, "SELECT COUNT(*) FROM plates WHERE resin_id = $id", ("$id", id)) > 0)
        {
            Exec(c, "UPDATE resins SET active = 0 WHERE id = $id", ("$id", id));
            return false;
        }
        Exec(c, "DELETE FROM filament_mappings WHERE key LIKE 'RESIN|%' AND filament_id = $id", ("$id", id));
        Exec(c, "DELETE FROM resins WHERE id = $id", ("$id", id));
        return true;
    }

    // ---------------- Extra costs ----------------

    public List<ExtraCost> GetExtraCosts()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, name, cost, basis, applies_to, active, notes FROM extra_costs ORDER BY id";
        using var r = cmd.ExecuteReader();
        var list = new List<ExtraCost>();
        while (r.Read())
        {
            list.Add(new ExtraCost
            {
                Id = r.GetInt64(0),
                Name = r.GetString(1),
                Cost = Dec(r.GetString(2)),
                Basis = (CostBasis)r.GetInt32(3),
                AppliesTo = r.IsDBNull(4) ? null : (PrintTechnology)r.GetInt32(4),
                Active = r.GetInt64(5) != 0,
                Notes = r.GetString(6),
            });
        }
        return list;
    }

    public void SaveExtraCost(ExtraCost e)
    {
        using var c = Open();
        var args = new (string, object?)[]
        {
            ("$id", e.Id), ("$name", e.Name), ("$cost", Str(e.Cost)), ("$basis", (int)e.Basis),
            ("$applies", e.AppliesTo is PrintTechnology t ? (int)t : null), ("$active", e.Active ? 1 : 0), ("$notes", e.Notes),
        };
        if (e.Id == 0)
            e.Id = Scalar<long>(c, """
                INSERT INTO extra_costs(name, cost, basis, applies_to, active, notes)
                VALUES ($name, $cost, $basis, $applies, $active, $notes) RETURNING id
                """, args);
        else
            Exec(c, "UPDATE extra_costs SET name=$name, cost=$cost, basis=$basis, applies_to=$applies, active=$active, notes=$notes WHERE id=$id", args);
    }

    /// <summary>Extra costs are re-applied from the current list whenever a job is priced, so deleting one is always safe.</summary>
    public void DeleteExtraCost(long id)
    {
        using var c = Open();
        Exec(c, "DELETE FROM extra_costs WHERE id = $id", ("$id", id));
    }

    /// <summary>Everything a job is priced from, as currently saved.</summary>
    public PriceBook LoadPriceBook() => new(LoadSettings(), GetFilaments(), GetResins(), GetPrinters(), GetExtraCosts());

    // ---------------- Jobs ----------------

    public List<Job> GetJobs()
    {
        using var c = Open();
        var jobs = new Dictionary<long, Job>();
        var parts = new Dictionary<long, JobPart>();
        var plates = new Dictionary<long, JobPlate>();

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id, title, customer, invoice_number, created_date, invoice_date, modeling_hours, post_processing, paid, paid_date, notes, printed, printed_date, finalized, finalized_date, excluded_extra_costs, price_snapshot, finalized_total FROM jobs ORDER BY created_date DESC, id DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var j = new Job
                {
                    Id = r.GetInt64(0),
                    Title = r.GetString(1),
                    Customer = r.GetString(2),
                    InvoiceNumber = r.IsDBNull(3) ? null : r.GetString(3),
                    CreatedDate = Date(r.GetString(4)),
                    InvoiceDate = r.IsDBNull(5) ? null : Date(r.GetString(5)),
                    ModelingHours = Dec(r.GetString(6)),
                    PostProcessing = r.GetInt64(7) != 0,
                    Paid = r.GetInt64(8) != 0,
                    PaidDate = r.IsDBNull(9) ? null : Date(r.GetString(9)),
                    Notes = r.GetString(10),
                    Printed = r.GetInt64(11) != 0,
                    PrintedDate = r.IsDBNull(12) ? null : Date(r.GetString(12)),
                    Finalized = r.GetInt64(13) != 0,
                    FinalizedDate = r.IsDBNull(14) ? null : Date(r.GetString(14)),
                    ExcludedExtraCosts = IdSet(r.GetString(15)),
                    PriceSnapshot = r.IsDBNull(16) ? null : r.GetString(16),
                    FinalizedTotal = r.IsDBNull(17) ? null : Dec(r.GetString(17)),
                };
                jobs[j.Id] = j;
            }
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id, job_id, name, charge_slicing FROM parts ORDER BY job_id, sort";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var p = new JobPart { Id = r.GetInt64(0), Name = r.GetString(2), ChargeSlicing = r.GetInt64(3) != 0 };
                parts[p.Id] = p;
                if (jobs.TryGetValue(r.GetInt64(1), out var j)) j.Parts.Add(p);
            }
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id, part_id, source_file, plate_index, copies, parts_per_plate, sliced_seconds, override_seconds, thumbnail, has_failures, failed_count, failure_note, technology, printer_id, layer_count, layer_height, resin_profile, resin_sliced_ml, resin_override_ml, resin_id, resin_customer_supplied, parts_confirmed FROM plates ORDER BY part_id, sort";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var p = new JobPlate
                {
                    Id = r.GetInt64(0),
                    SourceFile = r.GetString(2),
                    PlateIndex = r.GetInt32(3),
                    Copies = r.GetInt32(4),
                    PartsPerPlate = r.GetInt32(5),
                    SlicedSeconds = r.GetInt32(6),
                    OverrideSeconds = r.IsDBNull(7) ? null : r.GetInt32(7),
                    Thumbnail = r.IsDBNull(8) ? null : (byte[])r[8],
                    HasFailures = r.GetInt64(9) != 0,
                    FailedCount = r.GetInt32(10),
                    FailureNote = r.GetString(11),
                    Technology = (PrintTechnology)r.GetInt32(12),
                    PrinterId = r.IsDBNull(13) ? null : r.GetInt64(13),
                    LayerCount = r.GetInt32(14),
                    LayerHeightMm = Dec(r.GetString(15)),
                    PartsCountConfirmed = r.GetInt64(21) != 0,
                };
                if (p.Technology == PrintTechnology.Resin)
                {
                    p.Resin = new PlateResin
                    {
                        SlicerProfile = r.GetString(16),
                        SlicedMl = Dec(r.GetString(17)),
                        OverrideMl = r.IsDBNull(18) ? null : Dec(r.GetString(18)),
                        ResinId = r.IsDBNull(19) ? null : r.GetInt64(19),
                        CustomerSupplied = r.GetInt64(20) != 0,
                    };
                }
                plates[p.Id] = p;
                if (parts.TryGetValue(r.GetInt64(1), out var part)) part.Plates.Add(p);
            }
        }

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT id, plate_id, slot_id, slicer_type, slicer_color_hex, slicer_vendor, slicer_profile, sliced_grams, override_grams, filament_id, customer_supplied FROM plate_filaments ORDER BY plate_id, slot_id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var f = new PlateFilament
                {
                    Id = r.GetInt64(0),
                    SlotId = r.GetInt32(2),
                    SlicerType = r.GetString(3),
                    SlicerColorHex = r.GetString(4),
                    SlicerVendor = r.GetString(5),
                    SlicerProfile = r.GetString(6),
                    SlicedGrams = Dec(r.GetString(7)),
                    OverrideGrams = r.IsDBNull(8) ? null : Dec(r.GetString(8)),
                    FilamentId = r.IsDBNull(9) ? null : r.GetInt64(9),
                    CustomerSupplied = r.GetInt64(10) != 0,
                };
                if (plates.TryGetValue(r.GetInt64(1), out var plate)) plate.Filaments.Add(f);
            }
        }

        return jobs.Values.ToList();
    }

    public void SaveJob(Job job)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();

        var args = new (string, object?)[]
        {
            ("$id", job.Id), ("$title", job.Title), ("$customer", job.Customer), ("$inv", job.InvoiceNumber),
            ("$created", DateStr(job.CreatedDate)), ("$invdate", job.InvoiceDate is DateTime d ? DateStr(d) : null),
            ("$model", Str(job.ModelingHours)), ("$post", job.PostProcessing ? 1 : 0), ("$paid", job.Paid ? 1 : 0),
            ("$paiddate", job.PaidDate is DateTime pd ? DateStr(pd) : null), ("$notes", job.Notes),
            ("$printed", job.Printed ? 1 : 0), ("$printeddate", job.PrintedDate is DateTime prd ? DateStr(prd) : null),
            ("$fin", job.Finalized ? 1 : 0), ("$findate", job.FinalizedDate is DateTime fd ? DateStr(fd) : null),
            ("$excluded", string.Join(",", job.ExcludedExtraCosts.Order())),
            ("$snapshot", job.PriceSnapshot), ("$ftotal", job.FinalizedTotal is decimal ft ? Str(ft) : null),
        };

        if (job.Id == 0)
        {
            job.Id = Scalar<long>(c, """
                INSERT INTO jobs(title, customer, invoice_number, created_date, invoice_date, modeling_hours, post_processing, paid, paid_date, notes, printed, printed_date, finalized, finalized_date, excluded_extra_costs, price_snapshot, finalized_total)
                VALUES ($title, $customer, $inv, $created, $invdate, $model, $post, $paid, $paiddate, $notes, $printed, $printeddate, $fin, $findate, $excluded, $snapshot, $ftotal) RETURNING id
                """, args);
        }
        else
        {
            Exec(c, """
                UPDATE jobs SET title=$title, customer=$customer, invoice_number=$inv, created_date=$created, invoice_date=$invdate,
                    modeling_hours=$model, post_processing=$post, paid=$paid, paid_date=$paiddate, notes=$notes,
                    printed=$printed, printed_date=$printeddate, finalized=$fin, finalized_date=$findate, excluded_extra_costs=$excluded,
                    price_snapshot=$snapshot, finalized_total=$ftotal WHERE id=$id
                """, args);
            Exec(c, "DELETE FROM parts WHERE job_id = $id", ("$id", job.Id));
        }

        for (int pi = 0; pi < job.Parts.Count; pi++)
        {
            var part = job.Parts[pi];
            part.Id = Scalar<long>(c, "INSERT INTO parts(job_id, sort, name, charge_slicing) VALUES ($j, $s, $n, $c) RETURNING id",
                ("$j", job.Id), ("$s", pi), ("$n", part.Name), ("$c", part.ChargeSlicing ? 1 : 0));

            for (int li = 0; li < part.Plates.Count; li++)
            {
                var plate = part.Plates[li];
                plate.Id = Scalar<long>(c, """
                    INSERT INTO plates(part_id, sort, source_file, plate_index, copies, parts_per_plate, sliced_seconds, override_seconds, thumbnail,
                        has_failures, failed_count, failure_note, technology, printer_id, layer_count, layer_height,
                        resin_profile, resin_sliced_ml, resin_override_ml, resin_id, resin_customer_supplied, parts_confirmed)
                    VALUES ($p, $s, $f, $i, $c, $n, $sec, $ov, $th, $hf, $fc, $fn, $tech, $printer, $layers, $lh, $rp, $rml, $rov, $rid, $rcs, $pc) RETURNING id
                    """,
                    ("$p", part.Id), ("$s", li), ("$f", plate.SourceFile), ("$i", plate.PlateIndex), ("$c", plate.Copies),
                    ("$n", plate.PartsPerPlate), ("$sec", plate.SlicedSeconds), ("$ov", plate.OverrideSeconds), ("$th", plate.Thumbnail),
                    ("$hf", plate.HasFailures ? 1 : 0), ("$fc", plate.FailedCount), ("$fn", plate.FailureNote),
                    ("$tech", (int)plate.Technology), ("$printer", plate.PrinterId), ("$layers", plate.LayerCount), ("$lh", Str(plate.LayerHeightMm)),
                    ("$rp", plate.Resin?.SlicerProfile ?? ""), ("$rml", Str(plate.Resin?.SlicedMl ?? 0)),
                    ("$rov", plate.Resin?.OverrideMl is decimal rov ? Str(rov) : null), ("$rid", plate.Resin?.ResinId),
                    ("$rcs", plate.Resin?.CustomerSupplied == true ? 1 : 0), ("$pc", plate.PartsCountConfirmed ? 1 : 0));

                foreach (var f in plate.Filaments)
                {
                    f.Id = Scalar<long>(c, """
                        INSERT INTO plate_filaments(plate_id, slot_id, slicer_type, slicer_color_hex, slicer_vendor, slicer_profile,
                            sliced_grams, override_grams, filament_id, customer_supplied)
                        VALUES ($p, $slot, $t, $hex, $v, $prof, $g, $og, $fid, $cs) RETURNING id
                        """,
                        ("$p", plate.Id), ("$slot", f.SlotId), ("$t", f.SlicerType), ("$hex", f.SlicerColorHex),
                        ("$v", f.SlicerVendor), ("$prof", f.SlicerProfile), ("$g", Str(f.SlicedGrams)),
                        ("$og", f.OverrideGrams is decimal og ? Str(og) : null), ("$fid", f.FilamentId), ("$cs", f.CustomerSupplied ? 1 : 0));
                }
            }
        }

        tx.Commit();
    }

    public void SetPaid(long jobId, bool paid, DateTime? paidDate)
    {
        using var c = Open();
        Exec(c, "UPDATE jobs SET paid = $paid, paid_date = $date WHERE id = $id",
            ("$id", jobId), ("$paid", paid ? 1 : 0), ("$date", paidDate is DateTime d ? DateStr(d) : null));
    }

    public void SetPrinted(long jobId, bool printed, DateTime? printedDate)
    {
        using var c = Open();
        Exec(c, "UPDATE jobs SET printed = $p, printed_date = $date WHERE id = $id",
            ("$id", jobId), ("$p", printed ? 1 : 0), ("$date", printedDate is DateTime d ? DateStr(d) : null));
    }

    public void DeleteJob(long id)
    {
        using var c = Open();
        Exec(c, "DELETE FROM jobs WHERE id = $id", ("$id", id));
    }

    // ---------------- helpers ----------------

    private static void Exec(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(c, sql, args);
        cmd.ExecuteNonQuery();
    }

    private static T Scalar<T>(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(c, sql, args);
        return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T), CultureInfo.InvariantCulture);
    }

    private static SqliteCommand Command(SqliteConnection c, string sql, (string Name, object? Value)[] args)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            if (sql.Contains(name, StringComparison.Ordinal))
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return cmd;
    }

    private static string Str(decimal d) => d.ToString(CultureInfo.InvariantCulture);
    private static HashSet<long> IdSet(string s) =>
        s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => long.Parse(x, CultureInfo.InvariantCulture)).ToHashSet();
    private static decimal Dec(string s) => decimal.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static string DateStr(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static DateTime Date(string s) => DateTime.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
