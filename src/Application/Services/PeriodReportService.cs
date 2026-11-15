using System.Globalization;
using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Reports;
using ClosedXML.Excel;

namespace Application.Services;

/// <summary>
/// Month / year report. The numbers come from the same dashboard service the UI uses, so the
/// report can never disagree with the dashboard. Files are generated only on request.
/// </summary>
public class PeriodReportService : IPeriodReportService
{
    private readonly IDashboardService _dashboard;
    private readonly IUserSettingsService _settings;

    public PeriodReportService(IDashboardService dashboard, IUserSettingsService settings)
    {
        _dashboard = dashboard;
        _settings = settings;
    }

    public async Task<Result<PeriodReportDto>> GetAsync(string userId, int year, int? month)
    {
        if (year < 2000 || year > 2100)
            return Result.Failure<PeriodReportDto>(new Error("Report.InvalidPeriod", "Choose a year between 2000 and 2100."));
        if (month is < 1 or > 12)
            return Result.Failure<PeriodReportDto>(new Error("Report.InvalidPeriod", "Month must be between 1 and 12."));

        var from = month ?? 1;
        var to = month ?? 12;
        // The user's "month" may start on a day other than the 1st (salary day) — see General settings.
        var startDay = (await _settings.GetGeneralAsync(userId)).MonthStartDay;

        var report = new PeriodReportDto
        {
            Mode = month.HasValue ? "month" : "year",
            Year = year,
            Month = month,
            Label = month.HasValue
                ? new DateTime(year, month.Value, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture) + RangeNote(year, month.Value, startDay)
                : year.ToString(CultureInfo.InvariantCulture) + (startDay == 1 ? "" : $" (months start on day {startDay})")
        };

        // Per-month rows (the whole-year view is just twelve of them).
        for (var m = from; m <= to; m++)
        {
            var (utcStart, utcEnd) = MonthWindow(year, m, startDay);
            var summary = await _dashboard.GetSummaryAsync(userId, utcStart, utcEnd);
            if (!summary.IsSuccess) return Result.Failure<PeriodReportDto>(summary.Error);

            report.Months.Add(new PeriodMonthDto
            {
                Month = m,
                Label = new DateTime(year, m, 1).ToString("MMM", CultureInfo.InvariantCulture),
                Income = summary.Value!.MonthlyIncome,
                Expenses = summary.Value.MonthlyExpenses,
                Net = summary.Value.MonthlyIncome - summary.Value.MonthlyExpenses
            });
            report.NetWorthNow = summary.Value.NetWorth;
        }

        report.Income = report.Months.Sum(x => x.Income);
        report.Expenses = report.Months.Sum(x => x.Expenses);
        report.Net = report.Income - report.Expenses;
        report.SavingsRate = report.Income > 0 ? Math.Round(report.Net / report.Income * 100, 1) : 0;

        var (start, _) = MonthWindow(year, from, startDay);
        var (_, end) = MonthWindow(year, to, startDay);
        var categories = await _dashboard.GetSpendingByCategoryAsync(userId, start, end);
        if (!categories.IsSuccess) return Result.Failure<PeriodReportDto>(categories.Error);

        var total = categories.Value!.Sum(c => c.TotalAmount);
        report.Categories = categories.Value!
            .OrderByDescending(c => c.TotalAmount)
            .Select(c => new PeriodCategoryDto
            {
                Name = c.CategoryName,
                Amount = c.TotalAmount,
                Percent = total > 0 ? Math.Round(c.TotalAmount / total * 100, 1) : 0
            }).ToList();

        return Result.Success(report);
    }

    public async Task<Result<byte[]>> ExportExcelAsync(string userId, int year, int? month)
    {
        var result = await GetAsync(userId, year, month);
        if (!result.IsSuccess) return Result.Failure<byte[]>(result.Error);
        var r = result.Value!;

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Summary");
        ws.Cell(1, 1).Value = $"FinPlanner report — {r.Label}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;

        var row = 3;
        void Metric(string name, decimal value, string format = "#,##0.00")
        {
            ws.Cell(row, 1).Value = name;
            ws.Cell(row, 2).Value = value;
            ws.Cell(row, 2).Style.NumberFormat.Format = format;
            row++;
        }
        Metric("Income", r.Income);
        Metric("Expenses", r.Expenses);
        Metric("Net", r.Net);
        Metric("Savings rate (%)", r.SavingsRate, "0.0");
        Metric("Net worth today", r.NetWorthNow);

        row += 1;
        ws.Cell(row, 1).Value = "Month";
        ws.Cell(row, 2).Value = "Income";
        ws.Cell(row, 3).Value = "Expenses";
        ws.Cell(row, 4).Value = "Net";
        ws.Range(row, 1, row, 4).Style.Font.Bold = true;
        row++;
        foreach (var m in r.Months)
        {
            ws.Cell(row, 1).Value = m.Label;
            ws.Cell(row, 2).Value = m.Income;
            ws.Cell(row, 3).Value = m.Expenses;
            ws.Cell(row, 4).Value = m.Net;
            ws.Range(row, 2, row, 4).Style.NumberFormat.Format = "#,##0.00";
            row++;
        }
        ws.Columns().AdjustToContents();

        var cats = wb.AddWorksheet("Categories");
        cats.Cell(1, 1).Value = "Category";
        cats.Cell(1, 2).Value = "Amount";
        cats.Cell(1, 3).Value = "% of spending";
        cats.Range(1, 1, 1, 3).Style.Font.Bold = true;
        var cr = 2;
        foreach (var c in r.Categories)
        {
            cats.Cell(cr, 1).Value = c.Name;
            cats.Cell(cr, 2).Value = c.Amount;
            cats.Cell(cr, 2).Style.NumberFormat.Format = "#,##0.00";
            cats.Cell(cr, 3).Value = c.Percent;
            cats.Cell(cr, 3).Style.NumberFormat.Format = "0.0";
            cr++;
        }
        cats.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return Result.Success(ms.ToArray());
    }

