namespace PoTraffic.Client.Services;

/// <summary>
/// Holds a delete for a grace period so the user can actually take it back.
///
/// <para>
/// The previous flow issued the DELETE immediately and then showed a toast reading
/// "Undo within 5 seconds…" — with nothing to click and nothing to undo. This inverts
/// it: the route disappears from the list at once (optimistic, so the gesture still
/// feels instant), but the request is only sent when the grace period expires. Undo
/// cancels before anything leaves the browser.
/// </para>
///
/// <para>
/// Hidden and awaiting-commit are tracked separately on purpose. A route stays hidden
/// after its DELETE succeeds, right up until the owning page refreshes — collapsing the
/// two would make the card flash back onto the dashboard for the moment between the
/// request completing and the new route list arriving.
/// </para>
///
/// <para>
/// Generic over the key: it knows nothing about routes, only that something with that id
/// is hidden and may be restored. Keys must have value equality (the strongly-typed ids do).
/// Scoped per user session and observable, so the dashboard can filter its list and a
/// single undo bar in the layout can render the countdown, without either of them
/// owning the timer.
/// </para>
/// </summary>
public sealed class PendingDeletionService : IDisposable
{
    /// <summary>How long the user has to change their mind.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(6);

    private readonly Dictionary<object, CancellationTokenSource> _awaitingCommit = [];
    private readonly HashSet<object> _hidden = [];

    /// <summary>Raised whenever a deletion is scheduled, undone, committed, or fails.</summary>
    public event Action? Changed;

    /// <summary>The deletion the undo bar is currently offering, if any.</summary>
    public PendingDeletion? Current { get; private set; }

    /// <summary>True while <paramref name="key"/> should be kept out of lists.</summary>
    public bool IsPending(object key) => _hidden.Contains(key);

    /// <summary>
    /// Hides <paramref name="key"/> immediately and runs <paramref name="commit"/> once
    /// the grace period elapses. A route already scheduled is left alone.
    /// </summary>
    /// <param name="label">Human-readable description shown in the undo bar.</param>
    /// <param name="commit">The real delete request. Runs only if the user does not undo.</param>
    /// <param name="onCommitted">Invoked after a successful commit so the owner can refresh.</param>
    /// <param name="onFailed">Invoked when the commit throws; the route is restored first.</param>
    public void Schedule(
        object key,
        string label,
        Func<Task> commit,
        Func<Task>? onCommitted = null,
        Func<Task>? onFailed = null)
    {
        if (_awaitingCommit.ContainsKey(key))
            return;

        CancellationTokenSource cts = new();
        _awaitingCommit[key] = cts;
        _hidden.Add(key);
        Current = new PendingDeletion(key, label, DateTimeOffset.UtcNow + GracePeriod);
        Changed?.Invoke();

        _ = RunAsync(key, commit, onCommitted, onFailed, cts.Token);
    }

    /// <summary>Cancels the pending delete for <paramref name="key"/>, restoring it.</summary>
    public void Undo(object key)
    {
        if (!_awaitingCommit.Remove(key, out CancellationTokenSource? cts))
            return;

        Cancel(cts);
        cts.Dispose();
        _hidden.Remove(key);
        ClearCurrent(key);
        Changed?.Invoke();
    }

    private async Task RunAsync(
        object key,
        Func<Task> commit,
        Func<Task>? onCommitted,
        Func<Task>? onFailed,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(GracePeriod, ct);
        }
        catch (OperationCanceledException)
        {
            // Undone — nothing was ever sent.
            return;
        }

        // The window has closed: retire the undo bar, but keep the route hidden.
        if (_awaitingCommit.Remove(key, out CancellationTokenSource? done))
            done.Dispose();
        ClearCurrent(key);
        Changed?.Invoke();

        try
        {
            await commit();
        }
        catch
        {
            // Put the route back rather than leaving a phantom gap in the list.
            _hidden.Remove(key);
            Changed?.Invoke();

            if (onFailed is not null)
            {
                try { await onFailed(); } catch { /* reporting is best-effort */ }
            }
            return;
        }

        if (onCommitted is not null)
        {
            try { await onCommitted(); } catch { /* refresh is best-effort */ }
        }
    }

    private void ClearCurrent(object key)
    {
        if (Current is not null && Equals(Current.Key, key))
            Current = null;
    }

    private static void Cancel(CancellationTokenSource cts)
    {
        try { cts.Cancel(); } catch (ObjectDisposedException) { /* already finished */ }
    }

    public void Dispose()
    {
        foreach (CancellationTokenSource cts in _awaitingCommit.Values)
        {
            Cancel(cts);
            cts.Dispose();
        }
        _awaitingCommit.Clear();
        _hidden.Clear();
        Current = null;
    }
}

/// <summary>A delete awaiting commit, as rendered by the undo bar.</summary>
/// <param name="Key">What is hidden — any value-equal id (a RouteId today).</param>
/// <param name="Label">Human-readable description, e.g. "Home → Office".</param>
/// <param name="ExpiresAtUtc">When the grace period runs out and the request is sent.</param>
public sealed record PendingDeletion(object Key, string Label, DateTimeOffset ExpiresAtUtc);
