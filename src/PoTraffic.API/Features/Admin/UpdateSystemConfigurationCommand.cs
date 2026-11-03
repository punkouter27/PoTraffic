using FluentValidation;
using PoTraffic.API.Features.Config;
using PoTraffic.API.Infrastructure.Storage;

namespace PoTraffic.API.Features.Admin;

/// <summary>
/// Updates one administrator-editable configuration value. Only the keys catalogued in
/// <see cref="SystemSettings.All"/> (cost rates, limits, feature flags) can be changed here;
/// every other row (including anything sensitive) stays read-only. Readers look the value up on
/// every use, so a change takes effect on the next poll or request — no restart.
/// </summary>
public sealed record UpdateSystemConfigurationCommand(string Key, string Value);

public sealed class UpdateSystemConfigurationValidator : AbstractValidator<UpdateSystemConfigurationCommand>
{
    public UpdateSystemConfigurationValidator()
    {
        RuleFor(c => c.Key)
            .Must(SystemSettings.ByKey.ContainsKey)
            .WithMessage("This setting cannot be changed here.");
        RuleFor(c => c.Value)
            .Must((c, v) => SystemSettings.ByKey.TryGetValue(c.Key, out SystemSetting? setting) && setting.IsValid(v.Trim()))
            .When(c => SystemSettings.ByKey.ContainsKey(c.Key))
            .WithMessage("That is not a valid value for this setting.");
    }
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