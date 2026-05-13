using System.Text;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ScamAlert.Api.Services.Auth;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddScamAlertAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useTestingAuth = false)
    {
        if (useTestingAuth)
        {
            services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
            services.AddSingleton<IPasswordHasher, PasswordHasher>();
            services.AddScoped<ICurrentUserAccessService, CurrentUserAccessService>();
            services.AddScoped<IDeviceIngestAuthService, DeviceIngestAuthService>();
            services.AddAuthentication("Testing")
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestingAuthHandler>(
                    "Testing",
                    _ => { });
            services.AddAuthorization(options =>
            {
                options.AddPolicy(AuthPolicies.AdminOnly, policy => policy.RequireRole("admin"));
                options.AddPolicy(AuthPolicies.CustomerScope, policy => policy.Requirements.Add(new CustomerScopeRequirement()));
            });
            services.AddSingleton<IAuthorizationHandler, CustomerScopeAuthorizationHandler>();
            services.AddRateLimiter(rateLimiterOptions =>
            {
                rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                rateLimiterOptions.AddPolicy("signup", context =>
                {
                    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: ip,
                        factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 100,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        });
                });
                AddBillingRateLimitPolicies(rateLimiterOptions);
            });
            return services;
        }

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IAuthCredentialService, AuthCredentialService>();
        services.AddScoped<IDeviceIngestAuthService, DeviceIngestAuthService>();
        services.AddScoped<ICurrentUserAccessService, CurrentUserAccessService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        var authOptions = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        var provider = authOptions.Provider?.Trim();
        ValidateJwtOptions(authOptions.Jwt);

        if (provider is null || provider.Equals("JwtBearer", StringComparison.OrdinalIgnoreCase))
        {
            var jwt = authOptions.Jwt;
            var authBuilder = services.AddAuthentication(options =>
                {
                    options.DefaultScheme = AuthSchemes.PathForwarding;
                    options.DefaultChallengeScheme = AuthSchemes.PathForwarding;
                })
                .AddPolicyScheme(AuthSchemes.PathForwarding, AuthSchemes.PathForwarding, policy =>
                {
                    policy.ForwardDefaultSelector = ctx =>
                    {
                        var authHeader = ctx.Request.Headers.Authorization.ToString();
                        if (ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrEmpty(authHeader)
                            && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            return JwtBearerDefaults.AuthenticationScheme;
                        }

                        return CookieAuthenticationDefaults.AuthenticationScheme;
                    };
                })
                .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, cookie =>
                {
                    cookie.LoginPath = "/login";
                    cookie.Cookie.Name = "ScamAlert.Portal";
                    cookie.Cookie.HttpOnly = true;
                    cookie.Cookie.SameSite = SameSiteMode.Lax;
                })
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.RequireHttpsMetadata = jwt.RequireHttpsMetadata;
                    options.SaveToken = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwt.Issuer,
                        ValidAudience = jwt.Audience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                        ClockSkew = TimeSpan.FromSeconds(30)
                    };
                });

            var ms = authOptions.Microsoft;
            if (!string.IsNullOrWhiteSpace(ms.ClientId) && !string.IsNullOrWhiteSpace(ms.ClientSecret))
            {
                authBuilder.AddMicrosoftAccount(MicrosoftAccountDefaults.AuthenticationScheme, microsoft =>
                {
                    microsoft.ClientId = ms.ClientId;
                    microsoft.ClientSecret = ms.ClientSecret;
                    microsoft.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    microsoft.SaveTokens = false;
                    MicrosoftAccountTicketSync.Attach(microsoft);
                });
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"Unsupported authentication provider '{provider}'. Supported providers: JwtBearer");
        }

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.AdminOnly, policy => policy.RequireRole("admin"));
            options.AddPolicy(AuthPolicies.CustomerScope, policy => policy.Requirements.Add(new CustomerScopeRequirement()));
        });
        services.AddSingleton<IAuthorizationHandler, CustomerScopeAuthorizationHandler>();
        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiterOptions.AddPolicy("auth-token", context =>
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ip,
                    factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 8,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
            rateLimiterOptions.AddPolicy("signup", context =>
            {
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ip,
                    factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(10),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
            AddBillingRateLimitPolicies(rateLimiterOptions);
        });
        return services;
    }

    private static void AddBillingRateLimitPolicies(RateLimiterOptions rateLimiterOptions)
    {
        static string UserOrIpPartition(HttpContext context)
        {
            var sub = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.User?.FindFirst("sub")?.Value
                ?? context.User?.Identity?.Name;
            if (!string.IsNullOrEmpty(sub))
            {
                return $"user:{sub}";
            }

            return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        }

        rateLimiterOptions.AddPolicy("billing-summary", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                UserOrIpPartition(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));

        rateLimiterOptions.AddPolicy("billing-mutate", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                UserOrIpPartition(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 8,
                    Window = TimeSpan.FromMinutes(10),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
    }

    private static void ValidateJwtOptions(JwtAuthOptions jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt.SigningKey))
        {
            throw new InvalidOperationException("Authentication:Jwt:SigningKey is required.");
        }

        var keyBytes = Encoding.UTF8.GetByteCount(jwt.SigningKey);
        if (keyBytes < 32)
        {
            throw new InvalidOperationException("Authentication:Jwt:SigningKey must be at least 32 bytes.");
        }
    }
}

internal sealed class TestingAuthHandler(
    IOptionsMonitor<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : Microsoft.AspNetCore.Authentication.AuthenticationHandler<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>(
        options, logger, encoder)
{
    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new System.Security.Claims.ClaimsIdentity(
            [
                new("sub", "test-user"),
                new(System.Security.Claims.ClaimTypes.Name, "test-user"),
                new(System.Security.Claims.ClaimTypes.Role, "operator"),
                new(System.Security.Claims.ClaimTypes.Role, "admin"),
                new(AuthClaimTypes.CustomerAll, "true")
            ],
            Scheme.Name);
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(ticket));
    }
}
