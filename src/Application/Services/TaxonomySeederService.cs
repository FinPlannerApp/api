using Domain.Entities;
using Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Application.Services;

public class TaxonomySeederService
{
    private readonly IApplicationDbContext _context;

    public TaxonomySeederService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task SeedAsync()
    {
        if (await _context.IssueTaxonomies.AnyAsync()) return;

        var categories = new Dictionary<string, List<(string Sub, string[] Contexts)>>
        {
            { "Financial Core", new List<(string, string[])> {
                ("Balance mismatch", new[] { "Bank Sync", "Manual Entry" }),
                ("Duplicate transaction", new[] { "Import", "Sync" }),
                ("Missing transaction", new[] { "Sync" }),
                ("Wrong category", new[] { "Auto-cat", "Rules" }),
                ("Incorrect tax calculation", new[] { "Reports" })
            }},
            { "UX / Flow", new List<(string, string[])> {
                ("Too many clicks", new[] { "Navigation", "Forms" }),
                ("Confusing terminology", new[] { "Labels", "Help" }),
                ("Poor mobile layout", new[] { "Dashboard", "Transaction List" }),
                ("Accessibility issue", new[] { "Colors", "Screen Reader" })
            }},
            { "Performance", new List<(string, string[])> {
                ("Slow dashboard", new[] { "Load Time" }),
                ("Report takes too long", new[] { "Export", "View" }),
                ("App freeze", new[] { "Startup", "Scrolling" })
            }},
            { "Data Trust", new List<(string, string[])> {
                ("Data mismatch across screens", new[] { "Dashboard vs Reports" }),
                ("Export mismatch", new[] { "CSV", "PDF" }),
                ("Audit trail missing", new[] { "History" })
            }},
            { "Security / Privacy", new List<(string, string[])> {
                ("Session timeout", new[] { "Login" }),
                ("Data visibility issue", new[] { "Shared Access" }),
                ("Export access issue", new[] { "Download" })
            }},
            { "Account", new List<(string, string[])> {
                ("Balance mismatch", new[] { "Bank Sync", "Manual Entry" }),
                ("Account creation", new[] { "Form" }),
                ("Missing account", new[] { "Sync" })
            }},
            { "Transaction", new List<(string, string[])> {
                ("Duplicate transaction", new[] { "Import", "Sync" }),
                ("Missing transaction", new[] { "Sync" }),
                ("Wrong category", new[] { "Auto-cat", "Rules" })
            }},
            { "Budget", new List<(string, string[])> {
                ("Budget calculation incorrect", new[] { "Calculation" }),
                ("Cannot create budget", new[] { "Form" })
            }},
            { "Dashboard", new List<(string, string[])> {
                ("Slow dashboard", new[] { "Load Time" }),
                ("Data mismatch across screens", new[] { "Widgets" })
            }},
            { "General UI/UX", new List<(string, string[])> {
                ("Too many clicks", new[] { "Navigation", "Forms" }),
                ("Confusing terminology", new[] { "Labels", "Help" }),
                ("Poor mobile layout", new[] { "Responsiveness" })
            }}
        };

        foreach (var cat in categories)
        {
            var categoryEntity = new IssueTaxonomy { Name = cat.Key, Type = "Category" };
            _context.IssueTaxonomies.Add(categoryEntity);
            await _context.SaveChangesAsync(); // Save to get ID

            foreach (var sub in cat.Value)
            {
                var subEntity = new IssueTaxonomy { Name = sub.Item1, Type = "Subcategory", ParentId = categoryEntity.Id };
                _context.IssueTaxonomies.Add(subEntity);
                await _context.SaveChangesAsync();

                foreach (var ctx in sub.Item2)
                {
                    _context.IssueTaxonomies.Add(new IssueTaxonomy { Name = ctx, Type = "Context", ParentId = subEntity.Id });
                }
            }
        }
        await _context.SaveChangesAsync();

        // Seed Product Roadmap Candidates
        if (!await _context.Issues.AnyAsync())
        {
            var financialCoreCat = await _context.IssueTaxonomies.FirstOrDefaultAsync(t => t.Name == "Financial Core" && t.Type == "Category");
            var uxFlowCat = await _context.IssueTaxonomies.FirstOrDefaultAsync(t => t.Name == "General UI/UX" && t.Type == "Category");
            var securityCat = await _context.IssueTaxonomies.FirstOrDefaultAsync(t => t.Name == "Security / Privacy" && t.Type == "Category");

            var roadmapItems = new List<Issue>
            {
                new Issue
                {
                    Title = "AI-Powered Predictive Financial Forecasting & Smart Budgets",
                    Description = "<p>Introducing a comprehensive financial prediction model that projects month-end balances based on historic transactions and active recurring events.</p><ul><li>Predictive algorithms using multi-variable regression.</li><li>Interactive visual chart for cash-flow projection up to 6 months.</li><li>Smart alerts when a budget is projected to be breached.</li></ul>",
                    Status = "Planned",
                    Priority = "High",
                    Type = IssueType.Feature,
                    Severity = "Major",
                    ImpactsMoney = true,
                    FinancialImpactAmount = 5000,
                    Frequency = "Always",
                    Votes = 42,
                    PainScore = 420,
                    CategoryId = financialCoreCat?.Id,
                    CreatorUserId = null
                },
                new Issue
                {
                    Title = "Plaid / Yodlee Secure Bank Sync Integration",
                    Description = "<p>Fully automated, secure connection to major financial institutions via Plaid or Yodlee API. Say goodbye to manual transaction entries!</p><ul><li>Automated fetching of transactions and balances once a day.</li><li>Bank-grade OAuth authentication flows.</li><li>Intelligent auto-categorization of imported transactions.</li></ul>",
                    Status = "Planned",
                    Priority = "High",
                    Type = IssueType.Feature,
                    Severity = "Critical",
                    ImpactsMoney = true,
                    FinancialImpactAmount = 15000,
                    Frequency = "Always",
                    Votes = 89,
                    PainScore = 890,
                    CategoryId = financialCoreCat?.Id,
                    CreatorUserId = null
                },
                new Issue
                {
                    Title = "Collaborative Family Budgeting & Shared Accounts",
                    Description = "<p>Allow multiple users to access and manage a shared group of accounts and budgets. Perfect for households or small businesses.</p><ul><li>Fine-grained permission controls (Viewer, Editor, Co-owner).</li><li>Real-time sync and updates across active user screens.</li><li>Comprehensive audit logging detailing who made which transaction.</li></ul>",
                    Status = "Planned",
                    Priority = "Medium",
                    Type = IssueType.Feature,
                    Severity = "Major",
                    ImpactsMoney = false,
                    Votes = 27,
                    PainScore = 270,
                    CategoryId = uxFlowCat?.Id,
                    CreatorUserId = null
                },
                new Issue
                {
                    Title = "Multi-Currency Support & Live Exchange Rate Conversion",
                    Description = "<p>Seamlessly track accounts and record transactions in multiple currencies with automated exchange rate conversion and live currency feeds.</p><ul><li>Support for over 30 global currencies and crypto-assets.</li><li>Daily automated exchange rate fetching from open exchange rates.</li><li>Multi-currency analytics and aggregated visual reports in home currency.</li></ul>",
                    Status = "InProgress",
                    Priority = "High",
                    Type = IssueType.Feature,
                    Severity = "Major",
                    ImpactsMoney = true,
                    FinancialImpactAmount = 8000,
                    Frequency = "Frequent",
                    Votes = 56,
                    PainScore = 560,
                    CategoryId = financialCoreCat?.Id,
                    CreatorUserId = null
                },
                new Issue
                {
                    Title = "Advanced Custom Visual Reports & PDF Export Engine",
                    Description = "<p>An interactive drag-and-drop report builder allowing users to design and export customized financial health summaries and transaction statements.</p><ul><li>Custom charts (pie, bar, stacked, line) with multi-variable filters.</li><li>High-fidelity PDF and CSV exporter with branding options.</li><li>Schedule automated monthly reports sent directly to email.</li></ul>",
                    Status = "InProgress",
                    Priority = "Medium",
                    Type = IssueType.Feature,
                    Severity = "Minor",
                    ImpactsMoney = false,
                    Votes = 34,
                    PainScore = 340,
                    CategoryId = uxFlowCat?.Id,
                    CreatorUserId = null
                },
                new Issue
                {
                    Title = "Hangfire Automated Recurring Transactions Engine",
                    Description = "<p>A fully integrated background scheduler utilizing Hangfire to automatically record recurring payments, bills, and subscriptions.</p><ul><li>Configure Daily, Weekly, Monthly, or Yearly recurrence schedules.</li><li>Silent background jobs processing due items every hour.</li><li>Linked Subscription tracker with direct cancellation URLs.</li></ul>",
                    Status = "Released",
                    Priority = "High",
                    Type = IssueType.Feature,
                    Severity = "Major",
                    ImpactsMoney = true,
                    FinancialImpactAmount = 12000,
                    Frequency = "Always",
                    Votes = 72,
                    PainScore = 720,
                    CategoryId = financialCoreCat?.Id,
                    CreatorUserId = null
                },
                new Issue
                {
                    Title = "Optimistic Concurrency Data Integrity with DataHash",
                    Description = "<p>Ensures that concurrent transactions do not override each other by introducing optimistic concurrency checking via a unique DataHash field on Accounts and Transactions.</p><ul><li>Automated hashing on insert/update.</li><li>Detailed concurrency exception reporting to the client.</li><li>Global audit trail integrations.</li></ul>",
                    Status = "Released",
                    Priority = "High",
                    Type = IssueType.Bug,
                    Severity = "Major",
                    ImpactsMoney = true,
                    FinancialImpactAmount = 4500,
                    Frequency = "Rare",
                    Votes = 15,
                    PainScore = 150,
                    CategoryId = securityCat?.Id,
                    CreatorUserId = null
                },
                new Issue
                {
                    Title = "Pain-Score Prioritized Feedback Hub & Support System",
                    Description = "<p>A community-driven portal where users can report bugs, suggest features, discuss implementation details, and vote on existing requests.</p><ul><li>Automated calculations of Prioritized Pain Scores.</li><li>Threaded nested discussion replies and comment reaction emojis.</li><li>Direct SLA and milestone tracking linked to Github repository issue API.</li></ul>",
                    Status = "Released",
                    Priority = "Medium",
                    Type = IssueType.Feature,
                    Severity = "Minor",
                    ImpactsMoney = false,
                    Votes = 95,
                    PainScore = 950,
                    CategoryId = uxFlowCat?.Id,
                    CreatorUserId = null
                }
            };

            _context.Issues.AddRange(roadmapItems);
            await _context.SaveChangesAsync();
        }
    }
}
