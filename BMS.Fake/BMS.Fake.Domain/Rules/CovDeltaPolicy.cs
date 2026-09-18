namespace BMS.Fake.Domain.Rules;

/// <summary>Pure rule that converts a simulator roll into a point delta.</summary>
public static class CovDeltaPolicy
{
    public static decimal Calculate(string objectType, int randomRoll) =>
        string.Equals(objectType, "Temperature", StringComparison.OrdinalIgnoreCase)
            ? randomRoll / 10m
            : randomRoll / 100m;
}
