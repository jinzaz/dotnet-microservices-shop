using FluentValidation;
using MSA.Infrastrucute.Mediator;

namespace MSA.CatalogService.Application;

public sealed record PromotionRuleInput(
    string RuleType,
    int SchemaVersion,
    string ConfigurationJson,
    int Sequence);

public sealed record PromotionBenefitInput(
    string BenefitType,
    int SchemaVersion,
    string ConfigurationJson,
    int Sequence);

public sealed record PromotionTargetInput(
    string TargetType,
    string? TargetKey,
    bool IsExcluded);

public sealed record CreatePromotionCommand(
    string Code,
    string Name,
    string TriggerType,
    DateTimeOffset StartsOnUtc,
    DateTimeOffset EndsOnUtc,
    int Priority,
    string StackPolicy,
    string? ExclusiveGroup,
    IReadOnlyList<PromotionRuleInput> Rules,
    IReadOnlyList<PromotionBenefitInput> Benefits,
    IReadOnlyList<PromotionTargetInput> Targets,
    string Actor) : ICommand<Guid>;

public sealed record ReplacePromotionDefinitionCommand(
    Guid PromotionId,
    string Name,
    DateTimeOffset StartsOnUtc,
    DateTimeOffset EndsOnUtc,
    int Priority,
    string StackPolicy,
    string? ExclusiveGroup,
    IReadOnlyList<PromotionRuleInput> Rules,
    IReadOnlyList<PromotionBenefitInput> Benefits,
    IReadOnlyList<PromotionTargetInput> Targets,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record PublishPromotionCommand(
    Guid PromotionId,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record PausePromotionCommand(
    Guid PromotionId,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record ResumePromotionCommand(
    Guid PromotionId,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record EndPromotionCommand(
    Guid PromotionId,
    long? ExpectedVersion,
    string Actor) : ICommand<bool>;

public sealed record GetPromotionQuery(Guid PromotionId) : IQuery<PromotionDto?>;

public sealed record ListPromotionsQuery(int Skip, int Take, string? Status)
    : IQuery<PagedResult<PromotionDto>>;

public sealed class CreatePromotionValidator : AbstractValidator<CreatePromotionCommand>
{
    public CreatePromotionValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(60);
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.TriggerType).Must(BeTriggerType);
        RuleFor(command => command.StackPolicy).Must(BeStackPolicy);
        RuleFor(command => command.Priority).InclusiveBetween(-10_000, 10_000);
        RuleFor(command => command.ExclusiveGroup).MaximumLength(80);
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
        RuleFor(command => command).Must(command => command.StartsOnUtc < command.EndsOnUtc)
            .WithMessage("Promotion end must be after its start.");
        RuleFor(command => command.Benefits).NotEmpty();
        RuleForEach(command => command.Rules).SetValidator(new PromotionRuleInputValidator());
        RuleForEach(command => command.Benefits).SetValidator(new PromotionBenefitInputValidator());
        RuleForEach(command => command.Targets).SetValidator(new PromotionTargetInputValidator());
    }

    private static bool BeTriggerType(string value) =>
        string.Equals(value, "Automatic", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "Coupon", StringComparison.OrdinalIgnoreCase);

    private static bool BeStackPolicy(string value) =>
        string.Equals(value, "Stackable", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "Exclusive", StringComparison.OrdinalIgnoreCase);
}

public sealed class ReplacePromotionDefinitionValidator : AbstractValidator<ReplacePromotionDefinitionCommand>
{
    public ReplacePromotionDefinitionValidator()
    {
        RuleFor(command => command.PromotionId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.StackPolicy)
            .Must(value => string.Equals(value, "Stackable", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Exclusive", StringComparison.OrdinalIgnoreCase));
        RuleFor(command => command.Priority).InclusiveBetween(-10_000, 10_000);
        RuleFor(command => command.ExclusiveGroup).MaximumLength(80);
        RuleFor(command => command.Actor).NotEmpty().MaximumLength(200);
        RuleFor(command => command).Must(command => command.StartsOnUtc < command.EndsOnUtc)
            .WithMessage("Promotion end must be after its start.");
        RuleFor(command => command.Benefits).NotEmpty();
        RuleForEach(command => command.Rules).SetValidator(new PromotionRuleInputValidator());
        RuleForEach(command => command.Benefits).SetValidator(new PromotionBenefitInputValidator());
        RuleForEach(command => command.Targets).SetValidator(new PromotionTargetInputValidator());
    }
}

internal sealed class PromotionRuleInputValidator : AbstractValidator<PromotionRuleInput>
{
    public PromotionRuleInputValidator()
    {
        RuleFor(rule => rule.RuleType).NotEmpty().MaximumLength(80);
        RuleFor(rule => rule.SchemaVersion).Equal(1);
        RuleFor(rule => rule).Must(PromotionConfigurationValidation.IsSupportedRule)
            .WithMessage("Supported rules are MinimumOrderAmount v1 {amount>0} and MinimumItemQuantity v1 {quantity>0}.");
        RuleFor(rule => rule.Sequence).GreaterThanOrEqualTo(0);
    }
}

internal sealed class PromotionBenefitInputValidator : AbstractValidator<PromotionBenefitInput>
{
    public PromotionBenefitInputValidator()
    {
        RuleFor(benefit => benefit.BenefitType).NotEmpty().MaximumLength(80);
        RuleFor(benefit => benefit.SchemaVersion).Equal(1);
        RuleFor(benefit => benefit).Must(PromotionConfigurationValidation.IsSupportedBenefit)
            .WithMessage("Supported benefits are PercentageOff v1 and FixedAmountOff v1.");
        RuleFor(benefit => benefit.Sequence).GreaterThanOrEqualTo(0);
    }
}

internal sealed class PromotionTargetInputValidator : AbstractValidator<PromotionTargetInput>
{
    public PromotionTargetInputValidator()
    {
        RuleFor(target => target.TargetType).NotEmpty().MaximumLength(40);
        RuleFor(target => target.TargetType).Must(value =>
                value.Equals("All", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Sku", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Product", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Category", StringComparison.OrdinalIgnoreCase))
            .WithMessage("TargetType must be All, Sku, Product, or Category.");
        RuleFor(target => target.TargetKey).MaximumLength(200);
        RuleFor(target => target).Must(target =>
                string.Equals(target.TargetType, "All", StringComparison.OrdinalIgnoreCase)
                    ? string.IsNullOrWhiteSpace(target.TargetKey)
                    : !string.IsNullOrWhiteSpace(target.TargetKey))
            .WithMessage("Only the All target may omit TargetKey.");
    }
}

file static class PromotionConfigurationValidation
{
    public static bool IsSupportedRule(PromotionRuleInput rule)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(rule.ConfigurationJson);
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object || rule.SchemaVersion != 1)
                return false;

            if (rule.RuleType.Equals("MinimumOrderAmount", StringComparison.OrdinalIgnoreCase))
                return TryPositiveDecimal(root, "amount", out _);
            if (rule.RuleType.Equals("MinimumItemQuantity", StringComparison.OrdinalIgnoreCase))
                return root.TryGetProperty("quantity", out var quantity)
                    && quantity.TryGetInt32(out var value)
                    && value > 0;
            return false;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    public static bool IsSupportedBenefit(PromotionBenefitInput benefit)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(benefit.ConfigurationJson);
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object || benefit.SchemaVersion != 1)
                return false;

            if (benefit.BenefitType.Equals("FixedAmountOff", StringComparison.OrdinalIgnoreCase))
                return TryPositiveDecimal(root, "amount", out _);

            if (!benefit.BenefitType.Equals("PercentageOff", StringComparison.OrdinalIgnoreCase)
                || !TryPositiveDecimal(root, "percentage", out var percentage)
                || percentage > 100)
                return false;

            return !root.TryGetProperty("maximumDiscount", out var maximumDiscount)
                || maximumDiscount.ValueKind == System.Text.Json.JsonValueKind.Null
                || maximumDiscount.TryGetDecimal(out var maximum) && maximum > 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static bool TryPositiveDecimal(
        System.Text.Json.JsonElement root,
        string propertyName,
        out decimal value)
    {
        value = 0;
        return root.TryGetProperty(propertyName, out var property)
            && property.TryGetDecimal(out value)
            && value > 0;
    }
}
