using Application.Common.Models;
using Application.DTOs.Reports;

namespace Application.Contracts;

public interface IPeriodReportService
{
    /// <summary>The on-screen report for a month (month 1-12) or a whole year (month null).</summary>
    Task<Result<PeriodReportDto>> GetAsync(string userId, int year, int? month);

    /// <summary>Same numbers as an .xlsx file, built only when the user asks.</summary>
    Task<Result<byte[]>> ExportExcelAsync(string userId, int year, int? month);

    /// <summary>Same numbers as a PDF, built only when the user asks.</summary>
    Task<Result<byte[]>> ExportPdfAsync(string userId, int year, int? month);
}
