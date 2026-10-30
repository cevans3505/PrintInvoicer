using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;

namespace PrintInvoicer.Tests;

public sealed class FinalizeTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pi-fin-{Guid.NewGuid():N}.db");
    private static readonly DateTime Created = new(2026, 10, 29);
    private static readonly DateTime Delivered = new(2026, 11, 12);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }

    [Fact]
    public void Finalizing_a_draft_dates_it_today()
    {
        var job = new Job { CreatedDate = Created, InvoiceNumber = "2026-004" };
        Assert.False(job.WouldRedate(Delivered));

        job.FinalizeInvoice(Delivered, redate: false);

        Assert.True(job.Finalized);
        Assert.Equal(Delivered, job.InvoiceDate);
        Assert.Equal(Delivered, job.FinalizedDate);
    }

    [Fact]
    public void Refinalizing_keeps_the_original_date_unless_asked()
    {
        var job = new Job { InvoiceNumber = "2026-004" };
        job.FinalizeInvoice(Created, redate: false);

        Assert.True(job.WouldRedate(Delivered));
        job.FinalizeInvoice(Delivered, redate: false);
        Assert.Equal(Created, job.InvoiceDate);

        job.FinalizeInvoice(Delivered, redate: true);
        Assert.Equal(Delivered, job.InvoiceDate);
        Assert.False(job.WouldRedate(Delivered));
    }

    [Fact]
    public void Revert_to_draft_clears_the_date_but_keeps_the_number()
    {
        var job = new Job { InvoiceNumber = "2026-004" };
        job.FinalizeInvoice(Created, redate: false);
        job.RevertToDraft();

        Assert.False(job.Finalized);
        Assert.Null(job.InvoiceDate);
        Assert.Null(job.FinalizedDate);
        Assert.Equal("2026-004", job.InvoiceNumber);
    }

    [Fact]
    public void Finalized_state_round_trips()
    {
        var db = new Database(_path);
        var job = new Job { Title = "a", InvoiceNumber = "2026-001" };
        db.SaveJob(job);
        Assert.False(db.GetJobs()[0].Finalized);

        job.FinalizeInvoice(Delivered, redate: false);
        db.SaveJob(job);

        var back = db.GetJobs()[0];
        Assert.True(back.Finalized);
        Assert.Equal(Delivered, back.FinalizedDate);
        Assert.Equal(Delivered, back.InvoiceDate);
    }
}
