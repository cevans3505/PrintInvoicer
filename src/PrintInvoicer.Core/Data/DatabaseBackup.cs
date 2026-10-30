using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace PrintInvoicer.Core.Data;

/// <summary>
/// Copies of the database. The app makes one a day when it opens and keeps the newest <see cref="KeepDaily"/>; copies
/// made by hand or before a restore are never pruned. Copies use SQLite's backup API, so they are consistent even while
/// the app has the database open, and are written to a temporary name first so a dropped network share never leaves a
/// half-written backup behind.
/// </summary>
public static partial class DatabaseBackup
{
    public const int KeepDaily = 14;
    private const string Prefix = "printinvoicer-";

    public static string DefaultFolder(string dbPath) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "Backups");

    /// <summary>Makes today's backup unless one exists, then prunes old daily ones. Returns the new file, or null.</summary>
    public static string? DailyIfDue(string dbPath, string folder, DateTime now, int keep = KeepDaily)
    {
        string path = Path.Combine(folder, $"{Prefix}{now:yyyy-MM-dd}.db");
        if (File.Exists(path)) return null;
        Copy(dbPath, path);
        Prune(folder, keep);
        return path;
    }

    /// <summary>A backup made on request, kept until deleted by hand.</summary>
    public static string Manual(string dbPath, string folder, DateTime now, string label = "manual")
    {
        string path = Path.Combine(folder, $"{Prefix}{now:yyyy-MM-dd-HHmmss}-{label}.db");
        Copy(dbPath, path);
        return path;
    }

    /// <summary>Every backup in the folder, newest first.</summary>
    public static IReadOnlyList<FileInfo> List(string folder) =>
        Directory.Exists(folder)
            ? new DirectoryInfo(folder).GetFiles(Prefix + "*.db").OrderByDescending(f => f.LastWriteTimeUtc).ToList()
            : [];

    /// <summary>Deletes all but the newest <paramref name="keep"/> daily backups.</summary>
    public static void Prune(string folder, int keep = KeepDaily)
    {
        var daily = List(folder)
            .Where(f => DailyName().IsMatch(f.Name))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)   // the date in the name, not the file time
            .Skip(keep);
        foreach (var f in daily) f.Delete();
    }

    /// <summary>
    /// Replaces the database's contents with a backup. The current data is backed up first (labelled before-restore).
    /// The caller should restart the app afterwards so nothing keeps showing the old data.
    /// </summary>
    public static string Restore(string backupPath, string dbPath, string folder, DateTime now)
    {
        if (!LooksLikePrintInvoicer(backupPath))
            throw new InvalidDataException($"{Path.GetFileName(backupPath)} is not a PrintInvoicer database.");

        string safety = Manual(dbPath, folder, now, "before-restore");
        SqliteConnection.ClearAllPools();
        using (var src = Open(backupPath, readOnly: true))
        using (var dst = Open(dbPath, readOnly: false))
            src.BackupDatabase(dst);
        SqliteConnection.ClearAllPools();
        return safety;
    }

    public static bool LooksLikePrintInvoicer(string path)
    {
        try
        {
            using var c = Open(path, readOnly: true);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('jobs', 'settings', 'plates')";
            return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 3;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static void Copy(string dbPath, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        string temp = target + ".partial";
        if (File.Exists(temp)) File.Delete(temp);
        using (var src = Open(dbPath, readOnly: true))
        using (var dst = Open(temp, readOnly: false))
            src.BackupDatabase(dst);
        File.Move(temp, target, overwrite: true);
    }

    // Unpooled, so the backup files are released as soon as the copy is done and can be moved or deleted.
    private static SqliteConnection Open(string path, bool readOnly)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        c.Open();
        return c;
    }

    [GeneratedRegex(@"^printinvoicer-\d{4}-\d{2}-\d{2}\.db$")]
    private static partial Regex DailyName();
}
