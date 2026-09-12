using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using MSA.IdentityService.Application.Sms;
using MSA.IdentityService.Infrastructure.Sms;

namespace Microsoft.Extensions.DependencyInjection;

internal static class SmsServiceCollectionExtensions
{
    internal static IServiceCollection AddSmsLogin(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<SmsOptions>()
            .Bind(configuration.GetSection(SmsOptions.SectionName))
            .Validate(options => options.CodeLifetimeSeconds is >= 60 and <= 600,
                "Sms:CodeLifetimeSeconds must be between 60 and 600.")
            .Validate(options => options.ResendCooldownSeconds is >= 30 and <= 300,
                "Sms:ResendCooldownSeconds must be between 30 and 300.")
            .Validate(options => options.MaxSendsPerHour is >= 1 and <= 20,
                "Sms:MaxSendsPerHour must be between 1 and 20.")
            .Validate(options => options.MaxVerificationAttempts is >= 1 and <= 10,
                "Sms:MaxVerificationAttempts must be between 1 and 10.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.LoginTemplate),
                "Sms:LoginTemplate is required.")
            .Validate(options => !environment.IsDevelopment()
                || options.DevelopmentCode.Length == 6 && options.DevelopmentCode.All(char.IsAsciiDigit),
                "Sms:DevelopmentCode must contain exactly six digits in Development.")
            .ValidateOnStart();

        services.AddScoped<ISmsVerificationService, SmsVerificationService>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(IdentityServiceConstants.RateLimits.Authentication, context =>
                CreateLimiter(context, permitLimit: 10, TimeSpan.FromMinutes(1)));
            options.AddPolicy(IdentityServiceConstants.RateLimits.Registration, context =>
                CreateLimiter(context, permitLimit: 5, TimeSpan.FromHours(1)));
            options.AddPolicy(IdentityServiceConstants.RateLimits.SmsDispatch, context =>
                CreateLimiter(context, permitLimit: 5, TimeSpan.FromMinutes(10)));
        });

        return services;
    }

    private static RateLimitPartition<string> CreateLimiter(
        HttpContext context,
        int permitLimit,
        TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                AutoReplenishment = true,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
}
