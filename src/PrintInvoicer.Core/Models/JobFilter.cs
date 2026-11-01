using System.Text.RegularExpressions;

namespace PrintInvoicer.Core.Models;

// The choices in the job list's filter dropdowns.
public enum PaidFilter { Any, Unpaid, Paid }
public enum StageFilter { Any, Draft, Finalized }
public enum PrintedFilter { Any, NotPrinted, Printed }

/// <summary>A job-list column the keyword search can be limited to.</summary>
public enum SearchColumn { Any, Invoice, Job, Pieces, Customer, Notes, Status }

/// <summary>The job list's filters. Every set criterion must match.</summary>
public sealed class JobFilter
{
    public PaidFilter Paid { get; set; }
    public StageFilter Stage { get; set; }
    public PrintedFilter Printed { get; set; }

    /// <summary>Inclusive bounds on the job's list date (invoice date, or created date for drafts).</summary>
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>Space-separated words; "quoted phrases" stay together. Every word must appear.</summary>
    public string Keywords { get; set; } = "";
    public SearchColumn Column { get; set; }

    /// <summary>True when no filter is set, so every job shows.</summary>
    public bool IsEmpty =>
        Paid == PaidFilter.Any && Stage == StageFilter.Any && Printed == PrintedFilter.Any &&
        From is null && To is null && string.IsNullOrWhiteSpace(Keywords);

    /// <summary>The date the job list shows and the date range filters on.</summary>
    public static DateTime ListDate(Job job) => (job.InvoiceDate ?? job.CreatedDate).Date;

    /// <param name="text">The searchable text of each column for this job, as shown in the list.</param>
    public bool Matches(Job job, IReadOnlyDictionary<SearchColumn, string> text)
    {
        // Paid, draft or finalized, and printed filters.
        if (Paid == PaidFilter.Unpaid && job.Paid) return false;
        if (Paid == PaidFilter.Paid && !job.Paid) return false;
        if (Stage == StageFilter.Draft && job.Finalized) return false;
        if (Stage == StageFilter.Finalized && !job.Finalized) return false;
        if (Printed == PrintedFilter.NotPrinted && job.Printed) return false;
        if (Printed == PrintedFilter.Printed && !job.Printed) return false;

        // Date range, inclusive at both ends.
        var date = ListDate(job);
        if (From is DateTime from && date < from.Date) return false;
        if (To is DateTime to && date > to.Date) return false;

        // Search words: every word must appear, in the chosen column or in any column.
        var words = ParseKeywords(Keywords);
        if (words.Count == 0) return true;
        var haystack = Column == SearchColumn.Any
            ? string.Join("\n", text.Values)
            : text.GetValueOrDefault(Column, "");
        return words.All(w => haystack.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>The From and To dates (inclusive) for a named range; nulls mean open-ended.</summary>
    public static (DateTime? From, DateTime? To) RangeFor(string range, DateTime today)
    {
        var month = new DateTime(today.Year, today.Month, 1);
        return range switch
        {
            "This month" => (month, month.AddMonths(1).AddDays(-1)),
            "Last month" => (month.AddMonths(-1), month.AddDays(-1)),
            "Last 30 days" => (today.AddDays(-29), today),
            "This year" => (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31)),
            "Last year" => (new DateTime(today.Year - 1, 1, 1), new DateTime(today.Year - 1, 12, 31)),
            _ => (null, null),
        };
    }

    /// <summary>Splits search text into words, keeping "quoted phrases" together as one.</summary>
    public static IReadOnlyList<string> ParseKeywords(string? keywords) =>
        string.IsNullOrWhiteSpace(keywords)
            ? Array.Empty<string>()
            : Regex.Matches(keywords, "\"([^\"]*)\"|(\\S+)")
                .Select(m => m.Groups[1].Success ? m.Groups[1].Value.Trim() : m.Groups[2].Value)
                .Where(w => w.Length > 0)
                .ToList();
}
