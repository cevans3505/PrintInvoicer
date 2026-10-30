using PrintInvoicer.Core.Models;

namespace PrintInvoicer.Tests;

public class JobFilterTests
{
    private static Job MakeJob(bool paid = false, bool finalized = false, bool printed = false, DateTime? invoiceDate = null) => new()
    {
        Title = "Mini Thread Viewers",
        Customer = "Harbor Dive Shop",
        Notes = "Rush order for the dive show",
        CreatedDate = new DateTime(2026, 10, 28),
        InvoiceDate = invoiceDate,
        Paid = paid,
        Finalized = finalized,
        Printed = printed,
    };

    private static readonly Dictionary<SearchColumn, string> Text = new()
    {
        [SearchColumn.Invoice] = "2026-004",
        [SearchColumn.Job] = "Mini Thread Viewers",
        [SearchColumn.Pieces] = "10 Mini Thread Viewer Body (1 failed)",
        [SearchColumn.Customer] = "Harbor Dive Shop",
        [SearchColumn.Notes] = "Rush order for the dive show",
        [SearchColumn.Status] = "Draft",
    };

    [Fact]
    public void Empty_filter_matches_everything()
    {
        var f = new JobFilter();
        Assert.True(f.IsEmpty);
        Assert.True(f.Matches(MakeJob(), Text));
        Assert.True(f.Matches(MakeJob(paid: true, finalized: true, printed: true), Text));
    }

    [Fact]
    public void Status_filters_combine()
    {
        var f = new JobFilter { Paid = PaidFilter.Unpaid, Stage = StageFilter.Finalized, Printed = PrintedFilter.Printed };
        Assert.True(f.Matches(MakeJob(finalized: true, printed: true), Text));
        Assert.False(f.Matches(MakeJob(paid: true, finalized: true, printed: true), Text));
        Assert.False(f.Matches(MakeJob(printed: true), Text));
        Assert.False(f.Matches(MakeJob(finalized: true), Text));
    }

    [Fact]
    public void Date_range_is_inclusive_and_uses_created_date_for_drafts()
    {
        var f = new JobFilter { From = new DateTime(2026, 10, 28), To = new DateTime(2026, 10, 31) };
        Assert.True(f.Matches(MakeJob(), Text));                                              // created Oct 28
        Assert.True(f.Matches(MakeJob(finalized: true, invoiceDate: new DateTime(2026, 10, 31)), Text));
        Assert.False(f.Matches(MakeJob(finalized: true, invoiceDate: new DateTime(2026, 11, 1)), Text));
        Assert.False(new JobFilter { From = new DateTime(2026, 10, 29) }.Matches(MakeJob(), Text));
    }

    [Fact]
    public void Every_keyword_must_match_ignoring_case()
    {
        Assert.True(new JobFilter { Keywords = "mini VIEWER" }.Matches(MakeJob(), Text));
        Assert.True(new JobFilter { Keywords = "harbor 2026-004" }.Matches(MakeJob(), Text));  // across columns
        Assert.False(new JobFilter { Keywords = "mini brush" }.Matches(MakeJob(), Text));
    }

    [Fact]
    public void Keywords_can_be_limited_to_one_column()
    {
        Assert.True(new JobFilter { Keywords = "dive", Column = SearchColumn.Notes }.Matches(MakeJob(), Text));
        Assert.False(new JobFilter { Keywords = "dive", Column = SearchColumn.Job }.Matches(MakeJob(), Text));
        Assert.True(new JobFilter { Keywords = "failed", Column = SearchColumn.Pieces }.Matches(MakeJob(), Text));
    }

    [Fact]
    public void Quoted_phrases_match_whole()
    {
        Assert.Equal(new[] { "thread viewer", "body" }, JobFilter.ParseKeywords("\"thread viewer\" body"));
        Assert.True(new JobFilter { Keywords = "\"thread viewers\"" }.Matches(MakeJob(), Text));
        Assert.False(new JobFilter { Keywords = "\"viewers thread\"" }.Matches(MakeJob(), Text));
    }

    [Theory]
    [InlineData("This month", "2026-10-01", "2026-10-31")]
    [InlineData("Last month", "2026-09-01", "2026-09-30")]
    [InlineData("Last 30 days", "2026-10-01", "2026-10-30")]
    [InlineData("This year", "2026-01-01", "2026-12-31")]
    [InlineData("Last year", "2025-01-01", "2025-12-31")]
    public void Named_ranges(string range, string from, string to)
    {
        var (f, t) = JobFilter.RangeFor(range, new DateTime(2026, 10, 30));
        Assert.Equal(DateTime.Parse(from), f);
        Assert.Equal(DateTime.Parse(to), t);
    }

    [Fact]
    public void Last_month_in_January_is_December()
    {
        var (f, t) = JobFilter.RangeFor("Last month", new DateTime(2027, 1, 15));
        Assert.Equal(new DateTime(2026, 12, 1), f);
        Assert.Equal(new DateTime(2026, 12, 31), t);
        Assert.Equal((null, null), JobFilter.RangeFor("All dates", DateTime.Today));
    }
}
