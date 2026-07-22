namespace Shush.Core.Models;

/// <summary>
/// Pure decibel &lt;-&gt; linear gain conversions used to drive per-app session volume.
/// Windows session volume is a linear float in [0, 1]; humans perceive loudness
/// logarithmically, so the slider works in dB and we convert here.
/// </summary>
public static class GainMath
{
    /// <summary>Quietest attenuation exposed by the slider (~0.1% linear gain).</summary>
    public const double MinDecibels = -60.0;

    /// <summary>Loudest setting: 0 dB == unity gain == no extra attenuation.</summary>
    public const double MaxDecibels = 0.0;

    /// <summary>Clamps a decibel value into the supported [MinDecibels, MaxDecibels] range.</summary>
    public static double ClampDecibels(double decibels)
        => Math.Clamp(decibels, MinDecibels, MaxDecibels);

    /// <summary>
    /// Converts a decibel value to a linear gain in [0, 1] suitable for
    /// <c>SimpleAudioVolume.Volume</c>. Input is clamped to the supported range first.
    /// </summary>
    public static float DecibelsToLinear(double decibels)
    {
        decibels = ClampDecibels(decibels);
        return (float)Math.Pow(10.0, decibels / 20.0);
    }

    /// <summary>
    /// Converts a linear gain to decibels, clamped to the supported range.
    /// A gain of zero (or below) maps to <see cref="MinDecibels"/>.
    /// </summary>
    public static double LinearToDecibels(double linear)
    {
        if (linear <= 0.0)
        {
            return MinDecibels;
        }

        return ClampDecibels(20.0 * Math.Log10(linear));
    }

    /// <summary>Convenience: decibels expressed as a percentage of unity gain (0 dB == 100%).</summary>
    public static double DecibelsToPercent(double decibels)
        => DecibelsToLinear(decibels) * 100.0;
}
