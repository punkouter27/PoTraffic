using PoTraffic.API.Infrastructure.Storage;
using PoTraffic.Shared.Enums;

namespace PoTraffic.API.Features.Account;

/// <summary>The advanced panels the user has switched on, by <see cref="AdvancedPanel"/> name.</summary>
public sealed record GetAdvancedPanelsQuery(UserId UserId);

public sealed class GetAdvancedPanelsHandler(TableStorageContext db)
{
    /// <summary>Null when the user doesn't exist. Names no longer in the enum are dropped.</summary>
    public Task<string[]?> Handle(GetAdvancedPanelsQuery query, CancellationToken ct)
    {
        User? user = db.Users.FirstOrDefault(u => u.Id == query.UserId);
        string[]? panels = user is null
            ? null
            : [.. user.AdvancedPanels.Where(p => Enum.TryParse<AdvancedPanel>(p, out _))];
        return Task.FromResult(panels);
    }
}

/// <summary>Switches one advanced panel on or off for the user.</summary>
public sealed record SetAdvancedPanelCommand(UserId UserId, AdvancedPanel Panel, bool On);

public sealed class SetAdvancedPanelHandler(TableStorageContext db)
{
    /// <summary>False when the user doesn't exist. Writes only when the value actually changes.</summary>
    public async Task<bool> Handle(SetAdvancedPanelCommand command, CancellationToken ct)
    {
        User? user = db.Users.FirstOrDefault(u => u.Id == command.UserId);
        if (user is null)
            return false;

        string name = command.Panel.ToString();
        bool isOn = user.AdvancedPanels.Contains(name);
        if (isOn == command.On)
            return true;

        if (command.On) user.AdvancedPanels.Add(name);
        else user.AdvancedPanels.Remove(name);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
