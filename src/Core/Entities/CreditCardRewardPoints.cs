using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities;

/// <summary>
/// Points-based rewards, earned per bill/cycle — a genuinely separate
/// currency from cashback, not a variant of it. Amount here is always
/// points, never rupees; nothing in this entity touches any account
/// balance directly, unlike statement-credit cashback which does.
/// </summary>
public class CreditCardRewardPoints : BaseEntity
{
    public required string UserId { get; set; }

    public int CreditCardAccountId { get; set; }
    public Account CreditCardAccount { get; set; } = null!;

    public int CreditCardBillId { get; set; }
    public CreditCardBill CreditCardBill { get; set; } = null!;

    public decimal PointsEarned { get; set; }
    public decimal PointsRedeemed { get; set; } = 0;
    public decimal PointsExpired { get; set; } = 0;

    // Null means lifetime — no separate boolean needed alongside this,
    // since a bool-plus-nullable-date pair can drift out of sync with
    // itself in a way a single nullable field can't.
    public DateTime? ExpiryDate { get; set; }

    // Deliberately free text, not an auto-converted cash value —
    // redemption value varies too much by card and redemption type
    // (statement credit vs. travel vs. merchandise) to model
    // accurately; a plain note is honest about what's actually known.
    public string? RedemptionNote { get; set; }

    [NotMapped]
    public decimal PointsRemaining => PointsEarned - PointsRedeemed - PointsExpired;
}

