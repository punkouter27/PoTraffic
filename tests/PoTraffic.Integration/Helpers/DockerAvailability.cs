using System.Diagnostics;

namespace PoTraffic.Integration.Helpers;

/// <summary>
/// Whether a Docker daemon is reachable, probed once per test process.
///
/// <para>
/// The probe shells out to <c>docker info</c> with a 5-second timeout. xUnit constructs a
/// test's attribute during discovery, so probing inside the attribute's constructor ran that
/// command once per test method — on a machine without Docker that is a 5-second stall per
/// test, paid before a single assertion executes. <see cref="Lazy{T}"/> makes it once per run.
/// </para>
/// </summary>
internal static class DockerAvailability
{
    private static readonly Lazy<bool> Probe = new(ProbeDockerDaemon, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// True when <c>docker info</c> succeeds, i.e. Testcontainers can start Azurite. Always true in
    /// CI (<c>CI=true</c>): there the tier must run, so a missing Docker is a failure, not a skip.
    /// </summary>
    public static bool IsAvailable =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase) || Probe.Value;

    /// <summary>Reason shown on skipped tests when the daemon is unreachable.</summary>
    public const string SkipReason = "Docker is not running, so the Integration tier is skipped (it needs Docker for Testcontainers). Start Docker Desktop and run the tests again.";

    private static bool ProbeDockerDaemon()
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "info --format {{.ServerVersion}}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            return process.WaitForExit(20_000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
