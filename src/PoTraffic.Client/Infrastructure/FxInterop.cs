using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace PoTraffic.Client.Infrastructure;

/// <summary>
/// Typed-array interop for the chart and the 3D heatmap, through
/// <see cref="JSImportAttribute"/> rather than <c>IJSRuntime</c>.
///
/// <para>
/// WHY: both draw from a few hundred doubles that change on every refresh. Through
/// <c>IJSRuntime</c> each refresh serialised them to JSON text, shipped the string and
/// parsed it back into JS numbers. Here the doubles cross as a <see cref="Span{T}"/>
/// marshalled as a memory view over WASM memory, which the JS side copies straight into
/// a <c>Float64Array</c> — no text, no parse, no intermediate object graph.
/// </para>
///
/// <para>
/// The modules are imported by absolute URL, the same URL the components' own
/// <c>IJSRuntime</c> imports resolve to, so both mechanisms share ONE module instance:
/// the pointer handlers <c>pt-chart.js</c> attaches through <c>IJSRuntime</c> (they need a
/// <c>DotNetObjectReference</c>, which [JSImport] does not carry) see the data this class
/// renders. Element references do not cross [JSImport] either, so each canvas is found by a
/// <c>data-pt-chart</c> / <c>data-pt-terrain</c> id instead.
/// </para>
///
/// <para>
/// The generated marshalling code needs <c>AllowUnsafeBlocks</c> (SYSLIB1074), which is why
/// the client project enables it; nothing in this codebase writes <c>unsafe</c> itself.
/// </para>
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class FxInterop
{
    private const string Chart = "pt-chart";
    private const string Terrain = "pt-terrain";

    private static Task? _chart;
    private static Task? _terrain;

    /// <summary>Imports pt-chart.js once per session; a failed import is retried on the next call.</summary>
    public static Task EnsureChartAsync(string baseUri) => Load(ref _chart, Chart, baseUri, "js/pt-chart.js");

    /// <summary>Imports pt-terrain.js once per session; a failed import is retried on the next call.</summary>
    public static Task EnsureTerrainAsync(string baseUri) => Load(ref _terrain, Terrain, baseUri, "js/pt-terrain.js");

    private static Task Load(ref Task? slot, string name, string baseUri, string path)
    {
        if (slot is { IsFaulted: true } or { IsCanceled: true })
            slot = null;
        return slot ??= JSHost.ImportAsync(name, new Uri(new Uri(baseUri), path).AbsoluteUri);
    }

    /// <summary>Weather condition → the code <c>renderTyped</c> in pt-chart.js maps back. 0 is "not shaded".</summary>
    public static byte WeatherCode(string? condition) => condition switch
    {
        "Rain" => 1,
        "Snow" => 2,
        "Storm" => 3,
        "Fog" => 4,
        _ => 0,
    };

    [JSImport("renderTyped", Chart)]
    public static partial void RenderChart(
        string id,
        [JSMarshalAs<JSType.MemoryView>] Span<double> points,
        [JSMarshalAs<JSType.MemoryView>] Span<double> precipitation,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> weather,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] labels);

    [JSImport("renderTyped", Terrain)]
    public static partial bool RenderTerrain(
        string id,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] cols,
        [JSMarshalAs<JSType.Array<JSType.String>>] string[] rows,
        [JSMarshalAs<JSType.MemoryView>] Span<double> ratio,
        [JSMarshalAs<JSType.MemoryView>] Span<double> cv);

    [JSImport("destroyById", Terrain)]
    public static partial void DestroyTerrain(string id);
}
