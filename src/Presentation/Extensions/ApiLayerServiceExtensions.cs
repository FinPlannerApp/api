using API.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.OpenApi.Models;
using System.IO.Compression;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

namespace API.Extensions;

public static class ApiLayerServiceExtensions
{
    public static IServiceCollection AddApiLayerServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSignalR();
        services.AddScoped<Application.Contracts.ISplitNotifier, API.Services.SplitNotifier>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

            options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));

            options.AddPolicy("bulk", context => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                }));
        });

        // Add Response Compression
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

        var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        var originsSet = new HashSet<string>(configuredOrigins, StringComparer.OrdinalIgnoreCase)
        {
            "https://finplanner.ska97homelab.uk",
            "http://localhost:4200"
        };

        services.AddCors(options =>
        {
            options.AddPolicy(name: "AllowSpecificOrigins",
                              policy =>
                              {
                                  policy.WithOrigins(originsSet.ToArray())
                                        .AllowAnyHeader()
                                        .AllowAnyMethod()
                                        .AllowCredentials();
                              });
        });

        services.AddControllers(options =>
        {
            options.Filters.Add<ValidationFilter>();
        }).AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        // Anti-Forgery (CSRF Protection)
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "XSRF-TOKEN";
            options.Cookie.HttpOnly = false; // Must be false so Angular can read it
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddHttpContextAccessor();

        services.AddEndpointsApiExplorer();

        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "1.0.0";

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Financial Planner API",
                Version = informationalVersion,
                Description = "API for managing personal finances."
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                In = ParameterLocation.Header,
                Description = "Please enter a valid token",
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                BearerFormat = "JWT",
                Scheme = "Bearer"
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}