using System.Text.Json;

namespace HudiPet;

internal readonly record struct DesktopEdgeCue(double ContactX, double MinX, double MaxX, int ContactStart, int ContactEnd,
    double? MinY, double? MaxY, double[]? StepPadding)
{
    internal static bool TryRead(JsonElement source, int width, int height, int steps, out DesktopEdgeCue cue)
    {
        cue = default;
        if (source.ValueKind != JsonValueKind.Object ||
            !source.TryGetProperty("side", out var side) || side.ValueKind != JsonValueKind.String || side.GetString() != "right")
            return false;
        if (!Read("contactX", out double contact) || !Read("minX", out double min) || !Read("maxX", out double max) ||
            min < 0 || min >= max || max >= width || contact < min || contact > max ||
            !Index("contactStart", out int start) || !Index("contactEnd", out int end) ||
            start < 0 || end <= start || end >= steps)
            return false;
        double? minY = null, maxY = null;
        if (Read("minY", out double top) && Read("maxY", out double bottom) &&
            top >= 0 && bottom > top && bottom < height)
        {
            minY = top;
            maxY = bottom;
        }
        double[]? stepPadding = null;
        if (source.TryGetProperty("stepMaxX", out var track) &&
            track.ValueKind == JsonValueKind.Array && track.GetArrayLength() == steps)
        {
            var positions = new List<double>(steps);
            foreach (var value in track.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double right) ||
                    !double.IsFinite(right) || right < min || right > max) break;
                positions.Add(Math.Max(0, right - contact));
            }
            if (positions.Count == steps)
            {
                stepPadding = positions.ToArray();
                // Anticipate the silhouette rather than abruptly shifting the whole window
                // on a single large stride. Each pass preserves the real per-frame envelope.
                for (int i = 1; i < stepPadding.Length; i++)
                    stepPadding[i] = Math.Max(stepPadding[i], stepPadding[i - 1] - 3);
                for (int i = stepPadding.Length - 2; i >= 0; i--)
                    stepPadding[i] = Math.Max(stepPadding[i], stepPadding[i + 1] - 3);
            }
        }
        cue = new(contact, min, max, start, end, minY, maxY, stepPadding);
        return true;

        bool Read(string name, out double number)
        {
            number = 0;
            return source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
                   value.TryGetDouble(out number) && double.IsFinite(number);
        }

        bool Index(string name, out int number)
        {
            number = 0;
            return source.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
                   value.TryGetInt32(out number);
        }
    }

    internal bool FitsVertically(double screenTop, double screenBottom, double windowTop, double scale,
        double bodyTop, double bodyBottom) =>
        (MinY is { } min ? windowTop + min * scale : bodyTop) >= screenTop &&
        (MaxY is { } max ? windowTop + (max + 1) * scale : bodyBottom) <= screenBottom + 1;

    internal bool TryFitVertical(double screenTop, double screenBottom, double windowTop, double scale,
        double bodyTop, double bodyBottom, double tolerance, out double top)
    {
        top = windowTop;
        if (scale <= 0) return false;
        if (FitsVertically(screenTop, screenBottom, windowTop, scale, bodyTop, bodyBottom)) return true;
        double first = screenTop - (MinY is { } min ? min * scale : bodyTop - windowTop);
        double last = screenBottom - (MaxY is { } max ? (max + 1) * scale : bodyBottom - windowTop);
        if (first > last) return false;
        double corrected = Math.Clamp(windowTop, first, last);
        if (Math.Abs(corrected - windowTop) > tolerance) return false;
        top = corrected;
        return true;
    }

    internal bool TryPlace(double screenLeft, double screenRight, double windowLeft, double scale,
        double bodyRight, double bodyWidth, double tolerance, out double left)
    {
        left = windowLeft;
        if (scale <= 0 || screenRight - bodyRight > Math.Max(tolerance, bodyWidth * .42))
            return false;
        double overshoot = (MaxX - ContactX) * scale;
        // Align to the furthest opaque pixel if it differs only slightly from the contact.
        // A larger mismatch means the clip cannot touch the edge without being clipped.
        if (overshoot > tolerance * (StepPadding is null ? .22 : .68)) return false;
        double target = screenRight - ((StepPadding is null ? MaxX : ContactX) + 1) * scale;
        if (target + MinX * scale - (StepPadding is null ? 0 : overshoot) < screenLeft ||
            Math.Abs(target - windowLeft) > Math.Max(tolerance, bodyWidth * .75))
            return false;
        left = target;
        return true;
    }

    internal double PaddingAt(int step) => StepPadding is not null && step >= 0 && step < StepPadding.Length
        ? StepPadding[step] : 0;
}
