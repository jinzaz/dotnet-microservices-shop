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
            .Validate(options => !string.IsNullOrWhiteSpace(options.PasswordResetTemplate),
                "Sms:PasswordResetTemplate is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.PhoneConfirmationTemplate),
                "Sms:PhoneConfirmationTemplate is required.")
            .Validate(options => !environment.IsDevelopment()
                || options.DevelopmentCode.Length == 6 && options.DevelopmentCode.All(char.IsAsciiDigit),
                "Sms:DevelopmentCode must contain exactly six digits in Development.")
            .ValidateOnStart();

        services.AddScoped<ISmsVerificationService, SmsVerificationService>();
        return services;
    }
}
