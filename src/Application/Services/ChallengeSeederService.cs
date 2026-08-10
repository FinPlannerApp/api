using Application.Contracts;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

/// <summary>
/// Seeds the 30 ChallengeDay content rows and the completion Badge.
/// Follows the exact same guard pattern as TaxonomySeederService — safe to
/// call on every startup, no-ops after the first successful run.
///
/// Content is paraphrased into concise app-card copy from a standard
/// 30-day personal finance challenge structure (4 weeks: awareness/cleanup,
/// budgeting, debt/cashflow, wealth-building), not reproduced verbatim from
/// any single source.
/// </summary>
public class ChallengeSeederService
{
    private readonly IApplicationDbContext _context;
    public const string CompletionBadgeName = "30-Day Money Challenge Graduate";

    public ChallengeSeederService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task SeedAsync()
    {
        await SeedChallengeDaysAsync();
        await SeedCompletionBadgeAsync();
    }

    private async Task SeedChallengeDaysAsync()
    {
        if (await _context.ChallengeDays.AnyAsync()) return;

        var days = new List<ChallengeDay>
        {
            // ── Week 1: Money Awareness and Cleanup ──────────────────────────
            new() { DayNumber = 1, WeekNumber = 1,
                Title = "Map Your Money Footprint",
                Description = "List every bank account, UPI ID, and payment app you use. Add each one as an Account so you have a single source of truth.",
                ActionRoute = "/app/accounts" },

            new() { DayNumber = 2, WeekNumber = 1,
                Title = "Review Your Last 30 Days",
                Description = "Pull your last month's bank statement and see exactly where your money went. Import it via bulk upload or add transactions manually.",
                ActionRoute = "/app/transactions" },

            new() { DayNumber = 3, WeekNumber = 1,
                Title = "Cancel What You Don't Use",
                Description = "Go through your subscriptions and cancel anything you haven't actually used recently.",
                ActionRoute = "/app/subscriptions" },

            new() { DayNumber = 4, WeekNumber = 1,
                Title = "Declutter Your Digital Space",
                Description = "Unsubscribe from marketing emails and mute social accounts that trigger impulse spending or FOMO. Nothing to log here — just do it." },

            new() { DayNumber = 5, WeekNumber = 1,
                Title = "Audit Hidden Fees",
                Description = "Check your bank and card statements for annual fees, maintenance charges, or penalties you didn't know about. Cancel any card you're paying to keep unused." },

            new() { DayNumber = 6, WeekNumber = 1,
                Title = "Lock Down Your Accounts",
                Description = "Update weak banking passwords and turn on two-factor authentication everywhere you can." },

            new() { DayNumber = 7, WeekNumber = 1, IsRestDay = true,
                Title = "Rest Day",
                Description = "Take a breather. Facing your numbers honestly is the hardest part — you've done it." },

            // ── Week 2: The Hot Money Method ─────────────────────────────────
            new() { DayNumber = 8, WeekNumber = 2, RequiresReflection = true,
                Title = "Define Your Rich Life",
                Description = "Forget what you're 'supposed' to want. Write down what a genuinely rich life looks like for you, stripped of anyone else's expectations." },

            new() { DayNumber = 9, WeekNumber = 2,
                Title = "Sort Your Spending Into 4 Buckets",
                Description = "Tag your spending categories as Needs, Joys, Goals, or Leaks. This becomes the foundation for your budget.",
                ActionRoute = "/app/transaction-categories" },

            new() { DayNumber = 10, WeekNumber = 2,
                Title = "Start Tracking Daily",
                Description = "Get in the habit of logging what you spend, every day, starting today.",
                ActionRoute = "/app/transactions" },

            new() { DayNumber = 11, WeekNumber = 2,
                Title = "Build Your 50/30/20 Budget",
                Description = "50% Needs, up to 30% Joys, 20% Goals. Set this up as your first real budget.",
                ActionRoute = "/app/budgets" },

            new() { DayNumber = 12, WeekNumber = 2,
                Title = "Tighten the Leaks",
                Description = "Set a strict limit on your Leaks category and hold yourself to it this week.",
                ActionRoute = "/app/budgets" },

            new() { DayNumber = 13, WeekNumber = 2,
                Title = "Pick Your Money Days",
                Description = "Choose fixed days each week for paying bills, checking balances, and investing. Consistency beats intensity.",
                ActionRoute = "/app/recurring-transactions" },

            new() { DayNumber = 14, WeekNumber = 2, IsRestDay = true,
                Title = "Rest Day",
                Description = "You've built the framework. Let it settle before Week 3." },

            // ── Week 3: Managing Debt, Credit, and Cash Flow ─────────────────
            new() { DayNumber = 15, WeekNumber = 3,
                Title = "List Every Debt",
                Description = "Every loan, every EMI, every rupee you owe — friends, apps, banks. Order it from highest EMI to lowest.",
                ActionRoute = "/app/accounts" },

            new() { DayNumber = 16, WeekNumber = 3,
                Title = "Choose Your Payoff Strategy",
                Description = "Paying off the smallest balance first builds momentum fastest. Decide your order and commit to it." },

            new() { DayNumber = 17, WeekNumber = 3,
                Title = "Automate Every EMI",
                Description = "Set up a recurring transaction for each EMI so a due date never slips again.",
                ActionRoute = "/app/recurring-transactions" },

            new() { DayNumber = 18, WeekNumber = 3,
                Title = "Check Your Credit Score",
                Description = "Look up your CIBIL score. It decides the interest rate you'll get on everything from here on." },

            new() { DayNumber = 19, WeekNumber = 3,
                Title = "Renegotiate What You Can",
                Description = "If you owe money informally, have the conversation. Politely ask for a lower rate." },

            new() { DayNumber = 20, WeekNumber = 3,
                Title = "Find One More Income Stream",
                Description = "A side gig, freelance work, or simply asking for the raise you've earned. Write down three real options." },

            new() { DayNumber = 21, WeekNumber = 3, IsRestDay = true,
                Title = "Rest Day",
                Description = "Debt work is heavy. Rest before you shift toward building." },

            // ── Week 4: Wealth Building and Energy Shift ─────────────────────
            new() { DayNumber = 22, WeekNumber = 4,
                Title = "Secure Your Emergency Fund",
                Description = "Move it into a debt mutual fund, or start one with a SIP if you don't have a fund yet.",
                ActionRoute = "/app/goals" },

            new() { DayNumber = 23, WeekNumber = 4,
                Title = "Design Your Investment Mix",
                Description = "Split your investments across large-cap, mid-cap, and small-cap based on your age and risk appetite." },

            new() { DayNumber = 24, WeekNumber = 4,
                Title = "Automate Your Investments",
                Description = "Schedule your SIP dates so investing happens without you having to remember.",
                ActionRoute = "/app/recurring-transactions" },

            new() { DayNumber = 25, WeekNumber = 4,
                Title = "Pick Your Platform",
                Description = "Choose one broker or investment app you trust and stick with it." },

            new() { DayNumber = 26, WeekNumber = 4, RequiresReflection = true,
                Title = "Face Your Money Beliefs",
                Description = "What's one belief about money you inherited that's holding you back? Write it down, then write why it isn't true." },

            new() { DayNumber = 27, WeekNumber = 4, RequiresReflection = true,
                Title = "Map Your Next 15 Years",
                Description = "Where do you want to be in 1 year, 5 years, 15 years? Get specific." },

            new() { DayNumber = 28, WeekNumber = 4, IsRestDay = true,
                Title = "Rest Day",
                Description = "Almost there. Let the last month settle in." },

            new() { DayNumber = 29, WeekNumber = 4, RequiresReflection = true,
                Title = "Write to Future You",
                Description = "A letter to the person you'll be a year from now. What did you build? What do you want them to remember about starting this? " },

            new() { DayNumber = 30, WeekNumber = 4,
                Title = "Review the Whole Month",
                Description = "What worked, what didn't, and what are you carrying into next month?" },
        };

        _context.ChallengeDays.AddRange(days);
        await _context.SaveChangesAsync();
    }

    private async Task SeedCompletionBadgeAsync()
    {
        if (await _context.Badges.AnyAsync(b => b.Name == CompletionBadgeName)) return;

        _context.Badges.Add(new Badge
        {
            Name = CompletionBadgeName,
            Description = "Completed all 30 days of the Money Challenge — cleanup, budgeting, debt, and wealth-building.",
            IconUrl = "pi pi-verified",
            Color = "#059669"
        });

        await _context.SaveChangesAsync();
    }
}
