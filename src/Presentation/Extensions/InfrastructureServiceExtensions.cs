using System;
using Application;
using Application.Contracts;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Services;
using Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using System.Text;
namespace API.Extensions;
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
            o => o.MigrationsHistoryTable("__EFMigrationsHistory")));

        // 2.1 Register Redis connection
        // 2.1 Register Redis connection with AbortOnConnectFail=false for local dev resilience
        services.AddSingleton<IConnectionMultiplexer>(sp => 
        {
            var rawConnectionString = configuration.GetConnectionString("Redis") ?? 
                                     configuration["REDIS_URL"] ?? 
                                     "localhost";
            
            ConfigurationOptions options;
            
            if (rawConnectionString.StartsWith("redis", StringComparison.OrdinalIgnoreCase) && rawConnectionString.Contains("://"))
            {
                var uri = new Uri(rawConnectionString);
                var userInfo = uri.UserInfo;
                var password = string.IsNullOrEmpty(userInfo) ? null : userInfo.Split(':').LastOrDefault();
                
                options = new ConfigurationOptions
                {
                    EndPoints = { { uri.Host, uri.Port > 0 ? uri.Port : 6379 } },
                    Password = password,
                    Ssl = uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase) || uri.Host.Contains("upstash.io"),
                    AbortOnConnectFail = false
                };
            }
            else
            {
                options = ConfigurationOptions.Parse(rawConnectionString);
                options.AbortOnConnectFail = false;
                if (options.EndPoints.Any(e => e?.ToString()?.Contains("upstash.io") == true))
                {
                    options.Ssl = true;
                }
            }

            options.ConnectRetry = 5;
            options.ConnectTimeout = 30000;
            options.SyncTimeout = 30000;
            options.AsyncTimeout = 30000;
            
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

        services.AddScoped<RecurringTransactionJob>();
        services.AddScoped<UpdatePainVelocityJob>();
        services.AddScoped<RefreshTokenCleanupJob>();
        services.AddScoped<RewardPointsExpiryJob>();

        services.AddHostedService<RecurringTransactionSchedulerWorker>();
        services.AddHostedService<PainVelocitySchedulerWorker>();
        services.AddHostedService<RefreshTokenCleanupSchedulerWorker>();
        services.AddHostedService<RewardPointsExpirySchedulerWorker>();

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
        .AddRoles<IdentityRole>()
        .AddRoleManager<RoleManager<IdentityRole>>()
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        // Overrides Identity's default PBKDF2-HMAC-SHA256 hasher with Argon2id.
        // Must be registered AFTER AddIdentityCore(...) — see Argon2PasswordHasher.cs
        // for the migration-safety details (existing users aren't locked out).
        services.AddSingleton<IPasswordHasher<ApplicationUser>, Argon2PasswordHasher<ApplicationUser>>();

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
                ClockSkew = TimeSpan.Zero
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });

        // 5. Register Services
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        return services;
    }
}
