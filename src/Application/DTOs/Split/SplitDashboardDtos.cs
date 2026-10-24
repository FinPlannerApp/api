using Domain.Entities.Split;

namespace Application.DTOs.Split;

public class CreateFriendDto
{
    /// <summary>The friend's name (they don't need an account).</summary>
    public required string FriendName { get; set; }
    /// <summary>The caller's own display name in the ledger.</summary>
    public required string MyName { get; set; }
}

/// <summary>One group's contribution to what you owe / are owed with one person.</summary>
public class DashboardLineDto
{
    public int GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public bool IsDirect { get; set; }
    public string Currency { get; set; } = "INR";
    /// <summary>Positive = they owe you; negative = you owe them.</summary>
    public decimal Amount { get; set; }
}

public class DashboardTotalDto
{
    public string Currency { get; set; } = "INR";
    /// <summary>Positive = they owe you; negative = you owe them.</summary>
    public decimal Net { get; set; }
}

public class DashboardPersonDto
{
    /// <summary>"u:{userId}" for someone with an account (merged across groups), else "m:{memberId}".</summary>
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Your one-to-one ledger with them, when you have one.</summary>
    public int? DirectGroupId { get; set; }
    public List<DashboardTotalDto> Totals { get; set; } = new();
    public List<DashboardLineDto> Lines { get; set; } = new();
}

public class DashboardCurrencyTotalDto
{
    public string Currency { get; set; } = "INR";
    public decimal YouAreOwed { get; set; }
    public decimal YouOwe { get; set; }
}

public class SplitDashboardDto
{
    public List<DashboardCurrencyTotalDto> Totals { get; set; } = new();
    public List<DashboardPersonDto> People { get; set; } = new();
    public int GroupCount { get; set; }
}

public class SettleWithPersonDto
{
    public required string PersonKey { get; set; }
    /// <summary>Only the lines in this currency are settled (currencies are never mixed).</summary>
    public required string Currency { get; set; }
    public SettlementMethod Method { get; set; }
}

public class SettleWithPersonResultDto
{
    public int PaymentsRecorded { get; set; }
    public decimal Net { get; set; }
}
