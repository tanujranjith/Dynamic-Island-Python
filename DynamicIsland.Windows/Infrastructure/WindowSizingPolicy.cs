namespace DynamicIsland.Windows.Infrastructure;

public static class WindowSizingPolicy
{
    public static double EffectiveDimension(double requested, double actual)
    {
        if (!double.IsNaN(requested) && !double.IsInfinity(requested) && requested > 0)
            return requested;
        return actual > 0 ? actual : 1d;
    }

    public static double AntiClippingDimension(double configured, double desired, double maximum)
    {
        var safeMaximum = Math.Max(configured, maximum);
        return Math.Clamp(Math.Max(configured, desired), configured, safeMaximum);
    }
}
