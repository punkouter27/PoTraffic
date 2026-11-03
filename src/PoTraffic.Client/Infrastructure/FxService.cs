using Microsoft.JSInterop;

namespace PoTraffic.Client.Infrastructure;

/// <summary>How much decorative motion the app is allowed to draw.</summary>
public enum MotionLevel
{
    /// <summary>Everything: the map's traffic flow animates and charts draw themselves in.</summary>
    Full,

    /// <summary>Effects still render, but nothing moves. Colour keeps its meaning; motion stops.</summary>
    Reduced,

    /// <summary>No decorative rendering at all.</summary>
    Off,
}

/// <summary>A synthesised sound cue (<c>js/pt-audio.js</c>).</summary>
public enum SoundCue
{
    /// <summary>A Check now came back. Pitch follows the result against its baseline.</summary>
    Check,

    /// <summary>A new alert arrived in the notification bell.</summary>
    Alert,

    /// <summary>A change was saved.</summary>
    Save,

    /// <summary>A pending action was taken back.</summary>
    Undo,
}

/// <summary>
/// The app's motion and sound settings, and the one bridge to <c>js/pt-fx.js</c>, which
/// owns every decorative effect at runtime.
///
/// <para>
/// One JS surface on purpose. The cues (<c>pt-audio.js</c>) and the burst
/// (<c>pt-burst.js</c>) are loaded by <c>pt-fx.js</c> on first use, so a muted user never
/// downloads the synth and a reduced-motion user never downloads the burst — and neither
/// costs anything on a page load.
/// </para>
///
/// <para>
/// The preference lives in <c>localStorage</c> on the JS side rather than in this class,
/// deliberately: <c>pt-fx.js</c> is imported before Blazor has finished booting, and an
/// effect that waits for .NET to tell it whether it may animate is one that flickers on at
/// second three of every page load.
/// </para>
///
/// <para>
/// The OS <c>prefers-reduced-motion</c> setting is a floor, not a default — see the note in
/// <c>pt-fx.js</c>. Someone who asked their system for less motion does not get full motion
/// back because this app's setting says "full".
/// </para>
/// </summary>
public sealed class FxService : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _fx;
    private bool _started;

    public FxService(IJSRuntime js) => _js = js;

    public MotionLevel Motion { get; private set; } = MotionLevel.Full;

    /// <summary>Whether the sound cues play. On by default; a per-browser choice, like motion.</summary>
    public bool SoundOn { get; private set; } = true;

    /// <summary>
    /// A result counts as "faster than usual" — and earns the burst — only when it beats
    /// the baseline by more than noise. Two percent of a 30-minute commute is 36 seconds.
    /// </summary>
    private const double FasterRatio = 0.98;

    /// <summary>
    /// Loads the effects module and reads the stored preference. Called once from the root
    /// layout; safe to call again.
    /// </summary>
    public async Task StartAsync()
    {
        if (_started)
            return;
        _started = true;

        try
        {
            _fx = await Import("./js/pt-fx.js");
            await ReadAsync();
        }
        catch (JSDisconnectedException) { /* page tearing down */ }
        catch (ObjectDisposedException) { /* page tearing down */ }
        catch (JSException)
        {
            // A blocked module or absent localStorage is not worth surfacing. The app
            // renders exactly as it did before the effects existed.
        }
    }

    private Task<IJSObjectReference> Import(string path) =>
        _js.InvokeAsync<IJSObjectReference>("import", path).AsTask();

    public async Task SetMotionAsync(MotionLevel level)
    {
        Motion = level;
        await Safe(() => _fx!.InvokeVoidAsync("setMotionLevel", Serialize(level)).AsTask());
    }

    public async Task SetSoundAsync(bool on)
    {
        SoundOn = on;
        await Safe(() => _fx!.InvokeVoidAsync("setSound", on).AsTask());
        // Switching sound on is itself a gesture, so it doubles as the confirmation.
        if (on)
            await CueAsync(SoundCue.Save);
    }

    /// <summary>Plays a cue. Silent when muted, before the first user gesture, or with the tab hidden.</summary>
    public Task CueAsync(SoundCue cue) => Cue(cue, null);

    /// <summary>
    /// The feedback for a Check now result: the check cue pitched by how the result sits
    /// against <paramref name="baselineSeconds"/>, and a particle burst when it beat it.
    /// With no baseline the cue plays at its neutral pitch and nothing bursts.
    /// </summary>
    public async Task CheckResultAsync(int durationSeconds, double? baselineSeconds)
    {
        double? ratio = baselineSeconds is > 0 ? durationSeconds / baselineSeconds.Value : null;
        await Cue(SoundCue.Check, ratio);
        if (ratio is < FasterRatio)
            await Safe(() => _fx!.InvokeVoidAsync("burst", "faster").AsTask());
    }

    private async Task Cue(SoundCue cue, double? ratio)
    {
        if (!SoundOn)
            return;
        await Safe(() => _fx!.InvokeVoidAsync("cue", cue.ToString().ToLowerInvariant(), ratio).AsTask());
    }

    private async Task ReadAsync()
    {
        if (_fx is null)
            return;

        Motion = Parse(await _fx.InvokeAsync<string>("motionLevel"));
        SoundOn = await _fx.InvokeAsync<bool>("soundOn");
    }

    /// <summary>
    /// Every effect call goes through here. Decoration must never be able to break a page:
    /// a missing module or a torn-down circuit both end the same way, which is quietly.
    /// </summary>
    private static async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (JSException) { }
        catch (NullReferenceException) { /* StartAsync never completed */ }
    }

    private static string Serialize(MotionLevel level) => level switch
    {
        MotionLevel.Reduced => "reduced",
        MotionLevel.Off => "off",
        _ => "full",
    };

    private static MotionLevel Parse(string? value) => value switch
    {
        "reduced" => MotionLevel.Reduced,
        "off" => MotionLevel.Off,
        _ => MotionLevel.Full,
    };

    public async ValueTask DisposeAsync()
    {
        if (_fx is null)
            return;

        try { await _fx.DisposeAsync(); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (JSException) { }
    }
}
