namespace Domain.Entities;

/// <summary>One row per user per module ("general", "budgets", ...). The JSON is validated and normalised by UserSettingsService.</summary>
public class UserModuleSetting : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
}
