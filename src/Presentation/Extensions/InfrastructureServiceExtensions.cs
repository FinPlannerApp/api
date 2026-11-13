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
    /// <summary>
    /// Refuse to start outside Development if the JWT signing key is missing, short or one of the
    /// placeholder values committed to the repo -- anyone who can read the repo could forge tokens.
    /// Set it with the environment variable Jwt__Key (32+ random characters).
    /// </summary>
    private static void EnsureJwtKeyIsSafe(IConfiguration configuration)
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase)) return;

        var key = configuration[$"{JwtSettings.SectionName}:Key"];
        var placeholders = new[] { "YOUR_SUPER_SECRET_KEY", "SOME_SUPER_SECRET_KEY", "CHANGE_ME", "CHANGEME" };
        var bad = string.IsNullOrWhiteSpace(key)
                  || key.Length < 32
                  || placeholders.Any(p => key.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (bad)
            throw new InvalidOperationException(
                "Jwt:Key is missing, shorter than 32 characters, or still a placeholder. " +
                "Set a strong random value via the Jwt__Key environment variable before starting in " + env + ". " +
                "(If a placeholder was ever used in production, rotate it: all existing tokens become invalid.)");
    }

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Configure strongly typed settings objects
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<SessionSecuritySettings>(configuration.GetSection(SessionSecuritySettings.SectionName));
        EnsureJwtKeyIsSafe(configuration);

        // 2. Register the DbContext
        services.AddSingleton<Infrastructure.Persistence.DashboardCacheInvalidationInterceptor>();
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"),
                o => o.MigrationsHistoryTable("__EFMigrationsHistory"));
            options.AddInterceptors(sp.GetRequiredService<Infrastructure.Persistence.DashboardCacheInvalidationInterceptor>());
        });

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

            // The library pings an open connection every 60s so a silently dropped socket is noticed
            // quickly. Upstash does not bill PING, so the default is kept; raise it only if you want
            // fewer log/network round trips.
            options.KeepAlive = configuration.GetValue<int?>("Redis:KeepAliveSeconds") ?? 60;
            // Skip the CLIENT SETINFO handshake commands sent on every (re)connect.
            options.SetClientLibrary = false;
            options.ConnectRetry = 5;
            options.ConnectTimeout = 30000;
            options.SyncTimeout = 30000;
            options.AsyncTimeout = 30000;
            
            return ConnectionMultiplexer.Connect(options);
        });

        // 2.2 Register two-tier cache service (IMemoryCache L1 + Redis L2)
        // L1 serves most reads with zero Upstash commands; Redis only hit on L1 miss.
        services.AddMemoryCache();
        // Consumers that only need Redis occasionally (OTP codes) take Lazy<IConnectionMultiplexer>,
        // so the connection is opened on the first OTP request instead of at startup.
        services.AddSingleton<Lazy<IConnectionMultiplexer>>(sp =>
            new Lazy<IConnectionMultiplexer>(() => sp.GetRequiredService<IConnectionMultiplexer>()));
        services.AddSingleton<RedisQuotaGuard>();
        services.AddHostedService<RedisActivityKeeperWorker>();
        services.AddSingleton<ICacheService, RedisCacheService>();

        // 2.3 Register Email Services & Workers
        services.AddSingleton<IDirectEmailSender, DirectEmailSender>();
        services.AddSingleton<InMemoryEmailQueue>();
        services.AddScoped<IEmailService, QueuedEmailService>();
        services.AddHostedService<EmailQueueWorker>();
        services.AddHostedService<InfrastructureMonitorWorker>();

        services.AddScoped<RecurringTransactionJob>();
        services.AddScoped<UpdatePainVelocityJob>();
        services.AddScoped<RefreshTokenCleanupJob>();
        services.AddScoped<RewardPointsExpiryJob>();
        services.AddScoped<AccountChargeAdvanceJob>();

        services.AddHostedService<RecurringTransactionSchedulerWorker>();
        services.AddHostedService<PainVelocitySchedulerWorker>();
        services.AddHostedService<RefreshTokenCleanupSchedulerWorker>();
        services.AddHostedService<RewardPointsExpirySchedulerWorker>();
        services.AddHostedService<AccountChargeAdvanceSchedulerWorker>();

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
