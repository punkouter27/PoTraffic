using System.Globalization;
using PoTraffic.Shared.DTOs.History;

namespace PoTraffic.Mobile.Pages;

/// <summary>A line of travel times over time (the trend chart and, small, the card sparkline).</summary>
public sealed class LineChart(IReadOnlyList<(DateTime At, double Minutes)> points, bool axes) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (points.Count < 2)
        {
            return;
        }

        float left = axes ? 34 : 2, bottom = axes ? 18 : 2;
        RectF plot = new(dirtyRect.X + left, dirtyRect.Y + 4, dirtyRect.Width - left - 4, dirtyRect.Height - bottom - 8);
        double minY = points.Min(p => p.Minutes), maxY = points.Max(p => p.Minutes);
        if (maxY - minY < 1)
        {
            maxY = minY + 1;
        }

        long minX = points[0].At.Ticks, spanX = Math.Max(1, points[^1].At.Ticks - minX);
        PointF At((DateTime At, double Minutes) p) => new(
            plot.X + (float)((p.At.Ticks - minX) / (double)spanX * plot.Width),
            plot.Bottom - (float)((p.Minutes - minY) / (maxY - minY) * plot.Height));

        if (axes)
        {
            canvas.FontSize = 10;
            canvas.FontColor = Colors.Gray;
            canvas.DrawString($"{maxY:F0}m", dirtyRect.X, plot.Y - 4, left - 4, 12, HorizontalAlignment.Right, VerticalAlignment.Top);
            canvas.DrawString($"{minY:F0}m", dirtyRect.X, plot.Bottom - 10, left - 4, 12, HorizontalAlignment.Right, VerticalAlignment.Top);
            canvas.DrawString(points[0].At.ToString("h:mm tt", CultureInfo.CurrentCulture), plot.X, plot.Bottom + 4, 80, 12, HorizontalAlignment.Left, VerticalAlignment.Top);
            canvas.DrawString(points[^1].At.ToString("h:mm tt", CultureInfo.CurrentCulture), plot.Right - 80, plot.Bottom + 4, 80, 12, HorizontalAlignment.Right, VerticalAlignment.Top);
            canvas.StrokeColor = Colors.Gray.WithAlpha(0.3f);
            canvas.DrawLine(plot.X, plot.Bottom, plot.Right, plot.Bottom);
        }

        PathF path = new();
        path.MoveTo(At(points[0]));
        foreach (var p in points.Skip(1))
        {
            path.LineTo(At(p));
        }

        canvas.StrokeColor = Ui.Accent;
        canvas.StrokeSize = axes ? 2.5f : 1.5f;
        canvas.DrawPath(path);
    }

    public static GraphicsView View(IEnumerable<PollRecordDto> polls, double height, bool axes)
        => new()
        {
            HeightRequest = height,
            Drawable = new LineChart([.. polls.OrderBy(p => p.PolledAt).Select(p => (p.PolledAt.LocalDateTime, p.TravelDurationSeconds / 60.0))], axes),
        };
}

/// <summary>Day × hour grid of mean travel time against the route's median (the web volatility heatmap).</summary>
public sealed class HeatmapChart(VolatilityHeatmapDto map) : IDrawable
{
    private static readonly string[] DayOrder = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        const float labelW = 30, labelH = 14;
        float cellW = (dirtyRect.Width - labelW) / 24f, cellH = (dirtyRect.Height - labelH) / 7f;
        canvas.FontSize = 9;
        canvas.FontColor = Colors.Gray;
        double median = Math.Max(1, map.MedianDurationSeconds);

        for (int d = 0; d < 7; d++)
        {
            canvas.DrawString(DayOrder[d][..3], dirtyRect.X, dirtyRect.Y + d * cellH, labelW - 2, cellH, HorizontalAlignment.Left, VerticalAlignment.Center);
            for (int h = 0; h < 24; h++)
            {
                var cells = map.Cells.Where(c => c.Hour == h && c.DayOfWeek.StartsWith(DayOrder[d][..3], StringComparison.OrdinalIgnoreCase) && c.SampleCount > 0).ToList();
                RectF cell = new(dirtyRect.X + labelW + h * cellW, dirtyRect.Y + d * cellH, cellW - 1, cellH - 1);
                if (cells.Count == 0)
                {
                    canvas.FillColor = Colors.Gray.WithAlpha(0.12f);
                }
                else
                {
                    double mean = cells.Sum(c => c.MeanDurationSeconds * c.SampleCount) / cells.Sum(c => c.SampleCount);
                    double ratio = Math.Clamp((mean / median - 0.85) / 0.5, 0, 1); // 0.85× median = calm, 1.35× = red
                    canvas.FillColor = Color.FromRgba(0.95f, (float)(0.85 - ratio * 0.65), (float)(0.3 - ratio * 0.2), 0.35f + (float)ratio * 0.65f);
                }

                canvas.FillRectangle(cell);
            }
        }

        foreach (int h in new[] { 0, 6, 12, 18 })
        {
            canvas.DrawString(h == 0 ? "12a" : h == 12 ? "12p" : h < 12 ? $"{h}a" : $"{h - 12}p", dirtyRect.X + labelW + h * cellW, dirtyRect.Bottom - labelH, 30, labelH, HorizontalAlignment.Left, VerticalAlignment.Bottom);
        }
    }
}
