namespace Application.DTOs.Settings;

/// <summary>App-wide preferences that several modules read ("general" module).</summary>
public class GeneralSettingsDto
{
    /// <summary>Day (1–28) on which the user's money month starts, e.g. salary day. 1 = calendar month.</summary>
    public int MonthStartDay { get; set; } = 1;
}

/// <summary>"budgets" module settings.</summary>
public class BudgetSettingsDto
{
    /// <summary>Default alert level, 50–100 % of a budget. A budget can override it.</summary>
    public int DefaultAlertPercent { get; set; } = 80;
    /// <summary>Show an in-app toast when a new expense reaches the alert level or the limit.</summary>
    public bool ToastAlerts { get; set; } = true;
    /// <summary>Also email on alert/limit. Off by default — figures are entered by hand, so the toast is usually enough.</summary>
    public bool EmailAlerts { get; set; } = false;
}

/// <summary>"accounts" module settings.</summary>
public class AccountSettingsDto
{
    /// <summary>Flag bank/cash accounts whose balance falls below this amount. 0 = off.</summary>
    public decimal LowBalanceThreshold { get; set; } = 0;
    /// <summary>Blur balances on the Accounts pages until revealed.</summary>
    public bool HideBalances { get; set; } = false;
    /// <summary>Highlight credit cards whose bill is due within this many days (0 = off, max 14).</summary>
    public int CardDueReminderDays { get; set; } = 5;
}

/// <summary>"transactions" module settings.</summary>
public class TransactionSettingsDto
{
    /// <summary>An identical transaction saved again within this many seconds is rejected as a double-submit. 0 = off, max 120.</summary>
    public int DuplicateGuardSeconds { get; set; } = 3;
    /// <summary>Expenses at or above this amount are called out on the Transactions overview. 0 = off.</summary>
    public decimal LargeExpenseThreshold { get; set; } = 0;
    /// <summary>Most Quick Cards shown in Bulk add (0 = hide the strip, max 30).</summary>
    public int QuickCardsMax { get; set; } = 12;
    /// <summary>An entry must be repeated this many times to become a Quick Card (2-20).</summary>
    public int QuickCardsMinUses { get; set; } = 3;
    /// <summary>Quick Cards look back this many days (14-365).</summary>
    public int QuickCardsDays { get; set; } = 120;
    /// <summary>Account pre-filled in Bulk add when you type an amount or description (null = none).</summary>
    public int? DefaultAccountId { get; set; }
    /// <summary>Category pre-filled for new transactions (null = none).</summary>
    public int? DefaultCategoryId { get; set; }
}

/// <summary>"subscriptions" module settings.</summary>
public class SubscriptionSettingsDto
{
    /// <summary>Highlight subscriptions renewing within this many days (0 = off, max 30).</summary>
    public int RenewalAlertDays { get; set; } = 7;
    /// <summary>Your own monthly ceiling for subscription spend; the Overview shows progress against it. 0 = off.</summary>
    public decimal MonthlyTarget { get; set; } = 0;
}

/// <summary>"recurring" module settings.</summary>
public class RecurringSettingsDto
{
    /// <summary>How far ahead the Recurring overview projects (7–180 days).</summary>
    public int LookAheadDays { get; set; } = 30;
    /// <summary>Include paused items in the overview list (they never count toward totals).</summary>
    public bool ShowPaused { get; set; } = true;
}

/// <summary>"notifications" settings — account-wide email switches. Every one defaults to OFF.</summary>
public class NotificationSettingsDto
{
    /// <summary>Email me when someone comments on, or changes the status of, an issue I reported in Feedback Hub.</summary>
    public bool IssueUpdateEmails { get; set; } = false;
    /// <summary>Email me a daily digest of payments coming due (recurring items and subscriptions, insurance premiums, card bills). OFF by default.</summary>
    public bool PaymentReminderEmails { get; set; } = false;
    /// <summary>How many days ahead to remind (1-14).</summary>
    public int ReminderDaysBefore { get; set; } = 2;
}
