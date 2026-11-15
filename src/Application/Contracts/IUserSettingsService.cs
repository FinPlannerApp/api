using System.Text.Json;
using Application.Common.Models;
using Application.DTOs.Settings;

namespace Application.Contracts;

public interface IUserSettingsService
{
    /// <summary>Settings for a module as an object (defaults when nothing is saved yet).</summary>
    Task<Result<object>> GetAsync(string userId, string module, CancellationToken ct = default);
    /// <summary>Validates and saves; returns the normalised settings.</summary>
    Task<Result<object>> SaveAsync(string userId, string module, JsonElement body, CancellationToken ct = default);

    Task<GeneralSettingsDto> GetGeneralAsync(string userId, CancellationToken ct = default);
    Task<TransactionSettingsDto> GetTransactionSettingsAsync(string userId, CancellationToken ct = default);
    Task<NotificationSettingsDto> GetNotificationSettingsAsync(string userId, CancellationToken ct = default);
    Task<BudgetSettingsDto> GetBudgetSettingsAsync(string userId, CancellationToken ct = default);
}
