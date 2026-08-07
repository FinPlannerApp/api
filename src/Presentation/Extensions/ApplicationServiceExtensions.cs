using Application.Contracts;
using FluentValidation;
using System.Reflection;
using Application.DTOs.AccountCategory;
using Application.DTOs.Categories;
using Application.DTOs.TransactionCategory;
using Application.Services;

namespace API.Extensions;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddMediatR(cfg => {
            cfg.RegisterServicesFromAssemblies(AppDomain.CurrentDomain.GetAssemblies());
            cfg.AddOpenBehavior(typeof(Application.Common.Behaviors.ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(Application.Common.Behaviors.CachingBehavior<,>));
        });

        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ISavingsBucketService, SavingsBucketService>();
        services.AddScoped<IMerchantService, MerchantService>();
        services.AddScoped<IGoalService, GoalService>();
        services.AddScoped<ITransactionService, TransactionService>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddScoped<ICategoryService<AccountCategoryDto, UpsertAccountCategoryDto>, AccountCategoryService>();
        services.AddScoped<AccountCategoryService>();
        services.AddScoped<ICategoryService<TransactionCategoryDto, UpsertTransactionCategoryDto>, TransactionCategoryService>();

        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<Application.Features.Insights.IFinancialInsightsEngine, Application.Features.Insights.FinancialInsightsEngine>();
        services.AddScoped<Domain.Rules.IFinancialRule, Application.Features.Insights.Rules.HighSubscriptionSpendRule>();
        services.AddScoped<Domain.Rules.IFinancialRule, Application.Features.Insights.Rules.LifestyleInflationRule>();
        services.AddScoped<Domain.Rules.IFinancialRule, Application.Features.Insights.Rules.SalaryDaySpikeRule>();
        services.AddScoped<Domain.Rules.IFinancialRule, Application.Features.Insights.Rules.SubscriptionReviewNudgeRule>();

        services.AddScoped<IssueRankingService>();
        services.AddScoped<IssueSimilarityService>();
        services.AddScoped<TaxonomySeederService>();
        services.AddScoped<IChallengeService, ChallengeService>();
        services.AddScoped<ChallengeSeederService>();
        services.AddScoped<GamificationService>();
        services.AddScoped<IssueWorkflowService>();
        services.AddScoped<VoteService>();
        services.AddScoped<CommentService>();
        services.AddScoped<ReactionService>();
        services.AddScoped<IssueActivityService>();
        services.AddScoped<IssueRelationService>();
        services.AddScoped<IDecisionJournalService, DecisionJournalService>();

        return services;
    }
}