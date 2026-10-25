using Microsoft.Extensions.Options;
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

        services.AddOptions<AliyunSmsOptions>()
            .Bind(configuration.GetSection(AliyunSmsOptions.SectionName))
            .Validate<IOptions<SmsOptions>>((aliyun, sms) =>
                    environment.IsDevelopment()
                    || !string.Equals(sms.Value.Provider.Trim(), AliyunSmsSender.Name, StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(aliyun.SignName)
                    && new[] { sms.Value.LoginTemplate, sms.Value.PasswordResetTemplate, sms.Value.PhoneConfirmationTemplate }
                        .All(key => !string.IsNullOrWhiteSpace(aliyun.Templates.GetValueOrDefault(key)))
                    && AliyunSmsSender.IsConfigured,
                $"Sms:Provider=aliyun requires Sms:Aliyun:SignName, a Sms:Aliyun:Templates code for every template, "
                + $"and {AliyunSmsSender.KeyIdVariable} / {AliyunSmsSender.SecretVariable}.")
            .ValidateOnStart();
        services.AddHttpClient<ISmsSender, AliyunSmsSender>(client =>
            client.BaseAddress = new Uri("https://dypnsapi.aliyuncs.com/"));

        services.AddScoped<ISmsVerificationService, SmsVerificationService>();
        return services;
    }
}
