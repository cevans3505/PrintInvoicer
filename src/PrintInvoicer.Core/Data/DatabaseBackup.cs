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

    /// <summary>The Backups folder next to the database, used when no backup folder is set in Settings.</summary>
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
        // AUDIT: file delete. Removes the oldest daily backups (printinvoicer-YYYY-MM-DD.db) in the backup folder only.
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
        // The copy is page for page, so a damaged backup would replace good data with a database that will not open.
        if (!IsIntact(backupPath))
            throw new InvalidDataException($"{Path.GetFileName(backupPath)} is damaged, so it was not restored. Try an older backup.");

        // AUDIT: file write. A restore first backs up the current data, then copies the chosen backup over the database.
        string safety = Manual(dbPath, folder, now, "before-restore");
        SqliteConnection.ClearAllPools();
        using (var src = Open(backupPath, readOnly: true))
        using (var dst = Open(dbPath, readOnly: false))
            src.BackupDatabase(dst);
        SqliteConnection.ClearAllPools();
        return safety;
    }

    /// <summary>True when the file is a SQLite database with PrintInvoicer's main tables; checked before a restore.</summary>
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

    // SQLite's own integrity check on a backup before restoring it, so a damaged copy never replaces good data.
    private static bool IsIntact(string path)
    {
        try
        {
            using var c = Open(path, readOnly: true);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check(1)";
            return cmd.ExecuteScalar() as string == "ok";
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    // Copies the database with SQLite's backup API (consistent even while the app is using it) to a temporary name,
    // then renames it into place, so an interrupted copy never leaves a half-written backup.
    private static void Copy(string dbPath, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        string temp = target + ".partial";
        if (File.Exists(temp)) File.Delete(temp);
        using (var src = Open(dbPath, readOnly: true))
        using (var dst = Open(temp, readOnly: false))
            src.BackupDatabase(dst);
        // AUDIT: file write. Puts a finished backup in place in the backup folder.
        File.Move(temp, target, overwrite: true);
    }

    // Unpooled, so the backup files are released as soon as the copy is done and can be moved or deleted.
    private static SqliteConnection Open(string path, bool readOnly)
    {
        // AUDIT: file read/write. Opens the database or a backup file for copying.
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
    // Daily backups are named printinvoicer-YYYY-MM-DD.db; only these are pruned.
    private static partial Regex DailyName();
}
