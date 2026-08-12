using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Split;
using Domain.Entities.Split;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class SplitService : ISplitService
{
    private readonly IApplicationDbContext _context;

    public SplitService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<GroupDto>> CreateGroupAsync(string userId, CreateGroupDto dto)
    {
        var group = new SplitGroup
        {
            Name = dto.Name,
            CreatedByUserId = userId,
            ShareToken = GenerateShareToken()
        };

        // The creator is automatically the first member, linked to their
        // real account — everyone else added later is just a name unless
        // explicitly linked.
        var creatorMember = new SplitGroupMember
        {
            Name = dto.CreatorName,
            LinkedUserId = userId
        };
        group.Members.Add(creatorMember);

        _context.SplitGroups.Add(group);
        await _context.SaveChangesAsync();

        return Result.Success(MapToDto(group));
    }

    public async Task<Result<List<GroupDto>>> GetMyGroupsAsync(string userId)
    {
        // "My groups" means groups where I created it OR I'm a linked
        // member — a creator who added themselves under a different
        // display name would still show up correctly either way, but the
        // common case (creator == the one linked member with your UserId)
        // covers this cleanly.
        var groups = await _context.SplitGroups
            .Include(g => g.Members)
            .Include(g => g.Expenses)
            .Where(g => g.CreatedByUserId == userId || g.Members.Any(m => m.LinkedUserId == userId))
            .ToListAsync();

        return Result.Success(groups.Select(MapToDto).ToList());
    }

    public async Task<Result<GroupDto>> GetGroupAsync(string userId, int groupId)
    {
        var group = await LoadGroupForUserAsync(userId, groupId);
        if (group == null)
            return Result.Failure<GroupDto>(new Error("SplitGroup.NotFound", "Group not found."));

        return Result.Success(MapToDto(group));
    }

    public async Task<Result<MemberDto>> AddMemberAsync(string userId, AddMemberDto dto)
    {
        var group = await LoadGroupForUserAsync(userId, dto.GroupId);
        if (group == null)
            return Result.Failure<MemberDto>(new Error("SplitGroup.NotFound", "Group not found."));

        var member = new SplitGroupMember
        {
            SplitGroupId = dto.GroupId,
            Name = dto.Name,
            UpiId = dto.UpiId
        };

        _context.SplitGroupMembers.Add(member);
        await _context.SaveChangesAsync();

        return Result.Success(new MemberDto { Id = member.Id, Name = member.Name, UpiId = member.UpiId });
    }

    public async Task<Result<bool>> UpdateMemberUpiAsync(string userId, UpdateMemberUpiDto dto)
    {
        var member = await _context.SplitGroupMembers
            .Include(m => m.SplitGroup)
            .FirstOrDefaultAsync(m => m.Id == dto.MemberId);

        if (member == null)
            return Result.Failure<bool>(new Error("SplitMember.NotFound", "Member not found."));

        if (member.SplitGroup.CreatedByUserId != userId)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "Only the group creator can update a member's payment details right now."));

        member.UpiId = dto.UpiId;
        _context.SplitGroupMembers.Update(member);
        await _context.SaveChangesAsync();

        return Result.Success(true);
    }

    public async Task<Result<ExpenseDto>> AddExpenseAsync(string userId, CreateExpenseDto dto)
    {
        var group = await LoadGroupForUserAsync(userId, dto.GroupId);
        if (group == null)
            return Result.Failure<ExpenseDto>(new Error("SplitGroup.NotFound", "Group not found."));

        if (dto.Amount <= 0)
            return Result.Failure<ExpenseDto>(new Error("SplitExpense.InvalidAmount", "Amount must be greater than zero."));

        var payersSum = dto.Payers.Sum(p => p.AmountPaid);
        if (Math.Abs(payersSum - dto.Amount) > 0.01m)
            return Result.Failure<ExpenseDto>(new Error(
                "SplitExpense.PayersMismatch",
                $"Payers add up to {payersSum:F2}, but the expense total is {dto.Amount:F2} — they need to match."));

        List<(int MemberId, decimal Share)> shares;
        try
        {
            var participantInputs = dto.Participants.Select(p => new ExpenseParticipantInput
            {
                MemberId = p.MemberId,
                ExactAmount = p.ExactAmount,
                Percentage = p.Percentage,
                Shares = p.Shares
            }).ToList();

            shares = SplitShareCalculator.ComputeShares(dto.SplitType, dto.Amount, participantInputs);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<ExpenseDto>(new Error("SplitExpense.InvalidSplit", ex.Message));
        }

        var expense = new SplitExpense
        {
            SplitGroupId = dto.GroupId,
            Description = dto.Description,
            Amount = dto.Amount,
            Date = dto.Date,
            Category = dto.Category,
            SplitType = dto.SplitType
        };

        foreach (var payer in dto.Payers)
        {
            expense.Payers.Add(new SplitExpensePayer { SplitGroupMemberId = payer.MemberId, AmountPaid = payer.AmountPaid });
        }

        foreach (var (memberId, share) in shares)
        {
            expense.Participants.Add(new SplitExpenseParticipant { SplitGroupMemberId = memberId, ShareAmount = share });
        }

        _context.SplitExpenses.Add(expense);
        await _context.SaveChangesAsync();

        return await GetExpenseDtoAsync(expense.Id);
    }

    public async Task<Result<List<ExpenseDto>>> GetExpensesAsync(string userId, int groupId)
    {
        var group = await LoadGroupForUserAsync(userId, groupId);
        if (group == null)
            return Result.Failure<List<ExpenseDto>>(new Error("SplitGroup.NotFound", "Group not found."));

        var expenses = await _context.SplitExpenses
            .Include(e => e.Payers).ThenInclude(p => p.SplitGroupMember)
            .Include(e => e.Participants).ThenInclude(p => p.SplitGroupMember)
            .Where(e => e.SplitGroupId == groupId)
            .OrderByDescending(e => e.Date)
            .ToListAsync();

        return Result.Success(expenses.Select(MapExpenseToDto).ToList());
    }

    public async Task<Result<GroupBalancesDto>> GetBalancesAsync(string userId, int groupId)
    {
        var group = await LoadGroupForUserAsync(userId, groupId);
        if (group == null)
            return Result.Failure<GroupBalancesDto>(new Error("SplitGroup.NotFound", "Group not found."));

        return Result.Success(ComputeBalancesDto(group));
    }

    public async Task<Result<SettlementDto>> CreateSettlementAsync(string userId, CreateSettlementDto dto)
    {
        var group = await LoadGroupForUserAsync(userId, dto.GroupId);
        if (group == null)
            return Result.Failure<SettlementDto>(new Error("SplitGroup.NotFound", "Group not found."));

        if (dto.Amount <= 0)
            return Result.Failure<SettlementDto>(new Error("SplitSettlement.InvalidAmount", "Amount must be greater than zero."));

        if (dto.FromMemberId == dto.ToMemberId)
            return Result.Failure<SettlementDto>(new Error("SplitSettlement.SameMember", "A member can't settle with themselves."));

        var fromMember = group.Members.FirstOrDefault(m => m.Id == dto.FromMemberId);
        var toMember = group.Members.FirstOrDefault(m => m.Id == dto.ToMemberId);
        if (fromMember == null || toMember == null)
            return Result.Failure<SettlementDto>(new Error("SplitMember.NotFound", "One or both members not found in this group."));

        var settlement = new SplitSettlement
        {
            SplitGroupId = dto.GroupId,
            FromMemberId = dto.FromMemberId,
            ToMemberId = dto.ToMemberId,
            Amount = dto.Amount,
            Method = dto.Method,
            UpiIdUsed = dto.Method == SettlementMethod.Upi ? toMember.UpiId : null,
            PaymentReference = GeneratePaymentReference(),
            Status = SettlementStatus.Pending
        };

        _context.SplitSettlements.Add(settlement);
        await _context.SaveChangesAsync();

        return Result.Success(new SettlementDto
        {
            Id = settlement.Id,
            FromMemberName = fromMember.Name,
            ToMemberName = toMember.Name,
            Amount = settlement.Amount,
            Method = settlement.Method,
            Status = settlement.Status,
            PaymentReference = settlement.PaymentReference,
            CompletedAt = settlement.CompletedAt
        });
    }

    public async Task<Result<bool>> MarkSettlementPaidAsync(string userId, int settlementId)
    {
        var settlement = await _context.SplitSettlements
            .Include(s => s.SplitGroup)
            .FirstOrDefaultAsync(s => s.Id == settlementId);

        if (settlement == null)
            return Result.Failure<bool>(new Error("SplitSettlement.NotFound", "Settlement not found."));

        if (settlement.SplitGroup.CreatedByUserId != userId)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "Only the group creator can confirm settlements right now."));

        settlement.Status = SettlementStatus.Completed;
        settlement.CompletedAt = DateTime.UtcNow;
        _context.SplitSettlements.Update(settlement);
        await _context.SaveChangesAsync();

        return Result.Success(true);
    }

    public async Task<Result<PaymentRequestDto>> GetPaymentRequestAsync(string userId, int settlementId)
    {
        // SECURITY: every value in the response is derived from the
        // settlement record and the recipient's CURRENT membership data,
        // loaded fresh from the database. Nothing here is ever built from
        // a client-supplied amount or UPI ID — that's the whole point of
        // this endpoint existing separately from a generic "give me a QR
        // for this amount" utility. A client cannot turn a ₹500
        // settlement into a ₹50,000 payment or redirect it to a
        // different UPI ID by tampering with a request.
        var settlement = await _context.SplitSettlements
            .Include(s => s.SplitGroup).ThenInclude(g => g.Members)
            .Include(s => s.ToMember)
            .FirstOrDefaultAsync(s => s.Id == settlementId);

        if (settlement == null)
            return Result.Failure<PaymentRequestDto>(new Error("SplitSettlement.NotFound", "Settlement not found."));

        if (settlement.SplitGroup.CreatedByUserId != userId &&
            settlement.SplitGroup.Members.All(m => m.LinkedUserId != userId))
        {
            return Result.Failure<PaymentRequestDto>(new Error("SplitGroup.Forbidden", "You don't have access to this settlement."));
        }

        if (string.IsNullOrWhiteSpace(settlement.ToMember.UpiId))
            return Result.Failure<PaymentRequestDto>(new Error(
                "SplitSettlement.NoUpiId",
                $"{settlement.ToMember.Name} hasn't added a UPI ID yet — record this payment manually instead."));

        var deepLink = UpiDeepLinkGenerator.Generate(
            settlement.ToMember.UpiId,
            settlement.ToMember.Name,
            settlement.Amount,
            $"{settlement.SplitGroup.Name} settlement",
            settlement.PaymentReference);

        return Result.Success(new PaymentRequestDto
        {
            UpiDeepLink = deepLink,
            Amount = settlement.Amount,
            RecipientName = settlement.ToMember.Name,
            PaymentReference = settlement.PaymentReference
        });
    }

    public async Task<Result<PublicGroupViewDto>> GetPublicGroupViewAsync(string shareToken)
    {
        var group = await _context.SplitGroups
            .Include(g => g.Members)
            .Include(g => g.Expenses).ThenInclude(e => e.Payers).ThenInclude(p => p.SplitGroupMember)
            .Include(g => g.Expenses).ThenInclude(e => e.Participants).ThenInclude(p => p.SplitGroupMember)
            .Include(g => g.Settlements)
            .FirstOrDefaultAsync(g => g.ShareToken == shareToken);

        if (group == null)
            return Result.Failure<PublicGroupViewDto>(new Error("SplitGroup.NotFound", "This link doesn't point to a valid group."));

        return Result.Success(new PublicGroupViewDto
        {
            GroupName = group.Name,
            Currency = group.Currency,
            Members = group.Members.Select(m => new MemberDto { Id = m.Id, Name = m.Name, UpiId = m.UpiId }).ToList(),
            Expenses = group.Expenses.OrderByDescending(e => e.Date).Select(MapExpenseToDto).ToList(),
            Balances = ComputeBalancesDto(group)
        });
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private async Task<SplitGroup?> LoadGroupForUserAsync(string userId, int groupId)
    {
        var group = await _context.SplitGroups
            .Include(g => g.Members)
            .Include(g => g.Expenses)
            .Include(g => g.Settlements)
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (group == null) return null;

        // Access = created it, or linked as a member. No admin-approval
        // workflow — deliberately kept simple per the scoped V1 design.
        var hasAccess = group.CreatedByUserId == userId || group.Members.Any(m => m.LinkedUserId == userId);
        return hasAccess ? group : null;
    }

    private async Task<Result<ExpenseDto>> GetExpenseDtoAsync(int expenseId)
    {
        var expense = await _context.SplitExpenses
            .Include(e => e.Payers).ThenInclude(p => p.SplitGroupMember)
            .Include(e => e.Participants).ThenInclude(p => p.SplitGroupMember)
            .FirstAsync(e => e.Id == expenseId);

        return Result.Success(MapExpenseToDto(expense));
    }

    private static GroupBalancesDto ComputeBalancesDto(SplitGroup group)
    {
        var balances = SplitBalanceCalculator.CalculateNetBalances(group);
        var simplified = SplitBalanceCalculator.SimplifyDebts(balances);

        return new GroupBalancesDto
        {
            Balances = balances.Select(b => new MemberBalanceDto
            {
                MemberId = b.MemberId,
                MemberName = b.MemberName,
                NetBalance = b.NetBalance
            }).ToList(),
            SimplifiedPlan = simplified.Select(s => new SimplifiedDebtDto
            {
                FromMemberId = s.FromMemberId,
                FromMemberName = s.FromMemberName,
                ToMemberId = s.ToMemberId,
                ToMemberName = s.ToMemberName,
                Amount = s.Amount
            }).ToList()
        };
    }

    private static ExpenseDto MapExpenseToDto(SplitExpense e) => new()
    {
        Id = e.Id,
        Description = e.Description,
        Amount = e.Amount,
        Date = e.Date,
        Category = e.Category,
        SplitType = e.SplitType,
        Payers = e.Payers.Select(p => new PayerLineDto { MemberName = p.SplitGroupMember.Name, AmountPaid = p.AmountPaid }).ToList(),
        Participants = e.Participants.Select(p => new ParticipantLineDto { MemberName = p.SplitGroupMember.Name, ShareAmount = p.ShareAmount }).ToList()
    };

    private static GroupDto MapToDto(SplitGroup g) => new()
    {
        Id = g.Id,
        Name = g.Name,
        Currency = g.Currency,
        Status = g.Status,
        ShareToken = g.ShareToken,
        Members = g.Members.Select(m => new MemberDto { Id = m.Id, Name = m.Name, LinkedUserId = m.LinkedUserId, UpiId = m.UpiId }).ToList(),
        TotalSpend = g.Expenses.Sum(e => e.Amount)
    };

    private static string GenerateShareToken()
    {
        // 24 bytes of cryptographic randomness, base64url-encoded —
        // unguessable, and safe to put directly in a URL.
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string GeneratePaymentReference()
    {
        return $"FP-SET-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }
}
