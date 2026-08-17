using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Split;
using Application.DTOs.Transactions;
using Domain.Entities.Split;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class SplitService : ISplitService
{
    private readonly IApplicationDbContext _context;
    private readonly ITransactionService _transactionService;

    public SplitService(IApplicationDbContext context, ITransactionService transactionService)
    {
        _context = context;
        _transactionService = transactionService;
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

        // The creator can manage any member's details (the V1 model for
        // unlinked members). A linked member can additionally manage
        // their own — better privacy and UX than routing every UPI
        // update through the creator.
        if (member.SplitGroup.CreatedByUserId != userId && member.LinkedUserId != userId)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "You can only update your own payment details."));

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

        // Every payer and participant must actually belong to this
        // group — without this check, a crafted request could reference
        // a member ID from a completely different group, attaching this
        // expense to someone else's group data.
        var groupMemberIds = group.Members.Select(m => m.Id).ToHashSet();
        var allReferencedIds = dto.Payers.Select(p => p.MemberId)
            .Concat(dto.Participants.Select(p => p.MemberId));

        if (allReferencedIds.Any(id => !groupMemberIds.Contains(id)))
            return Result.Failure<ExpenseDto>(new Error(
                "SplitExpense.MemberNotInGroup",
                "One or more payers or participants don't belong to this group."));

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

        // The group creator can record a settlement on behalf of any
        // member (the intended V1 model — the creator operates the app
        // for members who don't have their own FinPlanner login). A
        // LINKED member, if one exists, can only create a settlement
        // where they themselves are the one paying.
        if (group.CreatedByUserId != userId)
        {
            var callerMember = FindMemberForUser(group, userId);
            if (callerMember == null || callerMember.Id != dto.FromMemberId)
                return Result.Failure<SettlementDto>(new Error(
                    "SplitGroup.Forbidden", "You can only create a settlement where you're the one paying."));
        }

        // Validate against the FromMember's actual overall outstanding
        // debt — not against the specific ToMember pairing, since a
        // settlement can legitimately go to someone other than whoever
        // the simplified debt plan suggested. What must hold regardless
        // is that nobody can settle for more than they actually owe in
        // total.
        var currentBalances = SplitBalanceCalculator.CalculateNetBalances(group);
        var fromMemberBalance = currentBalances.First(b => b.MemberId == dto.FromMemberId).NetBalance;
        var fromMemberOwes = Math.Max(0, -fromMemberBalance);

        if (dto.Amount > fromMemberOwes + 0.01m) // small tolerance for rounding
        {
            return Result.Failure<SettlementDto>(new Error(
                "SplitSettlement.ExceedsDebt",
                $"{fromMember.Name} owes ₹{fromMemberOwes:F2} overall — this settlement (₹{dto.Amount:F2}) exceeds that."));
        }

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
            .Include(s => s.SplitGroup).ThenInclude(g => g.Members) // needed for FindMemberForUser below — wasn't loaded before
            .FirstOrDefaultAsync(s => s.Id == settlementId);

        if (settlement == null)
            return Result.Failure<bool>(new Error("SplitSettlement.NotFound", "Settlement not found."));

        // Same principle as creating a settlement: the creator can
        // confirm on behalf of anyone; a linked member can only confirm
        // their own payment.
        if (settlement.SplitGroup.CreatedByUserId != userId)
        {
            var callerMember = FindMemberForUser(settlement.SplitGroup, userId);
            if (callerMember == null || callerMember.Id != settlement.FromMemberId)
                return Result.Failure<bool>(new Error(
                    "SplitGroup.Forbidden", "Only the person who made this payment can confirm it."));
        }

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

        // Narrower than general group access — a payment request
        // specifically should only go to the creator or the actual
        // person paying, not any member who happens to have access to
        // the group.
        if (settlement.SplitGroup.CreatedByUserId != userId)
        {
            var callerMember = FindMemberForUser(settlement.SplitGroup, userId);
            if (callerMember == null || callerMember.Id != settlement.FromMemberId)
                return Result.Failure<PaymentRequestDto>(new Error(
                    "SplitGroup.Forbidden", "You can only request payment details for your own settlement."));
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
            // UpiId deliberately omitted here — this is a public,
            // no-login view. Payment identifiers should only be visible
            // to someone actually operating the group (the creator),
            // not anyone who happens to have the link.
            Members = group.Members.Select(m => new MemberDto { Id = m.Id, Name = m.Name, UpiId = null }).ToList(),
            Expenses = group.Expenses.OrderByDescending(e => e.Date).Select(MapExpenseToDto).ToList(),
            Balances = ComputeBalancesDto(group)
        });
    }

    public async Task<Result<InviteCreatedDto>> CreateInviteAsync(string userId, CreateInviteDto dto)
    {
        var group = await _context.SplitGroups.FirstOrDefaultAsync(g => g.Id == dto.GroupId);
        if (group == null)
            return Result.Failure<InviteCreatedDto>(new Error("SplitGroup.NotFound", "Group not found."));

        if (group.CreatedByUserId != userId)
            return Result.Failure<InviteCreatedDto>(new Error("SplitGroup.Forbidden", "Only the group creator can create invites."));

        var token = InviteTokenHelper.GenerateToken();
        var invite = new SplitGroupInvite
        {
            SplitGroupId = dto.GroupId,
            CreatedByUserId = userId,
            TokenHash = InviteTokenHelper.Hash(token),
            ExpiresAt = dto.ExpiresAt,
            MaxUses = dto.MaxUses
        };

        _context.SplitGroupInvites.Add(invite);
        await _context.SaveChangesAsync();

        // The only moment the plain token exists anywhere — returned
        // once, here, then never retrievable again. Only its hash lives
        // in the database from this point forward.
        return Result.Success(new InviteCreatedDto
        {
            Token = token,
            ExpiresAt = invite.ExpiresAt,
            MaxUses = invite.MaxUses
        });
    }

    public async Task<Result<InvitePreviewDto>> PreviewInviteAsync(string token)
    {
        var tokenHash = InviteTokenHelper.Hash(token);
        var invite = await _context.SplitGroupInvites
            .Include(i => i.SplitGroup).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash);

        if (invite == null)
            return Result.Success(new InvitePreviewDto { IsValid = false, InvalidReason = "not found" });

        var (isValid, reason) = ValidateInvite(invite);

        return Result.Success(new InvitePreviewDto
        {
            GroupName = invite.SplitGroup.Name,
            MemberCount = invite.SplitGroup.Members.Count,
            IsValid = isValid,
            InvalidReason = reason
        });
    }

    public async Task<Result<JoinGroupResultDto>> JoinViaInviteAsync(string userId, JoinGroupDto dto)
    {
        var tokenHash = InviteTokenHelper.Hash(dto.Token);
        var invite = await _context.SplitGroupInvites
            .Include(i => i.SplitGroup).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash);

        if (invite == null)
            return Result.Failure<JoinGroupResultDto>(new Error("SplitInvite.NotFound", "This invite link isn't valid."));

        var (isValid, reason) = ValidateInvite(invite);
        if (!isValid)
            return Result.Failure<JoinGroupResultDto>(new Error("SplitInvite.Invalid", $"This invite is no longer valid ({reason})."));

        // Already joined via this exact link before — safe to just
        // confirm membership again, not an error. Makes the join
        // endpoint idempotent, which matters if someone opens the link
        // twice or double-taps "Join."
        var existingMember = invite.SplitGroup.Members.FirstOrDefault(m => m.LinkedUserId == userId);
        if (existingMember != null)
        {
            return Result.Success(new JoinGroupResultDto
            {
                GroupId = invite.SplitGroupId,
                MemberId = existingMember.Id,
                AlreadyWasMember = true
            });
        }

        var newMember = new SplitGroupMember
        {
            SplitGroupId = invite.SplitGroupId,
            Name = dto.DisplayName,
            LinkedUserId = userId
        };

        _context.SplitGroupMembers.Add(newMember);
        invite.UsedCount++;
        _context.SplitGroupInvites.Update(invite);
        await _context.SaveChangesAsync();

        return Result.Success(new JoinGroupResultDto
        {
            GroupId = invite.SplitGroupId,
            MemberId = newMember.Id,
            AlreadyWasMember = false
        });
    }

    public async Task<Result<bool>> RevokeInviteAsync(string userId, int inviteId)
    {
        var invite = await _context.SplitGroupInvites
            .Include(i => i.SplitGroup)
            .FirstOrDefaultAsync(i => i.Id == inviteId);

        if (invite == null)
            return Result.Failure<bool>(new Error("SplitInvite.NotFound", "Invite not found."));

        if (invite.SplitGroup.CreatedByUserId != userId)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "Only the group creator can revoke invites."));

        invite.RevokedAt = DateTime.UtcNow;
        _context.SplitGroupInvites.Update(invite);
        await _context.SaveChangesAsync();

        return Result.Success(true);
    }

    public async Task<Result<bool>> CloseGroupAsync(string userId, int groupId)
    {
        var group = await _context.SplitGroups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null)
            return Result.Failure<bool>(new Error("SplitGroup.NotFound", "Group not found."));

        if (group.CreatedByUserId != userId)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "Only the group creator can close the group."));

        group.Status = SplitGroupStatus.Settled;
        _context.SplitGroups.Update(group);
        await _context.SaveChangesAsync();

        return Result.Success(true);
    }

    public async Task<Result<ImportToLedgerResultDto>> ImportToLedgerAsync(string userId, ImportToLedgerDto dto)
    {
        var group = await _context.SplitGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == dto.GroupId);

        if (group == null)
            return Result.Failure<ImportToLedgerResultDto>(new Error("SplitGroup.NotFound", "Group not found."));

        // Deliberately restricted to closed groups, matching the flow
        // you described — importing mid-trip, while expenses are still
        // being added, would mean re-running this repeatedly and
        // reasoning about partial state. Closing first gives one clean,
        // final import.
        if (group.Status != SplitGroupStatus.Settled && group.Status != SplitGroupStatus.Archived)
            return Result.Failure<ImportToLedgerResultDto>(new Error(
                "SplitGroup.NotClosed", "Close this group before importing it to your ledger."));

        var callerMember = group.Members.FirstOrDefault(m => m.LinkedUserId == userId);
        if (callerMember == null)
            return Result.Failure<ImportToLedgerResultDto>(new Error(
                "SplitGroup.NotAMember", "You need to be a linked member of this group to import your share."));

        var participations = await _context.SplitExpenseParticipants
            .Include(p => p.SplitExpense)
            .Where(p => p.SplitGroupMemberId == callerMember.Id && p.ImportedTransactionId == null)
            .ToListAsync();

        int imported = 0;
        foreach (var participation in participations)
        {
            var expense = participation.SplitExpense;

            var result = await _transactionService.UpsertTransactionAsync(userId, dto.AccountId, new UpsertTransactionDto
            {
                Description = $"[{group.Name}] {expense.Description} — your share",
                Amount = participation.ShareAmount,
                Type = TransactionType.Expense,
                Date = expense.Date, // the ORIGINAL expense date, not today — this is the whole point
                TransactionCategoryId = null // left uncategorized deliberately rather than guessing at a mapping
            });

            if (result.IsSuccess)
            {
                // result.Value's Id comes back as an int from UpsertTransactionAsync's TransactionDto
                participation.ImportedTransactionId = result.Value.Id;
                _context.SplitExpenseParticipants.Update(participation);
                imported++;
            }
            // A single failed import (e.g. a concurrency conflict on the
            // target account) doesn't abort the whole batch — it's
            // simply left un-imported, and re-running this action later
            // will pick it up, since ImportedTransactionId stays null.
        }

        await _context.SaveChangesAsync();

        return Result.Success(new ImportToLedgerResultDto
        {
            TransactionsCreated = imported,
            AlreadyImportedCount = participations.Count - imported
        });
    }

    public async Task<Result<List<SettlementDto>>> GetSettlementHistoryAsync(string userId, int groupId)
    {
        var group = await LoadGroupForUserAsync(userId, groupId);
        if (group == null)
            return Result.Failure<List<SettlementDto>>(new Error("SplitGroup.NotFound", "Group not found."));

        var settlements = await _context.SplitSettlements
            .Include(s => s.FromMember)
            .Include(s => s.ToMember)
            .Where(s => s.SplitGroupId == groupId)
            .OrderByDescending(s => s.CompletedAt ?? s.CreatedAt)
            .ToListAsync();

        return Result.Success(settlements.Select(s => new SettlementDto
        {
            Id = s.Id,
            FromMemberName = s.FromMember.Name,
            ToMemberName = s.ToMember.Name,
            Amount = s.Amount,
            Method = s.Method,
            Status = s.Status,
            PaymentReference = s.PaymentReference,
            CompletedAt = s.CompletedAt
        }).ToList());
    }

    private static (bool IsValid, string? Reason) ValidateInvite(SplitGroupInvite invite)
    {
        if (invite.RevokedAt.HasValue) return (false, "revoked");
        if (invite.ExpiresAt.HasValue && invite.ExpiresAt.Value < DateTime.UtcNow) return (false, "expired");
        if (invite.MaxUses.HasValue && invite.UsedCount >= invite.MaxUses.Value) return (false, "fully used");
        return (true, null);
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the group member record linked to this authenticated user,
    /// if any. Null if the user isn't linked to any member in this
    /// group — which today means anyone except the creator, since no
    /// join flow exists yet to link anyone else.
    /// </summary>
    private static SplitGroupMember? FindMemberForUser(SplitGroup group, string userId)
        => group.Members.FirstOrDefault(m => m.LinkedUserId == userId);

    private async Task<SplitGroup?> LoadGroupForUserAsync(string userId, int groupId)
    {
        var group = await _context.SplitGroups
            .Include(g => g.Members)
            .Include(g => g.Expenses).ThenInclude(e => e.Payers)
            .Include(g => g.Expenses).ThenInclude(e => e.Participants)
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
