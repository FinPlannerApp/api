using Domain.Entities.Split;

namespace Application.DTOs.Split;

public class CreateGroupDto
{
    public required string Name { get; set; }
    public required string CreatorName { get; set; } // the creator's own display name within the group
}

public class GroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "INR";
    public SplitGroupStatus Status { get; set; }
    public string ShareToken { get; set; } = string.Empty;
    public List<MemberDto> Members { get; set; } = new();
    public decimal TotalSpend { get; set; }
}

public class MemberDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? LinkedUserId { get; set; }
    public string? UpiId { get; set; }
}

public class AddMemberDto
{
    public int GroupId { get; set; }
    public required string Name { get; set; }
    public string? UpiId { get; set; }
}

public class UpdateMemberUpiDto
{
    public int MemberId { get; set; }
    public required string UpiId { get; set; }
}

public class ExpenseParticipantDto
{
    public int MemberId { get; set; }
    public decimal? ExactAmount { get; set; }
    public decimal? Percentage { get; set; }
    public decimal? Shares { get; set; }
}

public class ExpensePayerDto
{
    public int MemberId { get; set; }
    public decimal AmountPaid { get; set; }
}

public class CreateExpenseDto
{
    public int GroupId { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Category { get; set; }
    public SplitType SplitType { get; set; }
    public List<ExpensePayerDto> Payers { get; set; } = new();
    public List<ExpenseParticipantDto> Participants { get; set; } = new();
}

public class ExpenseDto
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public string? Category { get; set; }
    public SplitType SplitType { get; set; }
    public List<PayerLineDto> Payers { get; set; } = new();
    public List<ParticipantLineDto> Participants { get; set; } = new();
}

public class PayerLineDto
{
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
}

public class ParticipantLineDto
{
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public decimal ShareAmount { get; set; }
}

public class GroupBalancesDto
{
    public List<MemberBalanceDto> Balances { get; set; } = new();
    public List<SimplifiedDebtDto> SimplifiedPlan { get; set; } = new();
}

public class MemberBalanceDto
{
    public int MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public decimal TotalPaid { get; set; }
    public decimal TotalShare { get; set; }
    public decimal NetBalance { get; set; }
}

public class SimplifiedDebtDto
{
    public int FromMemberId { get; set; }
    public string FromMemberName { get; set; } = string.Empty;
    public int ToMemberId { get; set; }
    public string ToMemberName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class CreateSettlementDto
{
    public int GroupId { get; set; }
    public int FromMemberId { get; set; }
    public int ToMemberId { get; set; }
    public decimal Amount { get; set; }
    public SettlementMethod Method { get; set; }
}

public class SettlementDto
{
    public int Id { get; set; }
    public string FromMemberName { get; set; } = string.Empty;
    public string ToMemberName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public SettlementMethod Method { get; set; }
    public SettlementStatus Status { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// The response for "generate a payment request" — every field here is
/// derived server-side from the settlement record and the recipient's
/// current membership data. Nothing in this DTO is ever built from
/// client-supplied amount/recipient values.
/// </summary>
public class PaymentRequestDto
{
    public string UpiDeepLink { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
}

/// <summary>
/// The public, read-only, no-login view for the shareable link. Includes
/// everything a trip participant needs to see (balances, expenses) but
/// nothing that would let them modify the group or see anything about
/// the group creator's actual FinPlanner account/personal finances.
/// </summary>
public class PublicGroupViewDto
{
    public string GroupName { get; set; } = string.Empty;
    public string Currency { get; set; } = "INR";
    public List<MemberDto> Members { get; set; } = new();
    public List<ExpenseDto> Expenses { get; set; } = new();
    public GroupBalancesDto Balances { get; set; } = new();
}

public class GroupFullDetailsDto
{
    public GroupDto Group { get; set; } = new();
    public List<ExpenseDto> Expenses { get; set; } = new();
    public GroupBalancesDto Balances { get; set; } = new();
}
