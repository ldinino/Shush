using Shush.Core.Models;

namespace Shush.Tests;

public class GainMathTests
{
    private static void AssertClose(double expected, double actual, double tolerance = 1e-4)
        => Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"Expected {expected} ± {tolerance}, but got {actual}.");

    [Fact]
    public void DecibelsToLinear_ZeroDb_IsUnity()
        => AssertClose(1.0, GainMath.DecibelsToLinear(0.0));

    [Fact]
    public void DecibelsToLinear_Minus20Db_IsTenPercent()
        => AssertClose(0.1, GainMath.DecibelsToLinear(-20.0));

    [Fact]
    public void DecibelsToLinear_MinusSixDb_IsRoughlyHalf()
        => AssertClose(0.5, GainMath.DecibelsToLinear(-6.0205999), 1e-3);

    [Fact]
    public void DecibelsToLinear_FloorDb_IsOneThousandth()
        => AssertClose(0.001, GainMath.DecibelsToLinear(GainMath.MinDecibels));

    [Fact]
    public void DecibelsToLinear_AboveMax_ClampsToUnity()
        => AssertClose(GainMath.DecibelsToLinear(0.0), GainMath.DecibelsToLinear(12.0));

    [Fact]
    public void DecibelsToLinear_BelowMin_ClampsToFloor()
        => AssertClose(GainMath.DecibelsToLinear(GainMath.MinDecibels), GainMath.DecibelsToLinear(-120.0));

    [Fact]
    public void LinearToDecibels_Unity_IsZeroDb()
        => AssertClose(0.0, GainMath.LinearToDecibels(1.0));

    [Fact]
    public void LinearToDecibels_TenPercent_IsMinus20Db()
        => AssertClose(-20.0, GainMath.LinearToDecibels(0.1));

    [Fact]
    public void LinearToDecibels_Zero_ReturnsFloor()
        => Assert.Equal(GainMath.MinDecibels, GainMath.LinearToDecibels(0.0));

    [Fact]
    public void LinearToDecibels_Negative_ReturnsFloor()
        => Assert.Equal(GainMath.MinDecibels, GainMath.LinearToDecibels(-0.5));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-6.0)]
    [InlineData(-20.0)]
    [InlineData(-45.5)]
    [InlineData(-60.0)]
    public void DecibelsToLinear_LinearToDecibels_RoundTrips(double decibels)
        => AssertClose(decibels, GainMath.LinearToDecibels(GainMath.DecibelsToLinear(decibels)), 1e-3);

    [Theory]
    [InlineData(10.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(-30.0, -30.0)]
    [InlineData(-60.0, -60.0)]
    [InlineData(-999.0, -60.0)]
    public void ClampDecibels_KeepsValueInRange(double input, double expected)
        => Assert.Equal(expected, GainMath.ClampDecibels(input));

    [Fact]
    public void DecibelsToPercent_Minus20Db_IsTenPercent()
        => AssertClose(10.0, GainMath.DecibelsToPercent(-20.0));
}
