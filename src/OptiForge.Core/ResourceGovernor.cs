namespace OptiForge.Core;

public enum ResourceProfile
{
    Balanced,
    LowMemory,
    Performance,
    Manual
}

public enum MemoryPressure
{
    Normal,
    Caution,
    High,
    Critical
}

public sealed class ResourceAdvice
{
    public MemoryPressure Pressure { get; init; }
    public ResourceProfile Profile { get; init; }
    public int SuggestedContext { get; init; }
    public int SuggestedMaxTokens { get; init; }
    public bool PauseBackgroundJobs { get; init; }
    public string Summary { get; init; } = "";
}

/// <summary>Central adaptive limits based on measured available RAM.</summary>
public sealed class ResourceGovernor
{
    public ResourceProfile Profile { get; set; } = ResourceProfile.Balanced;

    public MemoryPressure Classify(double availableRamPercent)
    {
        // availableRamPercent = free/total * 100
        if (availableRamPercent <= 10) return MemoryPressure.Critical;
        if (availableRamPercent <= 15) return MemoryPressure.High;
        if (availableRamPercent <= 25) return MemoryPressure.Caution;
        return MemoryPressure.Normal;
    }

    public ResourceAdvice Advise(double availableRamPercent)
    {
        var pressure = Classify(availableRamPercent);
        var profile = Profile;

        if (profile != ResourceProfile.Manual)
        {
            if (pressure >= MemoryPressure.High)
                profile = ResourceProfile.LowMemory;
            else if (pressure == MemoryPressure.Normal && Profile == ResourceProfile.Performance)
                profile = ResourceProfile.Performance;
            else if (pressure == MemoryPressure.Caution)
                profile = ResourceProfile.Balanced;
        }

        var (ctx, maxTok, pause) = profile switch
        {
            ResourceProfile.LowMemory => (1024, 256, true),
            ResourceProfile.Performance => (4096, 768, false),
            ResourceProfile.Manual => (2048, 512, false),
            _ => (2048, 512, pressure >= MemoryPressure.High)
        };

        if (pressure == MemoryPressure.Critical)
        {
            ctx = Math.Min(ctx, 1024);
            maxTok = Math.Min(maxTok, 192);
            pause = true;
        }

        return new ResourceAdvice
        {
            Pressure = pressure,
            Profile = profile,
            SuggestedContext = ctx,
            SuggestedMaxTokens = maxTok,
            PauseBackgroundJobs = pause,
            Summary = $"Profile={profile}, pressure={pressure}, freeRAM≈{availableRamPercent:0}% → ctx {ctx}, maxTokens {maxTok}"
        };
    }
}
