using System.Globalization;
using FluentValidation;
using PoTraffic.API.Features.Config;
using PoTraffic.API.Infrastructure.Storage;

namespace PoTraffic.API.Features.Admin;

/// <summary>
/// Updates one administrator-editable configuration value. Only the keys in
/// <see cref="UpdateSystemConfigurationValidator.EditableKeys"/> can be changed here; every
/// other row (including anything sensitive) stays read-only.
/// </summary>
public sealed record UpdateSystemConfigurationCommand(string Key, string Value);

public sealed class UpdateSystemConfigurationValidator : AbstractValidator<UpdateSystemConfigurationCommand>
{
    /// <summary>Editable keys and the check each value must pass.</summary>
    public static readonly IReadOnlyDictionary<string, Func<string, bool>> EditableKeys =
        new Dictionary<string, Func<string, bool>>
        {
            ["quota.daily.default"] = v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                                           && n is >= 1 and <= SetUserDailyQuotaValidator.MaxQuota,
            [SpendingCap.MonthlyCapKey] = v => IsNonNegativeDecimal(v),
            [PollCostRates.GoogleMapsKey] = v => IsNonNegativeDecimal(v),
        };

    public UpdateSystemConfigurationValidator()
    {
        RuleFor(c => c.Key)
            .Must(EditableKeys.ContainsKey)
            .WithMessage("This setting cannot be changed here.");
        RuleFor(c => c.Value)
            .Must((c, v) => EditableKeys.TryGetValue(c.Key, out Func<string, bool>? check) && check(v.Trim()))
            .When(c => EditableKeys.ContainsKey(c.Key))
            .WithMessage("That is not a valid value for this setting.");
    }

    private static bool IsNonNegativeDecimal(string v) =>
        decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d) && d >= 0;
}

public sealed class UpdateSystemConfigurationHandler(TableStorageContext db)
{
    private static readonly UpdateSystemConfigurationValidator Validator = new();

    /// <summary>False when the row does not exist (editable rows are seeded at startup).</summary>
    public async Task<bool> Handle(UpdateSystemConfigurationCommand cmd, CancellationToken ct)
    {
        await Validator.ValidateAndThrowAsync(cmd, ct);

        SystemConfiguration? row = db.SystemConfigurations.FirstOrDefault(c => c.Key == cmd.Key);
        if (row is null)
            return false;

        row.Value = cmd.Value.Trim();
        await db.SaveChangesAsync(ct);
        return true;
    }
}
