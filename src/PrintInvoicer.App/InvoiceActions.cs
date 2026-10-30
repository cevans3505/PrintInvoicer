using System.Windows;
using PrintInvoicer.Core.Data;
using PrintInvoicer.Core.Models;
using PrintInvoicer.Core.Pricing;

namespace PrintInvoicer.App;

/// <summary>Finalizing shared by the job window and the job list.</summary>
public static class InvoiceActions
{
    /// <summary>
    /// Asks whether invoices finalized on an earlier day should be re-dated to today. Returns false when none need
    /// asking, and null if the user cancelled.
    /// </summary>
    public static bool? AskRedate(IEnumerable<Job> jobs, DateTime today)
    {
        var earlier = jobs.Where(j => j.WouldRedate(today)).ToList();
        if (earlier.Count == 0) return false;

        string which = earlier.Count == 1
            ? $"Invoice {earlier[0].InvoiceNumber} was already finalized on {earlier[0].InvoiceDate:MMMM d, yyyy}."
            : $"{earlier.Count} of these invoices were already finalized on an earlier day.";
        var answer = MessageBox.Show(
            $"{which}\n\nChange the invoice date to today, {today:MMMM d}? Payment terms run from the invoice date.\n\n" +
            "Yes: date it today.\nNo: keep the original date.",
            "Finalize invoice", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };
    }

    /// <summary>
    /// A new install starts with no business name or contact line. Returns true when they are set, or when the user
    /// chooses to finalize anyway.
    /// </summary>
    public static bool ConfirmBusinessDetails(AppSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.BusinessName) && !string.IsNullOrWhiteSpace(s.ContactLine)) return true;
        return MessageBox.Show(
            "Your name or contact line is blank, so the invoice header will be missing it. Set them on the Settings tab.\n\nFinalize anyway?",
            "Finalize invoice", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Gives the job a number if it has none, marks it finalized and dated (see <see cref="Job.FinalizeInvoice"/>), and
    /// freezes its prices so the invoice never changes afterwards. Re-finalizing keeps the prices from the first time.
    /// </summary>
    public static void Finalize(Database db, Job job, DateTime today, bool redate)
    {
        job.InvoiceNumber ??= db.AllocateInvoiceNumber(today);
        job.FinalizeInvoice(today, redate);
        PriceSnapshot.Freeze(job, db.LoadPriceBook());
    }

    public const string RevertNote =
        "The invoice date is cleared and set again the next time you finalize, and the job is priced at today's rates " +
        "again, so its total can change. The invoice number stays the same.";
}
