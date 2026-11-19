using System.Globalization;

namespace PoTraffic.Mobile.Services;

/// <summary>The web client's display rules (TextFormatting + LocalTimeFormatter), so both read the same.</summary>
public static class Format
{
    public static int Minutes(double seconds) => (int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero);

    public static string SamplesSoFar(int count) => count == 0 ? "No samples yet" : $"{count} sample{(count == 1 ? "" : "s")} so far";

    public static string MinuteRange(int lowSeconds, int highSeconds)
    {
        int low = Minutes(lowSeconds), high = Minutes(highSeconds);
        return low == high ? $"{low} min" : $"{low}–{high} min";
    }

    public static string Street(string? address) => TitleCase(address?.Split(',')[0]);

    public static string Distance(int metres)
    {
        string region = RegionInfo.CurrentRegion.TwoLetterISORegionName;
        return region is "US" or "GB" or "LR" or "MM" ? $"{metres * 0.000621371:F1} mi" : $"{metres / 1000.0:F1} km";
    }

    public static string WallClock(string hhmm) =>
        TimeOnly.TryParse(hhmm, CultureInfo.InvariantCulture, out TimeOnly t) ? t.ToString("h:mm tt", CultureInfo.CurrentCulture) : hhmm;

    public static string WindowEnd(string hhmm) =>
        TimeOnly.TryParse(hhmm, CultureInfo.InvariantCulture, out TimeOnly t) && t == TimeOnly.MinValue ? "midnight" : WallClock(hhmm);

    public static string Clock(DateTime local) => local.ToString("h:mm tt", CultureInfo.CurrentCulture);

    public static string HHmm(TimeSpan? time) => $"{time.GetValueOrDefault().Hours:00}:{time.GetValueOrDefault().Minutes:00}";

    public static (double Lat, double Lng)? Coordinates(string? latLng)
    {
        string[] parts = (latLng ?? string.Empty).Split(',');
        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lng)
            ? (lat, lng)
            : null;
    }

    public static readonly (string Label, byte Bit, DayOfWeek Day)[] Days =
    [
        ("Mon", 0x01, DayOfWeek.Monday), ("Tue", 0x02, DayOfWeek.Tuesday), ("Wed", 0x04, DayOfWeek.Wednesday),
        ("Thu", 0x08, DayOfWeek.Thursday), ("Fri", 0x10, DayOfWeek.Friday), ("Sat", 0x20, DayOfWeek.Saturday), ("Sun", 0x40, DayOfWeek.Sunday),
    ];

    public static IEnumerable<DayOfWeek> DaysOf(IEnumerable<string> names)
        => Days.Where(d => names.Any(n => n.StartsWith(d.Label, StringComparison.OrdinalIgnoreCase))).Select(d => d.Day);

    public static string DaysLabel(IEnumerable<string> names)
    {
        bool[] on = [.. Days.Select(d => names.Any(n => n.StartsWith(d.Label, StringComparison.OrdinalIgnoreCase)))];
        if (on.All(x => x))
        {
            return "Every day";
        }

        List<string> parts = [];
        for (int i = 0; i < Days.Length;)
        {
            if (!on[i])
            {
                i++;
                continue;
            }

            int j = i;
            while (j + 1 < Days.Length && on[j + 1])
            {
                j++;
            }

            parts.Add(j - i >= 2 ? $"{Days[i].Label}–{Days[j].Label}" : string.Join(", ", Days[i..(j + 1)].Select(d => d.Label)));
            i = j + 1;
        }

        return string.Join(", ", parts);
    }

    public static string TitleCase(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string[] tokens = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            string t = tokens[i];
            if (char.IsDigit(t[0]) || (t.Length == 2 && t.All(c => !char.IsLetter(c) || char.IsUpper(c))))
            {
                continue;
            }

            tokens[i] = t.Length == 1 ? t.ToUpperInvariant() : char.ToUpperInvariant(t[0]) + t[1..].ToLowerInvariant();
        }

        return string.Join(' ', tokens);
    }

    /// <summary>Decodes a Google encoded polyline into points.</summary>
    public static List<(double Lat, double Lng)> DecodePolyline(string encoded)
    {
        List<(double, double)> points = [];
        int index = 0, lat = 0, lng = 0;
        while (index < encoded.Length)
        {
            lat += Next();
            lng += Next();
            points.Add((lat / 1e5, lng / 1e5));
        }

        return points;

        int Next()
        {
            int result = 0, shift = 0, b;
            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            }
            while (b >= 0x20 && index < encoded.Length);
            return (result & 1) != 0 ? ~(result >> 1) : result >> 1;
        }
    }
}