    public async Task<Result<byte[]>> ExportPdfAsync(string userId, int year, int? month)
    {
        var result = await GetAsync(userId, year, month);
        if (!result.IsSuccess) return Result.Failure<byte[]>(result.Error);
        var r = result.Value!;
        string Money(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);

        var pdf = new SimplePdf();
        const double left = 40, right = SimplePdf.PageWidth - 40, bottom = 50;
        var y = SimplePdf.PageHeight - 56;

        void EnsureSpace(double needed)
        {
            if (y - needed >= bottom) return;
            pdf.NewPage();
            y = SimplePdf.PageHeight - 56;
        }

        pdf.Text(left, y, $"FinPlanner report - {r.Label}", 18, bold: true);
        y -= 18;
        pdf.Text(left, y, $"Generated {DateTime.UtcNow:d MMM yyyy}", 9, grey: 0.4);
        y -= 28;

        // Headline numbers
        var labels = new[] { "Income", "Expenses", "Net", "Savings rate" };
        var values = new[] { Money(r.Income), Money(r.Expenses), Money(r.Net), r.SavingsRate.ToString("0.0", CultureInfo.InvariantCulture) + "%" };
        var colWidth = (right - left) / 4;
        for (var i = 0; i < 4; i++)
        {
            pdf.Text(left + i * colWidth, y, labels[i], 8, grey: 0.4);
            pdf.Text(left + i * colWidth, y - 16, values[i], 13, bold: true);
        }
        y -= 42;

        pdf.Text(left, y, r.Mode == "year" ? "Month by month" : "Month", 12, bold: true);
        y -= 8;
        pdf.Line(left, y, right, y);
        y -= 14;
        pdf.Text(left, y, "Month", 9, bold: true);
        pdf.Text(left + 240, y, "Income", 9, bold: true, alignRight: true);
        pdf.Text(left + 370, y, "Expenses", 9, bold: true, alignRight: true);
        pdf.Text(right, y, "Net", 9, bold: true, alignRight: true);
        y -= 14;
        foreach (var m in r.Months)
        {
            EnsureSpace(16);
            pdf.Text(left, y, m.Label, 9);
            pdf.Text(left + 240, y, Money(m.Income), 9, alignRight: true);
            pdf.Text(left + 370, y, Money(m.Expenses), 9, alignRight: true);
            pdf.Text(right, y, Money(m.Net), 9, alignRight: true);
            y -= 14;
        }

        y -= 14;
        EnsureSpace(40);
        pdf.Text(left, y, "Spending by category", 12, bold: true);
        y -= 8;
        pdf.Line(left, y, right, y);
        y -= 14;
        var max = r.Categories.Count > 0 ? r.Categories.Max(c => c.Amount) : 0;
        foreach (var c in r.Categories)
        {
            EnsureSpace(18);
            pdf.Text(left, y, c.Name.Length > 28 ? c.Name[..28] : c.Name, 9);
            if (max > 0) pdf.Bar(left + 170, y - 1, (double)(c.Amount / max) * 150, 7);
            pdf.Text(left + 400, y, Money(c.Amount), 9, alignRight: true);
            pdf.Text(right, y, c.Percent.ToString("0.0", CultureInfo.InvariantCulture) + "%", 9, alignRight: true);
            y -= 14;
        }
        if (r.Categories.Count == 0)
            pdf.Text(left, y, "No spending in this period.", 9, grey: 0.4);

        return Result.Success(pdf.ToBytes());
    }

    /// <summary>" · 5 Nov – 4 Dec" for a money-month that doesn't start on the 1st; empty for a calendar month.</summary>
    private static string RangeNote(int year, int month, int startDay)
    {
        if (startDay == 1) return "";
        var s = new DateTime(year, month, startDay);
        var e = s.AddMonths(1).AddDays(-1);
        return $" · {s.ToString("d MMM", CultureInfo.InvariantCulture)} – {e.ToString("d MMM", CultureInfo.InvariantCulture)}";
    }

    /// <summary>The user's money-month (starting on <paramref name="startDay"/>, 1 = calendar month) as a UTC window.</summary>
    private static (DateTime utcStart, DateTime utcEnd) MonthWindow(int year, int month, int startDay)
    {
        var localStart = new DateTime(year, month, Math.Clamp(startDay, 1, 28), 0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = localStart.AddMonths(1).AddTicks(-1);
        return (localStart.ToUtc(), localEnd.ToUtc());
    }
}
