using Application;
using Application.Contracts;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace API.Extensions;

using Hangfire;
using Hangfire.Redis.StackExchange;
using Infrastructure.BackgroundJobs;
using StackExchange.Redis;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Configure strongly typed settings objects
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        // 2. Register the DbContext
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"),
            o => o.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));

        // 2.1 Register Redis connection
        // 2.1 Register Redis connection with AbortOnConnectFail=false for local dev resilience
        services.AddSingleton<IConnectionMultiplexer>(sp => 
        {
            var connectionString = configuration.GetConnectionString("Redis") ?? "localhost";
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 3;
            if (options.ConnectTimeout < 10000)
            {
                options.ConnectTimeout = 10000;
            }
            if (options.SyncTimeout < 10000)
            {
                options.SyncTimeout = 10000;
                options.AsyncTimeout = 10000;
            }
            return ConnectionMultiplexer.Connect(options);
        });

        // 2.2 Register two-tier cache service (IMemoryCache L1 + Redis L2)
        // L1 serves most reads with zero Upstash commands; Redis only hit on L1 miss.
        services.AddMemoryCache();
        services.AddSingleton<ICacheService, RedisCacheService>();

        // 2.3 Register Email Services & Workers
        services.AddSingleton<IDirectEmailSender, DirectEmailSender>();
        services.AddScoped<IEmailService, RedisEmailService>();
        services.AddHostedService<EmailQueueWorker>();
        services.AddHostedService<InfrastructureMonitorWorker>();

        // 2.4 Register Hangfire
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseRedisStorage(configuration.GetConnectionString("Redis") ?? "localhost"));
        
        services.AddHangfireServer();
        
        services.AddScoped<RecurringTransactionJob>();

        // 3. Register Identity with STRONG password requirements (dev AND production)
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+ ";
            
            // PRODUCTION-GRADE Password Requirements (applied to both dev and prod)
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 12;
            options.Password.RequireNonAlphanumeric = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequiredUniqueChars = 4;
            
            // Account Lockout Protection (prevent brute force attacks)
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>();

        // 4. Register JWT Authentication
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
                ClockSkew = TimeSpan.Zero // Strict expiration - no grace period
            };
        });

        // 5. Register Services
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        return services;
    }
}