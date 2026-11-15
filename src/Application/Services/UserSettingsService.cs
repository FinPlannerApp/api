using System.Text.Json;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Settings;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Stores per-user, per-module settings as JSON. New modules add an entry to <see cref="Modules"/> with a
/// type and a validator — no database migration needed for new settings.
/// </summary>
public class UserSettingsService : IUserSettingsService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int MaxJsonBytes = 4096;

    private sealed record ModuleDef(Type Type, Func<object> Defaults, Func<object, string?> Validate);

    private static readonly Dictionary<string, ModuleDef> Modules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["general"] = new(typeof(GeneralSettingsDto), () => new GeneralSettingsDto(), o =>
        {
            var s = (GeneralSettingsDto)o;
            return s.MonthStartDay is < 1 or > 28 ? "Month start day must be between 1 and 28." : null;
        }),
        ["accounts"] = new(typeof(AccountSettingsDto), () => new AccountSettingsDto(), o =>
        {
            var s = (AccountSettingsDto)o;
            if (s.LowBalanceThreshold is < 0 or > 1_000_000_000m) return "Low-balance level must be between 0 and 1,000,000,000.";
            if (s.CardDueReminderDays is < 0 or > 14) return "Card due reminder must be between 0 and 14 days.";
            return null;
        }),
        ["transactions"] = new(typeof(TransactionSettingsDto), () => new TransactionSettingsDto(), o =>
        {
            var s = (TransactionSettingsDto)o;
            if (s.DuplicateGuardSeconds is < 0 or > 120) return "Duplicate guard must be between 0 and 120 seconds.";
            if (s.LargeExpenseThreshold is < 0 or > 1_000_000_000m) return "Large-expense level must be between 0 and 1,000,000,000.";
            if (s.DefaultAccountId is <= 0 || s.DefaultCategoryId is <= 0) return "Default account/category is invalid.";
            if (s.QuickCardsMax is < 0 or > 30) return "Quick Cards must be between 0 and 30.";
            if (s.QuickCardsMinUses is < 2 or > 20) return "Quick Card repeats must be between 2 and 20.";
            if (s.QuickCardsDays is < 14 or > 365) return "Quick Card look-back must be between 14 and 365 days.";
            return null;
        }),
        ["subscriptions"] = new(typeof(SubscriptionSettingsDto), () => new SubscriptionSettingsDto(), o =>
        {
            var s = (SubscriptionSettingsDto)o;
            if (s.RenewalAlertDays is < 0 or > 30) return "Renewal alert must be between 0 and 30 days.";
            if (s.MonthlyTarget is < 0 or > 1_000_000_000m) return "Monthly target must be between 0 and 1,000,000,000.";
            return null;
        }),
        ["recurring"] = new(typeof(RecurringSettingsDto), () => new RecurringSettingsDto(), o =>
        {
            var s = (RecurringSettingsDto)o;
            return s.LookAheadDays is < 7 or > 180 ? "Look-ahead must be between 7 and 180 days." : null;
        }),
        ["notifications"] = new(typeof(NotificationSettingsDto), () => new NotificationSettingsDto(), o =>
        {
            var s = (NotificationSettingsDto)o;
            return s.ReminderDaysBefore is < 1 or > 14 ? "Remind me must be between 1 and 14 days before." : null;
        }),
        ["budgets"] = new(typeof(BudgetSettingsDto), () => new BudgetSettingsDto(), o =>
        {
            var s = (BudgetSettingsDto)o;
            return s.DefaultAlertPercent is < 50 or > 100 ? "Alert level must be between 50% and 100%." : null;
        }),
    };

    private readonly IApplicationDbContext _context;

    public UserSettingsService(IApplicationDbContext context) => _context = context;

    public async Task<Result<object>> GetAsync(string userId, string module, CancellationToken ct = default)
    {
        if (!Modules.TryGetValue(module, out var def))
            return Result.Failure<object>(new Error("Settings.UnknownModule", "Unknown settings module."));
        return Result<object>.Success(await LoadAsync(userId, module, def, ct));
    }

    public async Task<Result<object>> SaveAsync(string userId, string module, JsonElement body, CancellationToken ct = default)
    {
        if (!Modules.TryGetValue(module, out var def))
            return Result.Failure<object>(new Error("Settings.UnknownModule", "Unknown settings module."));

        object? parsed;
        try { parsed = body.Deserialize(def.Type, Json); }
        catch (JsonException) { return Result.Failure<object>(new Error("Settings.Invalid", "Settings could not be read.")); }
        if (parsed is null)
            return Result.Failure<object>(new Error("Settings.Invalid", "Settings could not be read."));

        var problem = def.Validate(parsed);
        if (problem != null)
            return Result.Failure<object>(new Error("Settings.Invalid", problem));

        var data = JsonSerializer.Serialize(parsed, def.Type, Json);
        if (data.Length > MaxJsonBytes)
            return Result.Failure<object>(new Error("Settings.TooLarge", "Settings are too large."));

        var key = module.ToLowerInvariant();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var row = await _context.UserModuleSettings.FirstOrDefaultAsync(s => s.UserId == userId && s.Module == key, ct);
            if (row == null)
                _context.UserModuleSettings.Add(new UserModuleSetting { UserId = userId, Module = key, DataJson = data });
            else
                row.DataJson = data;
            try
            {
                await _context.SaveChangesAsync(ct);
                return Result<object>.Success(parsed);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Two first-time saves raced on the unique (UserId, Module) index — reload and update instead.
                // Drop the failed pending insert (removing an Added entity just detaches it).
                foreach (var e in _context.UserModuleSettings.Local.Where(s => s.UserId == userId && s.Module == key && s.Id == 0).ToList())
                    _context.UserModuleSettings.Remove(e);
            }
        }
        return Result.Failure<object>(new Error("Settings.SaveFailed", "Settings could not be saved. Please try again."));
    }

    public async Task<TransactionSettingsDto> GetTransactionSettingsAsync(string userId, CancellationToken ct = default)
        => (TransactionSettingsDto)await LoadAsync(userId, "transactions", Modules["transactions"], ct);

    public async Task<NotificationSettingsDto> GetNotificationSettingsAsync(string userId, CancellationToken ct = default)
        => (NotificationSettingsDto)await LoadAsync(userId, "notifications", Modules["notifications"], ct);

    public async Task<GeneralSettingsDto> GetGeneralAsync(string userId, CancellationToken ct = default)
        => (GeneralSettingsDto)await LoadAsync(userId, "general", Modules["general"], ct);

    public async Task<BudgetSettingsDto> GetBudgetSettingsAsync(string userId, CancellationToken ct = default)
        => (BudgetSettingsDto)await LoadAsync(userId, "budgets", Modules["budgets"], ct);

    private async Task<object> LoadAsync(string userId, string module, ModuleDef def, CancellationToken ct)
    {
        var key = module.ToLowerInvariant();
        var json = await _context.UserModuleSettings.AsNoTracking()
            .Where(s => s.UserId == userId && s.Module == key)
            .Select(s => s.DataJson)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return def.Defaults();
        try
        {
            var value = JsonSerializer.Deserialize(json, def.Type, Json);
            // A stored value that no longer passes validation (e.g. rules tightened later) falls back to defaults.
            return value != null && def.Validate(value) == null ? value : def.Defaults();
        }
        catch (JsonException) { return def.Defaults(); }
    }
}
