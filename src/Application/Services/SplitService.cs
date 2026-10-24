using Application.Common.Helpers;
using Application.Common.Models;
using Application.Contracts;
using Application.DTOs.Split;
using Application.DTOs.Transactions;
using Domain.Entities.Split;
using Domain.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class SplitService : ISplitService
{
    private readonly IApplicationDbContext _context;
    private readonly ITransactionService _transactionService;
    private readonly ISplitNotifier _notifier;

    public SplitService(
        IApplicationDbContext context,
        ITransactionService transactionService,
        ISplitNotifier notifier)
    {
        _context = context;
        _transactionService = transactionService;
        _notifier = notifier;
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
            .Where(g => !g.IsDirect && (g.CreatedByUserId == userId || g.Members.Any(m => m.LinkedUserId == userId)))
            .ToListAsync();

        // The list only needs people who are still in each group.
        var dtos = groups.Select(MapToDto).ToList();
        foreach (var dto in dtos)
            dto.Members = dto.Members.Where(m => m.LeftAt == null).ToList();

        return Result.Success(dtos);
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

        if (group.IsDirect)
            return Result.Failure<MemberDto>(new Error(
                "SplitGroup.DirectLedger", "A friend ledger is just between two people. Create a group to split with more people."));

        var isCallerInGroup = group.CreatedByUserId == userId || group.Members.Any(m => m.LinkedUserId == userId);
        if (!isCallerInGroup)
            return Result.Failure<MemberDto>(new Error(
                "SplitGroup.Forbidden", "Only group members can add new members."));

        var member = new SplitGroupMember
        {
            SplitGroupId = dto.GroupId,
            Name = dto.Name,
            UpiId = dto.UpiId
        };

        _context.SplitGroupMembers.Add(member);
        await _context.SaveChangesAsync();

        await _notifier.NotifyMemberAddedAsync(dto.GroupId, $"👤 {dto.Name} was added to the group.", new MemberDto { Id = member.Id, Name = member.Name, UpiId = member.UpiId });
        await RecordActivityAsync(dto.GroupId, userId, "MemberAdded", $"added {dto.Name} to the group.");

        return Result.Success(new MemberDto { Id = member.Id, Name = member.Name, UpiId = member.UpiId });
    }

    public async Task<Result<bool>> UpdateMemberUpiAsync(string userId, UpdateMemberUpiDto dto)
    {
        var member = await _context.SplitGroupMembers
            .Include(m => m.SplitGroup).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(m => m.Id == dto.MemberId);

        if (member == null)
            return Result.Failure<bool>(new Error("SplitMember.NotFound", "Member not found."));

        var isCallerInGroup = member.SplitGroup.CreatedByUserId == userId ||
                              member.SplitGroup.Members.Any(m => m.LinkedUserId == userId);

        if (!isCallerInGroup)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "Only group members can update payment details."));

        // A registered/linked user can ONLY update their own UPI ID unless the caller is the group admin.
        // A manually added person (LinkedUserId == null) can have their UPI ID updated by group members.
        var isAdmin = member.SplitGroup.CreatedByUserId == userId;
        if (member.LinkedUserId != null && member.LinkedUserId != userId && !isAdmin)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "You can only update your own payment details unless you are the group admin."));

        member.UpiId = dto.UpiId;
        _context.SplitGroupMembers.Update(member);
        await _context.SaveChangesAsync();

        await _notifier.NotifyMemberUpiUpdatedAsync(member.SplitGroupId, $"💳 {member.Name} updated payment details (UPI).", member.Id, member.UpiId);

        return Result.Success(true);
    }

    public async Task<Result<ExpenseDto>> AddExpenseAsync(string userId, CreateExpenseDto dto)
    {
        var group = await LoadGroupForUserAsync(userId, dto.GroupId);
        if (group == null)
            return Result.Failure<ExpenseDto>(new Error("SplitGroup.NotFound", "Group not found."));

        if (group.Status == SplitGroupStatus.Settled || group.Status == SplitGroupStatus.Archived)
            return Result.Failure<ExpenseDto>(new Error(
                "SplitExpense.GroupClosed", "This group is closed — new expenses can't be added."));

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

        var leftMemberIds = group.Members.Where(m => m.LeftAt != null).Select(m => m.Id).ToHashSet();
        if (allReferencedIds.Any(leftMemberIds.Contains))
            return Result.Failure<ExpenseDto>(new Error(
                "SplitExpense.MemberLeft",
                "Someone you picked has left the group — they can't be added to a new expense."));

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
            Date = EnsureUtc(dto.Date),
            Category = dto.Category,
            SplitType = dto.SplitType,
            Notes = NormalizeNotes(dto.Notes)
        };

        foreach (var payer in dto.Payers)
        {
            expense.Payers.Add(new SplitExpensePayer { SplitGroupMemberId = payer.MemberId, AmountPaid = payer.AmountPaid });
        }

        foreach (var (memberId, share) in shares)
        {
            expense.Participants.Add(new SplitExpenseParticipant
            {
                SplitGroupMemberId = memberId,
                ShareAmount = share,
                SplitValue = SplitValueFor(dto.SplitType, dto.Participants.First(p => p.MemberId == memberId))
            });
        }

        _context.SplitExpenses.Add(expense);
        await _context.SaveChangesAsync();

        var dtoRes = await GetExpenseDtoAsync(expense.Id);
        var updatedGroup = await _context.SplitGroups
            .AsNoTracking()
            .Include(g => g.Members)
            .Include(g => g.Expenses).ThenInclude(e => e.Payers)
            .Include(g => g.Expenses).ThenInclude(e => e.Participants)
            .Include(g => g.Settlements)
            .FirstOrDefaultAsync(g => g.Id == dto.GroupId);

        var balances = updatedGroup != null ? ComputeBalancesDto(updatedGroup) : new GroupBalancesDto();
        await _notifier.NotifyExpenseAddedAsync(dto.GroupId, $"Expense '{dto.Description}' (₹{dto.Amount:N2}) added.", dtoRes.Value, balances);
        await RecordActivityAsync(dto.GroupId, userId, "ExpenseAdded", $"added '{dto.Description}' (₹{dto.Amount:N2}).", expense.Id);

        return dtoRes;
    }

    public async Task<Result<ExpensePageDto>> GetExpensesAsync(string userId, int groupId, ExpenseListQuery query)
    {
        var group = await LoadGroupForUserAsync(userId, groupId);
        if (group == null)
            return Result.Failure<ExpensePageDto>(new Error("SplitGroup.NotFound", "Group not found."));

        return await QueryExpensePageAsync(groupId, query);
    }

    public async Task<Result<GroupBalancesDto>> GetBalancesAsync(string userId, int groupId)
    {
        var group = await LoadGroupForUserAsync(userId, groupId);
        if (group == null)
            return Result.Failure<GroupBalancesDto>(new Error("SplitGroup.NotFound", "Group not found."));

        return Result.Success(ComputeBalancesDto(group));
    }

    public async Task<Result<GroupFullDetailsDto>> GetGroupFullDetailsAsync(string userId, int groupId)
    {
        var group = await LoadGroupForUserAsync(userId, groupId);
        if (group == null)
            return Result.Failure<GroupFullDetailsDto>(new Error("SplitGroup.NotFound", "Group not found."));

        var page = await QueryExpensePageAsync(groupId, new ExpenseListQuery());
        if (!page.IsSuccess)
            return Result.Failure<GroupFullDetailsDto>(page.Error);

        var dto = new GroupFullDetailsDto
        {
            Group = MapToDto(group),
            Expenses = page.Value.Items,
            NextCursor = page.Value.NextCursor,
            TotalExpenseCount = page.Value.TotalCount,
            Balances = ComputeBalancesDto(group)
        };

        return Result.Success(dto);
    }

    public async Task<Result<GroupExportDto>> GetGroupExportAsync(string userId, int groupId)
    {
        var group = await LoadGroupForUserAsync(userId, groupId);
        if (group == null)
            return Result.Failure<GroupExportDto>(new Error("SplitGroup.NotFound", "Group not found."));

        var expenses = await _context.SplitExpenses
            .AsNoTracking()
            .Include(e => e.Payers).ThenInclude(p => p.SplitGroupMember)
            .Include(e => e.Participants).ThenInclude(p => p.SplitGroupMember)
            .Where(e => e.SplitGroupId == groupId)
            .OrderBy(e => e.Date).ThenBy(e => e.Id)
            .ToListAsync();

        var settlements = await _context.SplitSettlements
            .AsNoTracking()
            .Include(s => s.FromMember)
            .Include(s => s.ToMember)
            .Where(s => s.SplitGroupId == groupId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();

        return Result.Success(new GroupExportDto
        {
            GroupName = group.Name,
            Currency = group.Currency,
            ExportedAtUtc = DateTime.UtcNow,
            Members = group.Members.Select(m => new MemberDto { Id = m.Id, Name = m.Name, LinkedUserId = m.LinkedUserId, UpiId = m.UpiId, LeftAt = m.LeftAt }).ToList(),
            Expenses = expenses.Select(MapExpenseToDto).ToList(),
            Settlements = settlements.Select(MapSettlementToDto).ToList(),
            Balances = ComputeBalancesDto(group)
        });
    }

    public async Task<Result<SettlementDto>> CreateSettlementAsync(string userId, CreateSettlementDto dto)
    {
        var group = await LoadGroupForUserAsync(userId, dto.GroupId);
        if (group == null)
            return Result.Failure<SettlementDto>(new Error("SplitGroup.NotFound", "Group not found."));

        if (group.Status == SplitGroupStatus.Archived)
            return Result.Failure<SettlementDto>(new Error("SplitSettlement.GroupClosed", "This group is closed — payments can't be recorded."));

        if (dto.Amount <= 0)
            return Result.Failure<SettlementDto>(new Error("SplitSettlement.InvalidAmount", "Amount must be greater than zero."));

        if (dto.FromMemberId == dto.ToMemberId)
            return Result.Failure<SettlementDto>(new Error("SplitSettlement.SameMember", "A member can't settle with themselves."));

        var fromMember = group.Members.FirstOrDefault(m => m.Id == dto.FromMemberId);
        var toMember = group.Members.FirstOrDefault(m => m.Id == dto.ToMemberId);
        if (fromMember == null || toMember == null)
            return Result.Failure<SettlementDto>(new Error("SplitMember.NotFound", "One or both members not found in this group."));

        if (fromMember.LeftAt != null || toMember.LeftAt != null)
            return Result.Failure<SettlementDto>(new Error("SplitSettlement.MemberLeft", "Someone in this payment has left the group."));

        // 1. Authorization: the group creator can record a payment on behalf of anyone.
        // Everyone else can record one only if they are the payer OR the receiver.
        var isAdmin = group.CreatedByUserId == userId;
        var callerMember = FindMemberForUser(group, userId);
        var callerIsPayer = callerMember != null && callerMember.Id == dto.FromMemberId;
        var callerIsReceiver = callerMember != null && callerMember.Id == dto.ToMemberId;
        if (!isAdmin && !callerIsPayer && !callerIsReceiver)
            return Result.Failure<SettlementDto>(new Error(
                "SplitGroup.Forbidden", "You can only record a payment that you're part of."));

        // 2. Limit validation (see CheckSettlementLimits).
        var limitError = CheckSettlementLimits(group, fromMember, toMember, dto.Amount);
        if (limitError != null)
            return Result.Failure<SettlementDto>(limitError);

        // 3. Decide the starting status. "Already paid" recorded by the receiver (or
        // by the admin for other people) is final; recorded by the payer it still needs
        // the receiver's confirmation, so nobody can claim a payment unilaterally.
        var status = SettlementStatus.Pending;
        if (dto.AlreadyPaid)
        {
            var finalByReceiverOrAdmin = callerIsReceiver || (isAdmin && !callerIsPayer);
            status = finalByReceiverOrAdmin ? SettlementStatus.Completed : SettlementStatus.AwaitingConfirmation;
        }

        // 4. Re-use an existing not-yet-sent request for the same pair. A payment that
        // was already marked as sent is never overwritten.
        var settlement = group.Settlements.FirstOrDefault(s =>
            s.FromMemberId == dto.FromMemberId &&
            s.ToMemberId == dto.ToMemberId &&
            s.Status == SettlementStatus.Pending);

        var isUpdate = settlement != null;
        if (settlement == null)
        {
            settlement = new SplitSettlement
            {
                SplitGroupId = dto.GroupId,
                FromMemberId = dto.FromMemberId,
                ToMemberId = dto.ToMemberId,
                PaymentReference = GeneratePaymentReference()
            };
            _context.SplitSettlements.Add(settlement);
        }

        settlement.Amount = dto.Amount;
        settlement.Method = dto.Method;
        settlement.UpiIdUsed = dto.Method == SettlementMethod.Upi ? toMember.UpiId : null;
        settlement.Status = status;
        settlement.CompletedAt = status == SettlementStatus.Completed ? DateTime.UtcNow : null;

        if (isUpdate) _context.SplitSettlements.Update(settlement);
        await _context.SaveChangesAsync();

        var sDto = BuildSettlementDto(settlement, fromMember, toMember);
        var balances = ComputeBalancesDto(group);
        var message = status switch
        {
            SettlementStatus.Completed => $"✅ {fromMember.Name} → {toMember.Name}: ₹{dto.Amount:N2} payment recorded.",
            SettlementStatus.AwaitingConfirmation => $"📲 {fromMember.Name} says they paid {toMember.Name} ₹{dto.Amount:N2} — awaiting confirmation.",
            _ => isUpdate
                ? $"💸 Payment of ₹{dto.Amount:N2} updated ({fromMember.Name} → {toMember.Name})."
                : $"💸 Payment of ₹{dto.Amount:N2} recorded ({fromMember.Name} → {toMember.Name})."
        };
        await _notifier.NotifySettlementRecordedAsync(dto.GroupId, message, sDto, balances);
        await RecordActivityAsync(dto.GroupId, userId, "PaymentRecorded", status switch
        {
            SettlementStatus.Completed => $"recorded a ₹{dto.Amount:N2} payment: {fromMember.Name} → {toMember.Name}.",
            SettlementStatus.AwaitingConfirmation => $"said {fromMember.Name} paid {toMember.Name} ₹{dto.Amount:N2} (awaiting confirmation).",
            _ => $"asked for a ₹{dto.Amount:N2} payment: {fromMember.Name} → {toMember.Name}."
        });

        if (status == SettlementStatus.Completed)
            await CheckAndAutoSettleAsync(group.Id);

        return Result.Success(sDto);
    }

    /// <summary>Edit the amount / method of a payment that hasn't been sent yet.</summary>
    public async Task<Result<SettlementDto>> UpdateSettlementAsync(string userId, int settlementId, UpdateSettlementDto dto)
    {
        var settlement = await _context.SplitSettlements
            .Include(s => s.FromMember)
            .Include(s => s.ToMember)
            .FirstOrDefaultAsync(s => s.Id == settlementId);
        if (settlement == null)
            return Result.Failure<SettlementDto>(new Error("SplitSettlement.NotFound", "Settlement not found."));

        var group = await LoadGroupForUserAsync(userId, settlement.SplitGroupId);
        if (group == null)
            return Result.Failure<SettlementDto>(new Error("SplitGroup.Forbidden", "You don't have access to this group."));

        if (group.Status == SplitGroupStatus.Archived)
            return Result.Failure<SettlementDto>(new Error("SplitSettlement.GroupClosed", "This group is closed — payments can't be changed."));

        if (!CanManageSettlement(group, settlement, userId))
            return Result.Failure<SettlementDto>(new Error("SplitGroup.Forbidden", "Only the admin, the payer or the receiver can edit this payment."));

        if (settlement.Status != SettlementStatus.Pending)
            return Result.Failure<SettlementDto>(new Error(
                "SplitSettlement.WrongState",
                "Only a payment that hasn't been sent yet can be edited. Delete it and record it again instead."));

        if (dto.Amount <= 0)
            return Result.Failure<SettlementDto>(new Error("SplitSettlement.InvalidAmount", "Amount must be greater than zero."));

        var limitError = CheckSettlementLimits(group, settlement.FromMember, settlement.ToMember, dto.Amount);
        if (limitError != null)
            return Result.Failure<SettlementDto>(limitError);

        settlement.Amount = dto.Amount;
        settlement.Method = dto.Method;
        settlement.UpiIdUsed = dto.Method == SettlementMethod.Upi ? settlement.ToMember.UpiId : null;
        _context.SplitSettlements.Update(settlement);
        await _context.SaveChangesAsync();

        var sDto = BuildSettlementDto(settlement, settlement.FromMember, settlement.ToMember);
        await _notifier.NotifySettlementRecordedAsync(
            group.Id,
            $"✏️ Payment updated: {settlement.FromMember.Name} → {settlement.ToMember.Name} is now ₹{dto.Amount:N2}.",
            sDto,
            ComputeBalancesDto(group));
        await RecordActivityAsync(group.Id, userId, "PaymentEdited",
            $"changed the payment {settlement.FromMember.Name} → {settlement.ToMember.Name} to ₹{dto.Amount:N2}.");

        return Result.Success(sDto);
    }

    /// <summary>
    /// Cancels a payment in any state (pending, sent, or even completed — "undo this
    /// payment"). Balances revert; a fully settled group re-opens for settling.
    /// </summary>
    public async Task<Result<bool>> DeleteSettlementAsync(string userId, int settlementId)
    {
        var settlement = await _context.SplitSettlements
            .Include(s => s.FromMember)
            .Include(s => s.ToMember)
            .FirstOrDefaultAsync(s => s.Id == settlementId);
        if (settlement == null)
            return Result.Failure<bool>(new Error("SplitSettlement.NotFound", "Settlement not found."));

        var group = await LoadGroupForUserAsync(userId, settlement.SplitGroupId);
        if (group == null)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "You don't have access to this group."));

        if (group.Status == SplitGroupStatus.Archived)
            return Result.Failure<bool>(new Error("SplitSettlement.GroupClosed", "This group is closed — payments can't be changed."));

        if (!CanManageSettlement(group, settlement, userId))
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "Only the admin, the payer or the receiver can delete this payment."));

        var wasCompleted = settlement.Status == SettlementStatus.Completed;
        var text = $"{settlement.FromMember.Name} → {settlement.ToMember.Name} (₹{settlement.Amount:N2})";

        settlement.IsDeleted = true;
        settlement.DeletedAt = DateTime.UtcNow;

        var reopened = group.Status == SplitGroupStatus.Settled && wasCompleted;
        if (reopened)
        {
            group.Status = SplitGroupStatus.Locked;
            _context.SplitGroups.Update(group);
        }

        await _context.SaveChangesAsync();

        if (reopened)
            await _notifier.NotifyGroupStatusChangedAsync(group.Id, $"🗑️ Payment {text} was removed — the group is open for settling again.", SplitGroupStatus.Locked);
        else
            await NotifyGroupUpdatedAsync(group.Id, $"🗑️ Payment {text} was removed.");
        await RecordActivityAsync(group.Id, userId, "PaymentDeleted", $"removed the payment {text}.");

        return Result.Success(true);
    }

    /// <summary>
    /// Payments already sent (awaiting confirmation) count against what is still owed,
    /// so the same debt can't be paid twice, and the receiver must actually be owed
    /// this much. Returns null when the amount is fine.
    /// </summary>
    private static Error? CheckSettlementLimits(SplitGroup group, SplitGroupMember fromMember, SplitGroupMember toMember, decimal amount)
    {
        if (!group.SimplifyDebts)
        {
            // Pairwise groups: you can only pay back the person you actually owe.
            var owedToThem = SplitBalanceCalculator.PairwiseDebts(group)
                .Where(d => d.FromMemberId == fromMember.Id && d.ToMemberId == toMember.Id)
                .Sum(d => d.Amount);
            return amount > owedToThem + 0.01m
                ? new Error("SplitSettlement.ExceedsDebt",
                    $"{fromMember.Name} owes {toMember.Name} ₹{owedToThem:F2} — this payment (₹{amount:F2}) exceeds that.")
                : null;
        }

        var balances = SplitBalanceCalculator.CalculateNetBalances(group);
        var fromEntry = balances.First(b => b.MemberId == fromMember.Id);
        var toEntry = balances.First(b => b.MemberId == toMember.Id);
        var fromMemberOwes = Math.Max(0, -(fromEntry.NetBalance + fromEntry.InTransit));
        var toMemberIsOwed = Math.Max(0, toEntry.NetBalance + toEntry.InTransit);

        if (amount > fromMemberOwes + 0.01m) // small tolerance for rounding
            return new Error("SplitSettlement.ExceedsDebt",
                $"{fromMember.Name} owes ₹{fromMemberOwes:F2} overall — this payment (₹{amount:F2}) exceeds that.");

        if (amount > toMemberIsOwed + 0.01m)
            return new Error("SplitSettlement.ExceedsCredit",
                $"{toMember.Name} is only owed ₹{toMemberIsOwed:F2} overall — this payment (₹{amount:F2}) exceeds that.");

        return null;
    }

    private static bool CanManageSettlement(SplitGroup group, SplitSettlement settlement, string userId)
    {
        if (group.CreatedByUserId == userId) return true;
        var caller = FindMemberForUser(group, userId);
        return caller != null && (caller.Id == settlement.FromMemberId || caller.Id == settlement.ToMemberId);
    }

    private static SettlementDto BuildSettlementDto(SplitSettlement s, SplitGroupMember from, SplitGroupMember to) => new()
    {
        Id = s.Id,
        FromMemberId = from.Id,
        FromMemberName = from.Name,
        ToMemberId = to.Id,
        ToMemberName = to.Name,
        Amount = s.Amount,
        Method = s.Method,
        Status = s.Status,
        PaymentReference = s.PaymentReference,
        CompletedAt = s.CompletedAt,
        CreatedAt = s.CreatedAt
    };

    public async Task<Result<bool>> MarkPaymentSentAsync(string userId, int settlementId)
    {
        var settlement = await _context.SplitSettlements
            .Include(s => s.SplitGroup).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(s => s.Id == settlementId);

        if (settlement == null)
            return Result.Failure<bool>(new Error("SplitSettlement.NotFound", "Settlement not found."));

        if (settlement.Status != SettlementStatus.Pending)
            return Result.Failure<bool>(new Error("SplitSettlement.WrongState", "This settlement has already been acted on."));

        if (settlement.SplitGroup.CreatedByUserId != userId)
        {
            var callerMember = FindMemberForUser(settlement.SplitGroup, userId);
            if (callerMember == null || callerMember.Id != settlement.FromMemberId)
                return Result.Failure<bool>(new Error(
                    "SplitGroup.Forbidden", "Only the person making this payment can mark it as sent."));
        }

        settlement.Status = SettlementStatus.AwaitingConfirmation;
        _context.SplitSettlements.Update(settlement);
        await _context.SaveChangesAsync();

        var fromMemberName = settlement.SplitGroup.Members.FirstOrDefault(m => m.Id == settlement.FromMemberId)?.Name ?? "Payer";
        await NotifyGroupUpdatedAsync(settlement.SplitGroupId, $"📲 {fromMemberName} marked payment of ₹{settlement.Amount:N2} as sent.");
        await RecordActivityAsync(settlement.SplitGroupId, userId, "PaymentSent", $"marked ₹{settlement.Amount:N2} as sent to {settlement.SplitGroup.Members.FirstOrDefault(m => m.Id == settlement.ToMemberId)?.Name ?? "the receiver"}.");

        return Result.Success(true);
    }

    public async Task<Result<bool>> ConfirmPaymentReceivedAsync(string userId, int settlementId)
    {
        var settlement = await _context.SplitSettlements
            .Include(s => s.SplitGroup).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(s => s.Id == settlementId);

        if (settlement == null)
            return Result.Failure<bool>(new Error("SplitSettlement.NotFound", "Settlement not found."));

        if (settlement.Status != SettlementStatus.AwaitingConfirmation)
            return Result.Failure<bool>(new Error("SplitSettlement.WrongState", "This settlement isn't awaiting confirmation."));

        if (settlement.SplitGroup.CreatedByUserId != userId)
        {
            var callerMember = FindMemberForUser(settlement.SplitGroup, userId);
            if (callerMember == null || callerMember.Id != settlement.ToMemberId)
                return Result.Failure<bool>(new Error(
                    "SplitGroup.Forbidden", "Only the person who was owed this payment can confirm receiving it."));
        }

        settlement.Status = SettlementStatus.Completed;
        settlement.CompletedAt = DateTime.UtcNow;
        _context.SplitSettlements.Update(settlement);
        await _context.SaveChangesAsync();

        var toMemberName = settlement.SplitGroup.Members.FirstOrDefault(m => m.Id == settlement.ToMemberId)?.Name ?? "Recipient";
        await NotifyGroupUpdatedAsync(settlement.SplitGroupId, $"✅ {toMemberName} confirmed receipt of ₹{settlement.Amount:N2}.");
        await RecordActivityAsync(settlement.SplitGroupId, userId, "PaymentConfirmed", $"confirmed receiving ₹{settlement.Amount:N2} from {settlement.SplitGroup.Members.FirstOrDefault(m => m.Id == settlement.FromMemberId)?.Name ?? "the payer"}.");

        await CheckAndAutoSettleAsync(settlement.SplitGroupId);

        return Result.Success(true);
    }

    public async Task<Result<bool>> RejectPaymentAsync(string userId, int settlementId)
    {
        var settlement = await _context.SplitSettlements
            .Include(s => s.SplitGroup).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(s => s.Id == settlementId);

        if (settlement == null)
            return Result.Failure<bool>(new Error("SplitSettlement.NotFound", "Settlement not found."));

        if (settlement.Status != SettlementStatus.AwaitingConfirmation)
            return Result.Failure<bool>(new Error("SplitSettlement.WrongState", "This settlement isn't awaiting confirmation."));

        // Same authorization as confirming receipt — only the person
        // who was supposed to receive the money genuinely knows
        // whether it actually arrived.
        if (settlement.SplitGroup.CreatedByUserId != userId)
        {
            var callerMember = FindMemberForUser(settlement.SplitGroup, userId);
            if (callerMember == null || callerMember.Id != settlement.ToMemberId)
                return Result.Failure<bool>(new Error(
                    "SplitGroup.Forbidden", "Only the person who was supposed to receive this payment can reject it."));
        }

        settlement.Status = SettlementStatus.Pending;
        _context.SplitSettlements.Update(settlement);
        await _context.SaveChangesAsync();

        var fromMemberName = settlement.SplitGroup.Members.FirstOrDefault(m => m.Id == settlement.FromMemberId)?.Name ?? "Payer";
        await NotifyGroupUpdatedAsync(settlement.SplitGroupId, $"❌ Payment of ₹{settlement.Amount:N2} from {fromMemberName} wasn't received — marked back as pending.");
        await RecordActivityAsync(settlement.SplitGroupId, userId, "PaymentRejected", $"reported that ₹{settlement.Amount:N2} from {fromMemberName} wasn't received.");

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
            .Include(g => g.Expenses).ThenInclude(e => e.Payers)
            .Include(g => g.Expenses).ThenInclude(e => e.Participants)
            .Include(g => g.Settlements)
            .FirstOrDefaultAsync(g => g.ShareToken == shareToken);

        if (group == null)
            return Result.Failure<PublicGroupViewDto>(new Error("SplitGroup.NotFound", "This link doesn't point to a valid group."));

        var page = await QueryExpensePageAsync(group.Id, new ExpenseListQuery());
        if (page.IsSuccess) HidePrivateFields(page.Value.Items);
        if (!page.IsSuccess)
            return Result.Failure<PublicGroupViewDto>(page.Error);

        return Result.Success(new PublicGroupViewDto
        {
            GroupName = group.Name,
            Currency = group.Currency,
            // UpiId deliberately omitted here — this is a public,
            // no-login view. Payment identifiers should only be visible
            // to someone actually operating the group (the creator),
            // not anyone who happens to have the link.
            Members = group.Members.Select(m => new MemberDto { Id = m.Id, Name = m.Name, UpiId = null, LeftAt = m.LeftAt }).ToList(),
            Expenses = page.Value.Items,
            NextCursor = page.Value.NextCursor,
            TotalExpenseCount = page.Value.TotalCount,
            Balances = ComputeBalancesDto(group)
        });
    }

    public async Task<Result<ExpensePageDto>> GetPublicExpensesAsync(string shareToken, string? cursor, int? limit)
    {
        var groupId = await _context.SplitGroups
            .Where(g => g.ShareToken == shareToken)
            .Select(g => (int?)g.Id)
            .FirstOrDefaultAsync();

        if (groupId == null)
            return Result.Failure<ExpensePageDto>(new Error("SplitGroup.NotFound", "This link doesn't point to a valid group."));

        var publicPage = await QueryExpensePageAsync(groupId.Value, new ExpenseListQuery { Cursor = cursor, Limit = limit });
        if (publicPage.IsSuccess) HidePrivateFields(publicPage.Value.Items);
        return publicPage;
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
            ExpiresAt = dto.ExpiresAt.HasValue ? EnsureUtc(dto.ExpiresAt.Value) : null,
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
            MemberCount = invite.SplitGroup.Members.Count(m => m.LeftAt == null),
            IsValid = isValid,
            InvalidReason = reason,
            UnclaimedMembers = isValid
                ? invite.SplitGroup.Members
                    .Where(m => m.LinkedUserId == null && m.LeftAt == null)
                    .Select(m => new UnclaimedMemberDto { Id = m.Id, Name = m.Name }).ToList()
                : new List<UnclaimedMemberDto>()
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

        if (invite.SplitGroup.IsDirect && !dto.ClaimMemberId.HasValue)
            return Result.Failure<JoinGroupResultDto>(new Error(
                "SplitInvite.DirectLedger", "This is a two-person ledger — pick your name to join it."));

        SplitGroupMember newMember;
        if (dto.ClaimMemberId.HasValue)
        {
            // Join as an existing name-only member: they keep every expense already logged for them.
            var claimed = invite.SplitGroup.Members.FirstOrDefault(m => m.Id == dto.ClaimMemberId.Value);
            if (claimed == null || claimed.LinkedUserId != null || claimed.LeftAt != null)
                return Result.Failure<JoinGroupResultDto>(new Error(
                    "SplitInvite.CannotClaim", "That member has already been claimed — pick someone else or join as new."));
            claimed.LinkedUserId = userId;
            newMember = claimed;
        }
        else
        {
            newMember = new SplitGroupMember
            {
                SplitGroupId = invite.SplitGroupId,
                Name = dto.DisplayName,
                LinkedUserId = userId
            };
            _context.SplitGroupMembers.Add(newMember);
        }

        invite.UsedCount++;
        _context.SplitGroupInvites.Update(invite);
        await _context.SaveChangesAsync();

        await NotifyGroupUpdatedAsync(invite.SplitGroupId, $"🎉 {newMember.Name} joined the group!");
        await RecordActivityAsync(invite.SplitGroupId, userId, dto.ClaimMemberId.HasValue ? "MemberClaimed" : "MemberJoined",
            dto.ClaimMemberId.HasValue ? $"joined the group as {newMember.Name}." : "joined the group.", actorName: newMember.Name);

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

    public async Task<Result<ExpenseDto>> UpdateExpenseAsync(string userId, int expenseId, UpdateExpenseDto dto)
    {
        var expense = await _context.SplitExpenses
            .Include(e => e.Payers)
            .Include(e => e.Participants)
            .Include(e => e.SplitGroup).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(e => e.Id == expenseId);

        if (expense == null)
            return Result.Failure<ExpenseDto>(new Error("SplitExpense.NotFound", "Expense not found."));

        var group = expense.SplitGroup;
        var hasAccess = group.CreatedByUserId == userId || group.Members.Any(m => m.LinkedUserId == userId);
        if (!hasAccess)
            return Result.Failure<ExpenseDto>(new Error("SplitGroup.Forbidden", "You don't have access to this group."));

        if (group.Status != SplitGroupStatus.Active)
            return Result.Failure<ExpenseDto>(new Error(
                "SplitExpense.GroupClosed", "This group is locked, settled, or archived — expenses can't be edited."));

        if (dto.Amount <= 0)
            return Result.Failure<ExpenseDto>(new Error("SplitExpense.InvalidAmount", "Amount must be greater than zero."));

        var payersSum = dto.Payers.Sum(p => p.AmountPaid);
        if (Math.Abs(payersSum - dto.Amount) > 0.01m)
            return Result.Failure<ExpenseDto>(new Error(
                "SplitExpense.PayersMismatch",
                $"Payers add up to {payersSum:F2}, but the expense total is {dto.Amount:F2} — they need to match."));

        var groupMemberIds = group.Members.Select(m => m.Id).ToHashSet();
        var allReferencedIds = dto.Payers.Select(p => p.MemberId).Concat(dto.Participants.Select(p => p.MemberId));
        if (allReferencedIds.Any(id => !groupMemberIds.Contains(id)))
            return Result.Failure<ExpenseDto>(new Error(
                "SplitExpense.MemberNotInGroup", "One or more payers or participants don't belong to this group."));

        // A member who has since left may stay on an expense they were already on,
        // but can't be newly added to it.
        var alreadyOnExpense = expense.Payers.Select(p => p.SplitGroupMemberId)
            .Concat(expense.Participants.Select(p => p.SplitGroupMemberId)).ToHashSet();
        var leftMembers = group.Members.Where(m => m.LeftAt != null).Select(m => m.Id).ToHashSet();
        if (allReferencedIds.Any(id => leftMembers.Contains(id) && !alreadyOnExpense.Contains(id)))
            return Result.Failure<ExpenseDto>(new Error(
                "SplitExpense.MemberLeft",
                "Someone you picked has left the group — they can't be added to this expense."));

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

        var before = new ExpenseSnapshot(
            expense.Description, expense.Amount, expense.Date, expense.Category, expense.SplitType,
            expense.Participants.Select(p => p.SplitGroupMemberId).ToHashSet(),
            PayerKey(expense.Payers.Select(p => (p.SplitGroupMemberId, p.AmountPaid))),
            expense.Notes);

        _context.SplitExpensePayers.RemoveRange(expense.Payers);
        _context.SplitExpenseParticipants.RemoveRange(expense.Participants);

        expense.Notes = NormalizeNotes(dto.Notes);
        expense.Description = dto.Description;
        expense.Amount = dto.Amount;
        expense.Date = EnsureUtc(dto.Date);
        expense.Category = dto.Category;
        expense.SplitType = dto.SplitType;

        foreach (var payer in dto.Payers)
            expense.Payers.Add(new SplitExpensePayer { SplitGroupMemberId = payer.MemberId, AmountPaid = payer.AmountPaid });

        foreach (var (memberId, share) in shares)
        {
            expense.Participants.Add(new SplitExpenseParticipant
            {
                SplitGroupMemberId = memberId,
                ShareAmount = share,
                SplitValue = SplitValueFor(dto.SplitType, dto.Participants.First(p => p.MemberId == memberId))
            });
        }

        await _context.SaveChangesAsync();

        var dtoRes = await GetExpenseDtoAsync(expense.Id);
        var updatedGroup = await _context.SplitGroups
            .AsNoTracking()
            .Include(g => g.Members)
            .Include(g => g.Expenses).ThenInclude(e => e.Payers)
            .Include(g => g.Expenses).ThenInclude(e => e.Participants)
            .Include(g => g.Settlements)
            .FirstOrDefaultAsync(g => g.Id == expense.SplitGroupId);

        var balances = updatedGroup != null ? ComputeBalancesDto(updatedGroup) : new GroupBalancesDto();
        await _notifier.NotifyExpenseUpdatedAsync(expense.SplitGroupId, $"Expense '{dto.Description}' (₹{dto.Amount:N2}) updated.", dtoRes.Value, balances);
        await RecordActivityAsync(expense.SplitGroupId, userId, "ExpenseEdited", DescribeExpenseEdit(before, dto), expense.Id);

        return dtoRes;
    }

    public async Task<Result<DeleteExpenseResultDto>> DeleteExpenseAsync(string userId, int expenseId)
    {
        var expense = await _context.SplitExpenses
            .Include(e => e.SplitGroup).ThenInclude(g => g.Members)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.Id == expenseId);

        if (expense == null)
            return Result.Failure<DeleteExpenseResultDto>(new Error("SplitExpense.NotFound", "Expense not found."));

        var group = expense.SplitGroup;
        var hasAccess = group.CreatedByUserId == userId || group.Members.Any(m => m.LinkedUserId == userId);
        if (!hasAccess)
            return Result.Failure<DeleteExpenseResultDto>(new Error("SplitGroup.Forbidden", "You don't have access to this group."));

        if (group.Status != SplitGroupStatus.Active)
            return Result.Failure<DeleteExpenseResultDto>(new Error(
                "SplitExpense.GroupClosed", "This group is locked, settled, or archived — expenses can't be deleted."));

        int groupId = expense.SplitGroupId;
        string desc = expense.Description;
        decimal amt = expense.Amount;
        bool wasAlreadyImported = expense.Participants.Any(p => p.ImportedTransactionId != null);

        expense.IsDeleted = true;
        expense.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var updatedGroup = await _context.SplitGroups
            .AsNoTracking()
            .Include(g => g.Members)
            .Include(g => g.Expenses).ThenInclude(e => e.Payers)
            .Include(g => g.Expenses).ThenInclude(e => e.Participants)
            .Include(g => g.Settlements)
            .FirstOrDefaultAsync(g => g.Id == groupId);

        var balances = updatedGroup != null ? ComputeBalancesDto(updatedGroup) : new GroupBalancesDto();

        await _notifier.NotifyExpenseDeletedAsync(
            groupId,
            $"Expense '{desc}' (₹{amt:N2}) deleted.",
            expenseId,
            balances);
        await RecordActivityAsync(groupId, userId, "ExpenseDeleted", $"deleted '{desc}' (₹{amt:N2}).", expenseId);

        return Result.Success(new DeleteExpenseResultDto { WasAlreadyImported = wasAlreadyImported });
    }

    public async Task<Result<bool>> LockGroupAsync(string userId, int groupId)
    {
        var group = await _context.SplitGroups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null)
            return Result.Failure<bool>(new Error("SplitGroup.NotFound", "Group not found."));

        if (group.CreatedByUserId != userId)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "Only the group creator can lock the group."));

        if (group.Status != SplitGroupStatus.Active)
            return Result.Failure<bool>(new Error("SplitGroup.WrongState", "Only an active group can be locked."));

        group.Status = SplitGroupStatus.Locked;
        _context.SplitGroups.Update(group);
        await _context.SaveChangesAsync();

        await _notifier.NotifyGroupStatusChangedAsync(groupId, $"🔒 Group has been locked.", SplitGroupStatus.Locked);
        await RecordActivityAsync(groupId, userId, "GroupLocked", "locked the group.");

        return Result.Success(true);
    }

    public async Task<Result<bool>> UnlockGroupAsync(string userId, int groupId)
    {
        var group = await _context.SplitGroups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null)
            return Result.Failure<bool>(new Error("SplitGroup.NotFound", "Group not found."));

        if (group.CreatedByUserId != userId)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "Only the group creator can unlock the group."));

        group.Status = SplitGroupStatus.Active;
        _context.SplitGroups.Update(group);
        await _context.SaveChangesAsync();

        await _notifier.NotifyGroupStatusChangedAsync(groupId, $"🔓 Group has been unlocked.", SplitGroupStatus.Active);
        await RecordActivityAsync(groupId, userId, "GroupUnlocked", "unlocked the group.");

        return Result.Success(true);
    }

    public async Task<Result<bool>> CloseGroupAsync(string userId, int groupId)
    {
        var group = await _context.SplitGroups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null)
            return Result.Failure<bool>(new Error("SplitGroup.NotFound", "Group not found."));

        if (group.CreatedByUserId != userId)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "Only the group creator can close the group."));

        group.Status = SplitGroupStatus.Archived;
        _context.SplitGroups.Update(group);
        await _context.SaveChangesAsync();

        await _notifier.NotifyGroupStatusChangedAsync(groupId, $"📁 Group has been closed.", SplitGroupStatus.Archived);
        await RecordActivityAsync(groupId, userId, "GroupClosed", "closed the group.");

        return Result.Success(true);
    }

    public async Task<Result<GroupDto>> UpdateGroupAsync(string userId, UpdateGroupDto dto)
    {
        var group = await _context.SplitGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == dto.GroupId);

        if (group == null)
            return Result.Failure<GroupDto>(new Error("SplitGroup.NotFound", "Group not found."));

        if (group.CreatedByUserId != userId)
            return Result.Failure<GroupDto>(new Error("SplitGroup.Forbidden", "Only the group creator can edit trip details."));

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure<GroupDto>(new Error("SplitGroup.InvalidName", "Trip name cannot be empty."));

        group.Name = dto.Name.Trim();
        _context.SplitGroups.Update(group);
        await _context.SaveChangesAsync();

        await _notifier.NotifyGroupUpdatedAsync(group.Id, $"✏️ Trip name updated to '{group.Name}'.");
        await RecordActivityAsync(group.Id, userId, "GroupRenamed", $"renamed the trip to '{group.Name}'.");

        return Result.Success(MapToDto(group));
    }

    public async Task<Result<MemberDto>> RenameMemberAsync(string userId, RenameMemberDto dto)
    {
        var member = await _context.SplitGroupMembers
            .Include(m => m.SplitGroup).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(m => m.Id == dto.MemberId);

        if (member == null)
            return Result.Failure<MemberDto>(new Error("SplitMember.NotFound", "Member not found."));

        var isGroupCreator = member.SplitGroup.CreatedByUserId == userId;
        var isSelf = member.LinkedUserId == userId;
        var isUnlinked = member.LinkedUserId == null;

        if (!isGroupCreator && !isSelf && !isUnlinked)
            return Result.Failure<MemberDto>(new Error("SplitGroup.Forbidden", "You can only rename yourself or unlinked members unless you are the admin."));

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure<MemberDto>(new Error("SplitMember.InvalidName", "Member name cannot be empty."));

        var oldName = member.Name;
        member.Name = dto.Name.Trim();
        _context.SplitGroupMembers.Update(member);
        await _context.SaveChangesAsync();

        await _notifier.NotifyGroupUpdatedAsync(member.SplitGroupId, $"👤 {oldName} was renamed to '{member.Name}'.");
        await RecordActivityAsync(member.SplitGroupId, userId, "MemberRenamed", $"renamed {oldName} to '{member.Name}'.");

        return Result.Success(new MemberDto { Id = member.Id, Name = member.Name, LinkedUserId = member.LinkedUserId, UpiId = member.UpiId });
    }

    /// <summary>
    /// Leave the group (yourself) or remove someone (admin). Only possible once the
    /// member is fully settled up, like Splitwise. The member's row is kept so old
    /// expenses still show their name.
    /// </summary>
    public async Task<Result<bool>> RemoveMemberAsync(string userId, int memberId)
    {
        var groupId = await _context.SplitGroupMembers
            .Where(m => m.Id == memberId)
            .Select(m => (int?)m.SplitGroupId)
            .FirstOrDefaultAsync();
        if (groupId == null)
            return Result.Failure<bool>(new Error("SplitMember.NotFound", "Member not found."));

        var group = await LoadGroupForUserAsync(userId, groupId.Value);
        if (group == null)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "You don't have access to this group."));

        var member = group.Members.FirstOrDefault(m => m.Id == memberId);
        if (member == null)
            return Result.Failure<bool>(new Error("SplitMember.NotFound", "Member not found."));

        var isAdmin = group.CreatedByUserId == userId;
        var isSelf = member.LinkedUserId != null && member.LinkedUserId == userId;
        if (!isAdmin && !isSelf)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "Only the group creator can remove other members."));

        if (member.LinkedUserId != null && member.LinkedUserId == group.CreatedByUserId)
            return Result.Failure<bool>(new Error("SplitMember.CreatorCannotLeave", "The group creator can't leave — delete the group instead."));

        if (group.Status == SplitGroupStatus.Archived)
            return Result.Failure<bool>(new Error("SplitMember.GroupClosed", "This group is closed."));

        var entry = SplitBalanceCalculator.CalculateNetBalances(group).First(b => b.MemberId == memberId);
        if (Math.Abs(entry.NetBalance) >= 0.01m || Math.Abs(entry.InTransit) >= 0.01m)
        {
            var owes = entry.NetBalance + entry.InTransit;
            return Result.Failure<bool>(new Error(
                "SplitMember.HasBalance",
                $"{member.Name} isn't settled up yet ({(owes < 0 ? "owes" : "is owed")} ₹{Math.Abs(owes):N2}). Settle up first, then they can leave."));
        }

        if (!group.SimplifyDebts && SplitBalanceCalculator.PairwiseDebts(group)
                .Any(d => d.FromMemberId == memberId || d.ToMemberId == memberId))
        {
            return Result.Failure<bool>(new Error(
                "SplitMember.HasBalance",
                $"{member.Name} still has unsettled payments with other members. Settle up first, then they can leave."));
        }

        // Any payment requests that were never sent just get cancelled.
        foreach (var s in group.Settlements
                     .Where(s => s.Status == SettlementStatus.Pending &&
                                 (s.FromMemberId == memberId || s.ToMemberId == memberId))
                     .ToList())
        {
            s.IsDeleted = true;
            s.DeletedAt = DateTime.UtcNow;
        }

        member.LeftAt = DateTime.UtcNow;
        member.LinkedUserId = null; // loses access; the same person can rejoin via an invite later
        _context.SplitGroupMembers.Update(member);
        await _context.SaveChangesAsync();

        await _notifier.NotifyGroupUpdatedAsync(group.Id,
            isSelf ? $"👋 {member.Name} left the group." : $"👋 {member.Name} was removed from the group.");
        await RecordActivityAsync(group.Id, userId, isSelf ? "MemberLeft" : "MemberRemoved",
            isSelf ? "left the group." : $"removed {member.Name} from the group.",
            actorName: isSelf ? member.Name : null);

        return Result.Success(true);
    }

    /// <summary>Creator only. Soft delete — the group, its expenses and share link disappear for everyone.</summary>
    public async Task<Result<bool>> DeleteGroupAsync(string userId, int groupId)
    {
        var group = await _context.SplitGroups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null)
            return Result.Failure<bool>(new Error("SplitGroup.NotFound", "Group not found."));

        if (group.CreatedByUserId != userId)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "Only the group creator can delete the group."));

        group.IsDeleted = true;
        group.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await _notifier.NotifyGroupDeletedAsync(groupId, $"🗑️ '{group.Name}' was deleted by the group creator.");

        return Result.Success(true);
    }

    // ── History, undo, receipts, global search ────────────────────────────────

    public async Task<Result<ActivityPageDto>> GetActivityAsync(string userId, int groupId, string? cursor, int? limit)
    {
        var hasAccess = await _context.SplitGroups.AnyAsync(g =>
            g.Id == groupId && (g.CreatedByUserId == userId || g.Members.Any(m => m.LinkedUserId == userId)));
        if (!hasAccess)
            return Result.Failure<ActivityPageDto>(new Error("SplitGroup.NotFound", "Group not found."));

        var size = Math.Clamp(limit ?? 30, 1, 100);
        var query = _context.SplitActivities.AsNoTracking().Where(a => a.SplitGroupId == groupId);
        if (int.TryParse(cursor, out var beforeId))
            query = query.Where(a => a.Id < beforeId);

        var rows = await query.OrderByDescending(a => a.Id).Take(size + 1).ToListAsync();
        var hasMore = rows.Count > size;
        var page = hasMore ? rows.Take(size).ToList() : rows;

        return Result.Success(new ActivityPageDto
        {
            Items = page.Select(a => new ActivityDto
            {
                Id = a.Id,
                ActorName = a.ActorName,
                Type = a.Type,
                Message = a.Message,
                ExpenseId = a.SplitExpenseId,
                CreatedAt = a.CreatedAt
            }).ToList(),
            NextCursor = hasMore ? page[^1].Id.ToString() : null
        });
    }

    /// <summary>Recently deleted expenses (newest first) — the "undo" list.</summary>
    public async Task<Result<List<ExpenseDto>>> GetDeletedExpensesAsync(string userId, int groupId)
    {
        var hasAccess = await _context.SplitGroups.AnyAsync(g =>
            g.Id == groupId && (g.CreatedByUserId == userId || g.Members.Any(m => m.LinkedUserId == userId)));
        if (!hasAccess)
            return Result.Failure<List<ExpenseDto>>(new Error("SplitGroup.NotFound", "Group not found."));

        var rows = await _context.SplitExpenses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(e => e.Payers).ThenInclude(p => p.SplitGroupMember)
            .Include(e => e.Participants).ThenInclude(p => p.SplitGroupMember)
            .Where(e => e.SplitGroupId == groupId && e.IsDeleted)
            .OrderByDescending(e => e.DeletedAt)
            .Take(100)
            .ToListAsync();

        // IgnoreQueryFilters also lifts the filter on the lines — keep only the live ones.
        foreach (var e in rows)
        {
            e.Payers = e.Payers.Where(p => !p.IsDeleted).ToList();
            e.Participants = e.Participants.Where(p => !p.IsDeleted).ToList();
        }

        return Result.Success(rows.Select(r =>
        {
            var d = MapExpenseToDto(r);
            d.DeletedAt = r.DeletedAt;
            return d;
        }).ToList());
    }

    public async Task<Result<ExpenseDto>> RestoreExpenseAsync(string userId, int expenseId)
    {
        var expense = await _context.SplitExpenses
            .IgnoreQueryFilters()
            .Include(e => e.Payers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.Id == expenseId && e.IsDeleted);
        if (expense == null)
            return Result.Failure<ExpenseDto>(new Error("SplitExpense.NotFound", "That deleted expense wasn't found."));

        var group = await LoadGroupForUserAsync(userId, expense.SplitGroupId);
        if (group == null)
            return Result.Failure<ExpenseDto>(new Error("SplitGroup.Forbidden", "You don't have access to this group."));

        if (group.Status != SplitGroupStatus.Active)
            return Result.Failure<ExpenseDto>(new Error(
                "SplitExpense.GroupClosed", "This group is locked, settled, or archived — expenses can't be restored."));

        var involved = expense.Payers.Where(p => !p.IsDeleted).Select(p => p.SplitGroupMemberId)
            .Concat(expense.Participants.Where(p => !p.IsDeleted).Select(p => p.SplitGroupMemberId))
            .ToHashSet();
        var left = group.Members.Where(m => m.LeftAt != null && involved.Contains(m.Id)).Select(m => m.Name).ToList();
        if (left.Count > 0)
            return Result.Failure<ExpenseDto>(new Error(
                "SplitExpense.MemberLeft",
                $"{string.Join(", ", left)} has left the group, so this expense can't be restored. Add it again as a new expense instead."));

        expense.IsDeleted = false;
        expense.DeletedAt = null;
        await _context.SaveChangesAsync();

        var dtoRes = await GetExpenseDtoAsync(expense.Id);
        var balances = await BuildBalancesAsync(expense.SplitGroupId);
        await _notifier.NotifyExpenseAddedAsync(expense.SplitGroupId,
            $"Expense '{expense.Description}' (₹{expense.Amount:N2}) restored.", dtoRes.Value, balances);
        await RecordActivityAsync(expense.SplitGroupId, userId, "ExpenseRestored",
            $"restored '{expense.Description}' (₹{expense.Amount:N2}).", expense.Id);

        return dtoRes;
    }

    /// <summary>
    /// Points an expense at a newly uploaded receipt (or clears it with null). Returns the
    /// previous file path so the caller can delete the old file from disk.
    /// </summary>
    public async Task<Result<ReceiptChangeDto>> SetReceiptAsync(string userId, int expenseId, string? receiptPath)
    {
        var expense = await _context.SplitExpenses
            .Include(e => e.SplitGroup).ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(e => e.Id == expenseId);
        if (expense == null)
            return Result.Failure<ReceiptChangeDto>(new Error("SplitExpense.NotFound", "Expense not found."));

        var group = expense.SplitGroup;
        var hasAccess = group.CreatedByUserId == userId || group.Members.Any(m => m.LinkedUserId == userId);
        if (!hasAccess)
            return Result.Failure<ReceiptChangeDto>(new Error("SplitGroup.Forbidden", "You don't have access to this group."));

        if (group.Status == SplitGroupStatus.Archived)
            return Result.Failure<ReceiptChangeDto>(new Error("SplitExpense.GroupClosed", "This group is closed."));

        var previous = expense.ReceiptPath;
        expense.ReceiptPath = receiptPath;
        await _context.SaveChangesAsync();

        var dtoRes = await GetExpenseDtoAsync(expense.Id);
        var balances = await BuildBalancesAsync(group.Id);
        await _notifier.NotifyExpenseUpdatedAsync(group.Id,
            receiptPath == null ? $"Receipt removed from '{expense.Description}'." : $"Receipt added to '{expense.Description}'.",
            dtoRes.Value, balances);
        await RecordActivityAsync(group.Id, userId, "ReceiptChanged",
            receiptPath == null ? $"removed the receipt from '{expense.Description}'." : $"attached a receipt to '{expense.Description}'.",
            expense.Id);

        return Result.Success(new ReceiptChangeDto { PreviousPath = previous, CurrentPath = receiptPath });
    }

    /// <summary>Search expenses by description, note, category or payer across every group the user is in.</summary>
    public async Task<Result<List<GlobalExpenseHitDto>>> SearchExpensesAcrossGroupsAsync(string userId, string query, int? limit)
    {
        var term = query?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(term) || term.Length < 2)
            return Result.Success(new List<GlobalExpenseHitDto>());

        var take = Math.Clamp(limit ?? 30, 1, 100);

        var found = await _context.SplitExpenses
            .AsNoTracking()
            .Where(e => e.SplitGroup.CreatedByUserId == userId || e.SplitGroup.Members.Any(m => m.LinkedUserId == userId))
            .Where(e =>
                e.Description.ToLower().Contains(term) ||
                (e.Notes != null && e.Notes.ToLower().Contains(term)) ||
                (e.Category != null && e.Category.ToLower().Contains(term)) ||
                e.Payers.Any(p => p.SplitGroupMember.Name.ToLower().Contains(term)))
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Take(take)
            .Select(e => new
            {
                e.Id,
                e.SplitGroupId,
                GroupName = e.SplitGroup.Name,
                Currency = e.SplitGroup.Currency,
                e.Description,
                e.Amount,
                e.Date,
                e.Category,
                Payers = e.Payers.Select(p => p.SplitGroupMember.Name).ToList()
            })
            .ToListAsync();

        var rows = found.Select(f => new GlobalExpenseHitDto
        {
            ExpenseId = f.Id,
            GroupId = f.SplitGroupId,
            GroupName = f.GroupName,
            Currency = f.Currency,
            Description = f.Description,
            Amount = f.Amount,
            Date = f.Date,
            Category = f.Category,
            PaidBy = string.Join(", ", f.Payers)
        }).ToList();

        return Result.Success(rows);
    }

    private async Task<GroupBalancesDto> BuildBalancesAsync(int groupId)
    {
        var updatedGroup = await _context.SplitGroups
            .AsNoTracking()
            .Include(g => g.Members)
            .Include(g => g.Expenses).ThenInclude(e => e.Payers)
            .Include(g => g.Expenses).ThenInclude(e => e.Participants)
            .Include(g => g.Settlements)
            .FirstOrDefaultAsync(g => g.Id == groupId);

        return updatedGroup != null ? ComputeBalancesDto(updatedGroup) : new GroupBalancesDto();
    }

    /// <summary>
    /// Appends one line to the group's history. The actor's name is looked up from
    /// their member record unless given. Never throws — the history must not be able
    /// to break the action it describes.
    /// </summary>
    private async Task RecordActivityAsync(
        int groupId, string? userId, string type, string message, int? expenseId = null, string? actorName = null)
    {
        try
        {
            if (actorName == null && userId != null)
            {
                actorName = await _context.SplitGroupMembers
                    .Where(m => m.SplitGroupId == groupId && m.LinkedUserId == userId)
                    .Select(m => m.Name)
                    .FirstOrDefaultAsync();
            }

            var currency = await _context.SplitGroups
                .Where(g => g.Id == groupId).Select(g => g.Currency).FirstOrDefaultAsync();
            if (currency != null && currency != "INR")
                message = message.Replace("₹", CurrencySymbol(currency));

            _context.SplitActivities.Add(new SplitActivity
            {
                SplitGroupId = groupId,
                ActorUserId = userId,
                ActorName = actorName ?? "Someone",
                Type = type,
                Message = message,
                SplitExpenseId = expenseId
            });
            await _context.SaveChangesAsync();
        }
        catch (Exception)
        {
            // intentionally swallowed
        }
    }

    private sealed record ExpenseSnapshot(
        string Description, decimal Amount, DateTime Date, string? Category, SplitType SplitType,
        HashSet<int> Participants, string PayerKey, string? Notes);

    private static string PayerKey(IEnumerable<(int MemberId, decimal Amount)> payers)
        => string.Join(",", payers.OrderBy(p => p.MemberId).Select(p => $"{p.MemberId}:{p.Amount:F2}"));

    private static string? NormalizeNotes(string? notes)
    {
        var trimmed = notes?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > 1000 ? trimmed[..1000] : trimmed;
    }

    /// <summary>"edited 'Dinner' — amount ₹800 → ₹900, date 3 Oct 2026 → 4 Oct 2026"</summary>
    private static string DescribeExpenseEdit(ExpenseSnapshot before, UpdateExpenseDto dto)
    {
        var changes = new List<string>();

        if (!string.Equals(before.Description, dto.Description, StringComparison.Ordinal))
            changes.Add($"name '{before.Description}' → '{dto.Description}'");
        if (before.Amount != dto.Amount)
            changes.Add($"amount ₹{before.Amount:N2} → ₹{dto.Amount:N2}");
        if (before.Date.Date != dto.Date.Date)
            changes.Add($"date {before.Date:d MMM yyyy} → {dto.Date:d MMM yyyy}");

        var oldCategory = string.IsNullOrWhiteSpace(before.Category) ? "General" : before.Category;
        var newCategory = string.IsNullOrWhiteSpace(dto.Category) ? "General" : dto.Category;
        if (!string.Equals(oldCategory, newCategory, StringComparison.OrdinalIgnoreCase))
            changes.Add($"category {oldCategory} → {newCategory}");

        if (before.SplitType != dto.SplitType)
            changes.Add($"split type {before.SplitType} → {dto.SplitType}");

        var newParticipants = dto.Participants.Select(p => p.MemberId).ToHashSet();
        if (!before.Participants.SetEquals(newParticipants))
            changes.Add($"split between {before.Participants.Count} → {newParticipants.Count} people");

        if (before.PayerKey != PayerKey(dto.Payers.Select(p => (p.MemberId, p.AmountPaid))))
            changes.Add("who paid");

        if ((before.Notes ?? string.Empty) != (NormalizeNotes(dto.Notes) ?? string.Empty))
            changes.Add("note");

        return $"edited '{dto.Description}'" + (changes.Count > 0 ? " — " + string.Join(", ", changes) : string.Empty);
    }

    // ── Group settings, claiming and merging members ─────────────────────────

    private static readonly Dictionary<string, string> CurrencySymbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["INR"] = "₹", ["USD"] = "$", ["EUR"] = "€", ["GBP"] = "£", ["JPY"] = "¥", ["AED"] = "AED ",
        ["SGD"] = "S$", ["AUD"] = "A$", ["CAD"] = "C$", ["CHF"] = "CHF ", ["THB"] = "฿", ["MYR"] = "RM ",
        ["IDR"] = "Rp ", ["LKR"] = "Rs ", ["NPR"] = "Rs ", ["BDT"] = "৳", ["NZD"] = "NZ$", ["ZAR"] = "R "
    };

    private static string CurrencySymbol(string code)
        => CurrencySymbols.TryGetValue(code, out var symbol) ? symbol : code + " ";

    public async Task<Result<GroupDto>> UpdateGroupSettingsAsync(string userId, UpdateGroupSettingsDto dto)
    {
        var group = await _context.SplitGroups
            .Include(g => g.Members)
            .Include(g => g.Expenses)
            .FirstOrDefaultAsync(g => g.Id == dto.GroupId);
        if (group == null)
            return Result.Failure<GroupDto>(new Error("SplitGroup.NotFound", "Group not found."));
        if (group.CreatedByUserId != userId)
            return Result.Failure<GroupDto>(new Error("SplitGroup.Forbidden", "Only the group creator can change group settings."));
        if (group.Status == SplitGroupStatus.Archived)
            return Result.Failure<GroupDto>(new Error("SplitGroup.Closed", "This group is closed."));

        var currency = dto.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!CurrencySymbols.ContainsKey(currency))
            return Result.Failure<GroupDto>(new Error("SplitGroup.InvalidCurrency", "That currency isn't supported."));

        if (dto.StartDate.HasValue && dto.EndDate.HasValue && dto.EndDate.Value.Date < dto.StartDate.Value.Date)
            return Result.Failure<GroupDto>(new Error("SplitGroup.InvalidDates", "The end date can't be before the start date."));

        var changes = new List<string>();
        if (group.Currency != currency) changes.Add($"currency {group.Currency} → {currency}");
        if (group.SimplifyDebts != dto.SimplifyDebts) changes.Add(dto.SimplifyDebts ? "simplify debts on" : "simplify debts off");
        var start = dto.StartDate.HasValue ? EnsureUtc(dto.StartDate.Value.Date) : (DateTime?)null;
        var end = dto.EndDate.HasValue ? EnsureUtc(dto.EndDate.Value.Date) : (DateTime?)null;
        if (group.StartDate != start || group.EndDate != end) changes.Add("trip dates");

        group.Currency = currency;
        group.SimplifyDebts = dto.SimplifyDebts;
        group.StartDate = start;
        group.EndDate = end;
        await _context.SaveChangesAsync();

        await NotifyGroupUpdatedAsync(group.Id, "⚙️ Group settings were updated.");
        if (changes.Count > 0)
            await RecordActivityAsync(group.Id, userId, "SettingsChanged", "changed " + string.Join(", ", changes) + ".");

        return Result.Success(MapToDto(group));
    }

    /// <summary>
    /// Folds a name-only member into another: the source's payments, shares and
    /// settlements all move to the target, then the source disappears. Use it when
    /// the same person was added twice ("Rahul" and "Rahul K").
    /// </summary>
    public async Task<Result<bool>> MergeMembersAsync(string userId, int sourceMemberId, int targetMemberId)
    {
        if (sourceMemberId == targetMemberId)
            return Result.Failure<bool>(new Error("SplitMember.SameMember", "Choose two different members."));

        var source = await _context.SplitGroupMembers.FirstOrDefaultAsync(m => m.Id == sourceMemberId);
        var target = await _context.SplitGroupMembers.FirstOrDefaultAsync(m => m.Id == targetMemberId);
        if (source == null || target == null || source.SplitGroupId != target.SplitGroupId)
            return Result.Failure<bool>(new Error("SplitMember.NotFound", "Both members must be in the same group."));

        var group = await _context.SplitGroups.FirstAsync(g => g.Id == source.SplitGroupId);
        if (group.CreatedByUserId != userId)
            return Result.Failure<bool>(new Error("SplitGroup.Forbidden", "Only the group creator can merge members."));
        if (group.Status == SplitGroupStatus.Archived)
            return Result.Failure<bool>(new Error("SplitGroup.Closed", "This group is closed."));
        if (source.LinkedUserId != null)
            return Result.Failure<bool>(new Error("SplitMember.Linked", $"{source.Name} has a FinPlanner account linked and can't be merged away. Merge the name-only one into them instead."));
        if (source.LeftAt != null || target.LeftAt != null)
            return Result.Failure<bool>(new Error("SplitMember.Left", "Members who have left can't be merged."));

        // Payers and participants: combine where both are on the same expense, move otherwise.
        var payers = await _context.SplitExpensePayers
            .Where(p => p.SplitGroupMemberId == source.Id || p.SplitGroupMemberId == target.Id).ToListAsync();
        foreach (var byExpense in payers.GroupBy(p => p.SplitExpenseId))
        {
            var src = byExpense.FirstOrDefault(p => p.SplitGroupMemberId == source.Id);
            if (src == null) continue;
            var tgt = byExpense.FirstOrDefault(p => p.SplitGroupMemberId == target.Id);
            if (tgt == null) { src.SplitGroupMemberId = target.Id; continue; }
            tgt.AmountPaid += src.AmountPaid;
            src.IsDeleted = true;
            src.DeletedAt = DateTime.UtcNow;
        }

        var participants = await _context.SplitExpenseParticipants
            .Where(p => p.SplitGroupMemberId == source.Id || p.SplitGroupMemberId == target.Id).ToListAsync();
        foreach (var byExpense in participants.GroupBy(p => p.SplitExpenseId))
        {
            var src = byExpense.FirstOrDefault(p => p.SplitGroupMemberId == source.Id);
            if (src == null) continue;
            var tgt = byExpense.FirstOrDefault(p => p.SplitGroupMemberId == target.Id);
            if (tgt == null) { src.SplitGroupMemberId = target.Id; continue; }
            tgt.ShareAmount += src.ShareAmount;
            if (src.SplitValue.HasValue) tgt.SplitValue = (tgt.SplitValue ?? 0) + src.SplitValue.Value;
            src.IsDeleted = true;
            src.DeletedAt = DateTime.UtcNow;
        }

        // Settlements: re-point; one between the two of them is now a payment to oneself — drop it.
        var settlements = await _context.SplitSettlements
            .Where(s => s.SplitGroupId == group.Id &&
                        (s.FromMemberId == source.Id || s.ToMemberId == source.Id)).ToListAsync();
        foreach (var settlement in settlements)
        {
            if (settlement.FromMemberId == source.Id) settlement.FromMemberId = target.Id;
            if (settlement.ToMemberId == source.Id) settlement.ToMemberId = target.Id;
            if (settlement.FromMemberId == settlement.ToMemberId)
            {
                settlement.IsDeleted = true;
                settlement.DeletedAt = DateTime.UtcNow;
            }
        }

        if (string.IsNullOrWhiteSpace(target.UpiId) && !string.IsNullOrWhiteSpace(source.UpiId))
            target.UpiId = source.UpiId;

        source.IsDeleted = true;
        source.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await NotifyGroupUpdatedAsync(group.Id, $"🔀 {source.Name} was merged into {target.Name}.");
        await RecordActivityAsync(group.Id, userId, "MembersMerged", $"merged {source.Name} into {target.Name}.");

        return Result.Success(true);
    }

    // ── Friends and the cross-group dashboard ────────────────────────────────

    public async Task<Result<GroupDto>> CreateFriendAsync(string userId, CreateFriendDto dto)
    {
        var friend = dto.FriendName?.Trim() ?? string.Empty;
        var me = dto.MyName?.Trim() ?? string.Empty;
        if (friend.Length == 0 || me.Length == 0)
            return Result.Failure<GroupDto>(new Error("SplitFriend.InvalidName", "Enter your friend's name."));
        if (friend.Length > 100)
            return Result.Failure<GroupDto>(new Error("SplitFriend.InvalidName", "That name is too long."));

        var exists = await _context.SplitGroups.AnyAsync(g =>
            g.IsDirect && g.Status != SplitGroupStatus.Archived &&
            g.Members.Any(m => m.LinkedUserId == userId) &&
            g.Members.Any(m => m.LinkedUserId != userId && m.LeftAt == null && m.Name.ToLower() == friend.ToLower()));
        if (exists)
            return Result.Failure<GroupDto>(new Error("SplitFriend.Exists", $"You already have {friend} as a friend."));

        var group = new SplitGroup
        {
            Name = friend,
            CreatedByUserId = userId,
            ShareToken = GenerateShareToken(),
            IsDirect = true
        };
        group.Members.Add(new SplitGroupMember { Name = me, LinkedUserId = userId });
        group.Members.Add(new SplitGroupMember { Name = friend });

        _context.SplitGroups.Add(group);
        await _context.SaveChangesAsync();
        return Result.Success(MapToDto(group));
    }

    private async Task<List<SplitGroup>> LoadMyGroupsForDashboardAsync(string userId)
        => await _context.SplitGroups
            .Include(g => g.Members)
            .Include(g => g.Expenses).ThenInclude(e => e.Payers)
            .Include(g => g.Expenses).ThenInclude(e => e.Participants)
            .Include(g => g.Settlements)
            .Where(g => g.Status != SplitGroupStatus.Archived && g.Members.Any(m => m.LinkedUserId == userId && m.LeftAt == null))
            .ToListAsync();

    public async Task<Result<SplitDashboardDto>> GetDashboardAsync(string userId)
    {
        var groups = await LoadMyGroupsForDashboardAsync(userId);
        return Result.Success(SplitDashboardBuilder.Build(userId, groups));
    }

    /// <summary>
    /// Records "we're square" with one person in one currency: one payment per group where a
    /// debt exists, each in the direction the money is owed. Recorded by the person owed it is
    /// completed straight away; recorded by the one paying it waits for the receiver to confirm.
    /// </summary>
    public async Task<Result<SettleWithPersonResultDto>> SettleWithPersonAsync(string userId, SettleWithPersonDto dto)
    {
        var groups = await LoadMyGroupsForDashboardAsync(userId);
        var debts = SplitDashboardBuilder.DebtsWith(userId, groups, dto.PersonKey, dto.Currency);
        if (debts.Count == 0)
            return Result.Failure<SettleWithPersonResultDto>(new Error(
                "SplitSettlement.NothingToSettle", "There's nothing to settle with this person in that currency."));

        var net = 0m;
        foreach (var debt in debts)
        {
            var group = groups.First(g => g.Id == debt.GroupId);
            var me = group.Members.First(m => m.LinkedUserId == userId && m.LeftAt == null);
            net += debt.ToMemberId == me.Id ? debt.Amount : -debt.Amount;

            var method = dto.Method;
            if (method == SettlementMethod.Upi && !string.Equals(group.Currency, "INR", StringComparison.OrdinalIgnoreCase))
                method = SettlementMethod.Other;

            var result = await CreateSettlementAsync(userId, new CreateSettlementDto
            {
                GroupId = debt.GroupId,
                FromMemberId = debt.FromMemberId,
                ToMemberId = debt.ToMemberId,
                Amount = debt.Amount,
                Method = method,
                AlreadyPaid = true
            });
            if (!result.IsSuccess)
                return Result.Failure<SettleWithPersonResultDto>(result.Error);
        }

        return Result.Success(new SettleWithPersonResultDto { PaymentsRecorded = debts.Count, Net = Math.Round(net, 2) });
    }

    private async Task CheckAndAutoSettleAsync(int groupId)
    {
        var group = await _context.SplitGroups
            .Include(g => g.Members)
            .Include(g => g.Expenses).ThenInclude(e => e.Payers)
            .Include(g => g.Expenses).ThenInclude(e => e.Participants)
            .Include(g => g.Settlements)
            .FirstOrDefaultAsync(g => g.Id == groupId);

        if (group == null || group.Status != SplitGroupStatus.Locked) return;

        var balances = SplitBalanceCalculator.CalculateNetBalances(group);
        bool allSettled = SplitBalanceCalculator.IsFullySettled(group, balances);

        if (allSettled)
        {
            group.Status = SplitGroupStatus.Settled;
            _context.SplitGroups.Update(group);
            await _context.SaveChangesAsync();

            await _notifier.NotifyGroupStatusChangedAsync(groupId, $"🎉 All debts settled! Group is now fully settled.", SplitGroupStatus.Settled);
        }
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

        return Result.Success(settlements.Select(MapSettlementToDto).ToList());
    }

    private static (bool IsValid, string? Reason) ValidateInvite(SplitGroupInvite invite)
    {
        if (invite.SplitGroup != null && invite.SplitGroup.Status != SplitGroupStatus.Active)
            return (false, "group is locked or closed — new users cannot join");
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
        var simplified = SplitBalanceCalculator.BuildPlan(group, balances);
        var leftIds = group.Members.Where(m => m.LeftAt != null).Select(m => m.Id).ToHashSet();

        return new GroupBalancesDto
        {
            Balances = balances.Where(b => !leftIds.Contains(b.MemberId)).Select(b => new MemberBalanceDto
            {
                MemberId = b.MemberId,
                MemberName = b.MemberName,
                TotalPaid = b.TotalPaid,
                TotalShare = b.TotalShare,
                SettledPaid = b.SettledPaid,
                SettledReceived = b.SettledReceived,
                InTransit = b.InTransit,
                NetBalance = b.NetBalance
            }).ToList(),
            SimplifiedPlan = simplified.Select(s => new SimplifiedDebtDto
            {
                FromMemberId = s.FromMemberId,
                FromMemberName = s.FromMemberName,
                ToMemberId = s.ToMemberId,
                ToMemberName = s.ToMemberName,
                Amount = s.Amount
            }).ToList(),
            CategoryBreakdown = SplitBalanceCalculator.CategoryTotals(group.Expenses)
                .Select(c => new CategorySpendDto { Category = c.Category, Amount = c.Amount, Count = c.Count })
                .ToList(),
            ExpenseCount = group.Expenses.Count
        };
    }

    private static SettlementDto MapSettlementToDto(SplitSettlement s) => new()
    {
        Id = s.Id,
        FromMemberId = s.FromMemberId,
        FromMemberName = s.FromMember.Name,
        ToMemberId = s.ToMemberId,
        ToMemberName = s.ToMember.Name,
        Amount = s.Amount,
        Method = s.Method,
        Status = s.Status,
        PaymentReference = s.PaymentReference,
        CompletedAt = s.CompletedAt,
        CreatedAt = s.CreatedAt
    };

    private static decimal? SplitValueFor(SplitType type, ExpenseParticipantDto p) => type switch
    {
        SplitType.Exact => p.ExactAmount,
        SplitType.Percentage => p.Percentage,
        SplitType.Shares => p.Shares,
        _ => null
    };

    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    /// <summary>
    /// Keyset pagination over (Date desc, Id desc). An expense back-dated to an
    /// earlier day is therefore returned at its correct place in the timeline
    /// rather than at the top, and paging stays stable while rows are added.
    /// </summary>
    private async Task<Result<ExpensePageDto>> QueryExpensePageAsync(int groupId, ExpenseListQuery q)
    {
        var pageSize = Math.Clamp(q.Limit ?? DefaultPageSize, 1, MaxPageSize);

        var query = _context.SplitExpenses
            .AsNoTracking()
            .Where(e => e.SplitGroupId == groupId);

        var term = q.Search?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(e =>
                e.Description.ToLower().Contains(term) ||
                (e.Notes != null && e.Notes.ToLower().Contains(term)) ||
                e.Payers.Any(p => p.SplitGroupMember.Name.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(q.Category))
        {
            var category = q.Category.Trim();
            query = category.Equals("General", StringComparison.OrdinalIgnoreCase)
                ? query.Where(e => e.Category == null || e.Category == "" || e.Category == category)
                : query.Where(e => e.Category == category);
        }

        if (q.PayerId.HasValue)
        {
            var payerId = q.PayerId.Value;
            query = query.Where(e => e.Payers.Any(p => p.SplitGroupMemberId == payerId));
        }

        if (q.ParticipantId.HasValue)
        {
            var participantId = q.ParticipantId.Value;
            query = query.Where(e => e.Participants.Any(p => p.SplitGroupMemberId == participantId));
        }

        if (q.From.HasValue)
        {
            var from = DateTime.SpecifyKind(q.From.Value.Date, DateTimeKind.Utc);
            query = query.Where(e => e.Date >= from);
        }

        if (q.To.HasValue)
        {
            var toExclusive = DateTime.SpecifyKind(q.To.Value.Date, DateTimeKind.Utc).AddDays(1);
            query = query.Where(e => e.Date < toExclusive);
        }

        if (q.MinAmount.HasValue)
        {
            var min = q.MinAmount.Value;
            query = query.Where(e => e.Amount >= min);
        }

        if (q.MaxAmount.HasValue)
        {
            var max = q.MaxAmount.Value;
            query = query.Where(e => e.Amount <= max);
        }

        var total = await query.CountAsync();

        var sort = (q.Sort ?? "date_desc").Trim().ToLowerInvariant();
        var keyset = sort == "date_desc";
        var offset = 0;

        if (!string.IsNullOrWhiteSpace(q.Cursor))
        {
            if (keyset)
            {
                if (!TryDecodeCursor(q.Cursor, out var cursorDate, out var cursorId))
                    return Result.Failure<ExpensePageDto>(new Error("SplitExpense.InvalidCursor", "Invalid page cursor."));

                query = query.Where(e => e.Date < cursorDate || (e.Date == cursorDate && e.Id < cursorId));
            }
            else if (!q.Cursor.StartsWith("o:") || !int.TryParse(q.Cursor.AsSpan(2), out offset) || offset < 0)
            {
                return Result.Failure<ExpensePageDto>(new Error("SplitExpense.InvalidCursor", "Invalid page cursor."));
            }
        }

        var withLines = query
            .Include(e => e.Payers).ThenInclude(p => p.SplitGroupMember)
            .Include(e => e.Participants).ThenInclude(p => p.SplitGroupMember);

        IOrderedQueryable<SplitExpense> ordered = sort switch
        {
            "date_asc" => withLines.OrderBy(e => e.Date).ThenBy(e => e.Id),
            "amount_desc" => withLines.OrderByDescending(e => e.Amount).ThenByDescending(e => e.Id),
            "amount_asc" => withLines.OrderBy(e => e.Amount).ThenBy(e => e.Id),
            _ => withLines.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
        };

        // The default order pages by keyset (stable while rows are added); the other
        // orders use a simple offset.
        var rows = await (keyset ? ordered : ordered.Skip(offset))
            .Take(pageSize + 1)
            .ToListAsync();

        var hasMore = rows.Count > pageSize;
        var pageRows = hasMore ? rows.Take(pageSize).ToList() : rows;

        return Result.Success(new ExpensePageDto
        {
            Items = pageRows.Select(MapExpenseToDto).ToList(),
            NextCursor = !hasMore ? null : keyset ? EncodeCursor(pageRows[^1]) : $"o:{offset + pageSize}",
            TotalCount = total
        });
    }

    /// <summary>Notes and receipts are for group members only — never exposed on the public share link.</summary>
    private static void HidePrivateFields(IEnumerable<ExpenseDto> expenses)
    {
        foreach (var e in expenses)
        {
            e.Notes = null;
            e.ReceiptUrl = null;
        }
    }

    private static string EncodeCursor(SplitExpense e)
    {
        var raw = $"{e.Date.Ticks}.{e.Id}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static bool TryDecodeCursor(string cursor, out DateTime date, out int id)
    {
        date = default;
        id = 0;
        try
        {
            var b64 = cursor.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var parts = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Split('.');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !int.TryParse(parts[1], out id))
                return false;
            date = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }
        catch (FormatException) { return false; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static ExpenseDto MapExpenseToDto(SplitExpense e) => new()
    {
        Id = e.Id,
        Description = e.Description,
        Amount = e.Amount,
        Date = e.Date,
        Category = e.Category,
        SplitType = e.SplitType,
        Notes = e.Notes,
        ReceiptUrl = e.ReceiptPath,
        Payers = e.Payers.Select(p => new PayerLineDto { MemberId = p.SplitGroupMemberId, MemberName = p.SplitGroupMember.Name, AmountPaid = p.AmountPaid }).ToList(),
        Participants = e.Participants.Select(p => new ParticipantLineDto { MemberId = p.SplitGroupMemberId, MemberName = p.SplitGroupMember.Name, ShareAmount = p.ShareAmount, SplitValue = p.SplitValue }).ToList()
    };

    private static GroupDto MapToDto(SplitGroup g) => new()
    {
        Id = g.Id,
        Name = g.Name,
        CreatedByUserId = g.CreatedByUserId,
        Currency = g.Currency,
        SimplifyDebts = g.SimplifyDebts,
        IsDirect = g.IsDirect,
        StartDate = g.StartDate,
        EndDate = g.EndDate,
        Status = g.Status,
        ShareToken = g.ShareToken,
        Members = g.Members.Select(m => new MemberDto { Id = m.Id, Name = m.Name, LinkedUserId = m.LinkedUserId, UpiId = m.UpiId, LeftAt = m.LeftAt }).ToList(),
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

    private Task NotifyGroupUpdatedAsync(int groupId, string activityMessage)
    {
        return _notifier.NotifyGroupUpdatedAsync(groupId, activityMessage);
    }

    private static DateTime EnsureUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : dt.ToUniversalTime();
}
