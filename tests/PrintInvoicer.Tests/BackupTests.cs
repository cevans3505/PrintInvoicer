using Microsoft.Data.Sqlite;
using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;

namespace PrintInvoicer.Tests;

public sealed class BackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pi-backup-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_dir, "printinvoicer.db");
    private string Folder => Path.Combine(_dir, "Backups");

    public BackupTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private Database DbWithJob(string title)
    {
        var db = new Database(DbPath);
        db.SaveJob(new Job { Title = title, InvoiceNumber = "2026-001" });
        return db;
    }

    [Fact]
    public void One_daily_backup_a_day()
    {
        DbWithJob("Caps");
        var day = new DateTime(2026, 10, 30, 8, 0, 0);
        var first = DatabaseBackup.DailyIfDue(DbPath, Folder, day);
        Assert.NotNull(first);
        Assert.Null(DatabaseBackup.DailyIfDue(DbPath, Folder, day.AddHours(6)));   // same day: skipped
        Assert.NotNull(DatabaseBackup.DailyIfDue(DbPath, Folder, day.AddDays(1)));
        Assert.Equal(2, DatabaseBackup.List(Folder).Count);
        Assert.Empty(Directory.GetFiles(Folder, "*.partial"));

        var copy = new Database(first!);
        Assert.Equal("Caps", copy.GetJobs().Single().Title);
    }

    [Fact]
    public void Keeps_the_newest_daily_backups_and_every_manual_one()
    {
        DbWithJob("Caps");
        var start = new DateTime(2026, 10, 1, 9, 0, 0);
        DatabaseBackup.Manual(DbPath, Folder, start);
        for (int d = 0; d < 20; d++) DatabaseBackup.DailyIfDue(DbPath, Folder, start.AddDays(d), keep: 14);

        var names = DatabaseBackup.List(Folder).Select(f => f.Name).ToList();
        Assert.Equal(15, names.Count);                                  // 14 daily + 1 manual
        Assert.Contains("printinvoicer-2026-10-20.db", names);          // newest daily kept
        Assert.DoesNotContain("printinvoicer-2026-10-06.db", names);    // oldest pruned
        Assert.Contains(names, n => n.EndsWith("-manual.db"));
    }

    [Fact]
    public void Restore_brings_back_old_data_and_saves_the_current_first()
    {
        var db = DbWithJob("Before");
        var backup = DatabaseBackup.Manual(DbPath, Folder, new DateTime(2026, 10, 30, 9, 0, 0));

        var job = db.GetJobs().Single();
        job.Title = "After a mistake";
        db.SaveJob(job);

        var safety = DatabaseBackup.Restore(backup, DbPath, Folder, new DateTime(2026, 10, 30, 10, 0, 0));

        Assert.Equal("Before", new Database(DbPath).GetJobs().Single().Title);
        Assert.Equal("After a mistake", new Database(safety).GetJobs().Single().Title);
        Assert.EndsWith("-before-restore.db", safety);
    }

    [Fact]
    public void Restore_refuses_files_that_are_not_backups()
    {
        DbWithJob("Keep me");
        string junk = Path.Combine(_dir, "notes.db");
        File.WriteAllText(junk, "not a database");
        Assert.Throws<InvalidDataException>(() => DatabaseBackup.Restore(junk, DbPath, Folder, DateTime.Now));

        string other = Path.Combine(_dir, "other.db");
        using (var c = new SqliteConnection($"Data Source={other};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE stuff (x INTEGER)";
            cmd.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => DatabaseBackup.Restore(other, DbPath, Folder, DateTime.Now));
        Assert.Equal("Keep me", new Database(DbPath).GetJobs().Single().Title);
    }

    [Fact]
    public void Backup_works_while_the_app_has_the_database_open()
    {
        var db = DbWithJob("Open");
        using var holder = new SqliteConnection($"Data Source={DbPath}");
        holder.Open();
        using (var tx = holder.BeginTransaction())   // a write in progress elsewhere
        {
            var path = DatabaseBackup.DailyIfDue(DbPath, Folder, DateTime.Today);
            Assert.True(File.Exists(path));
        }
        Assert.Single(db.GetJobs());
    }
}
