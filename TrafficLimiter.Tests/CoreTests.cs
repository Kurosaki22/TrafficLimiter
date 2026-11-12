using TrafficLimiter.Core;

namespace TrafficLimiter.Tests;

public class CoreTests
{
    [Fact]
    public void Bucket_AllowsBurstThenDelaysAtRate()
    {
        var b = new TokenBucket(100_000, 0); // Kapazität 65536
        Assert.Equal(0, b.Delay(60_000, 0));
        // 60k verbraucht, 5.5k übrig; weitere 50k -> 44.5k Schuld -> 0.445 s
        Assert.Equal(0.445, b.Delay(50_000, 0), 3);
        // nach 1 s ist die Schuld getilgt
        Assert.Equal(0, b.Delay(1000, 1.5));
    }

    [Fact]
    public void Bucket_ReserveDelaysLowPriorityFirst()
    {
        var high = new TokenBucket(100_000, 0);
        var low = new TokenBucket(100_000, 0);
        Assert.Equal(0, high.Delay(40_000, 0, 0));
        Assert.True(low.Delay(40_000, 0, 0.5) > 0); // Rest 25k < 50% von 65k
    }

    [Fact]
    public void Settings_Roundtrip()
    {
        var p = Path.GetTempFileName();
        var s = new Settings([new AppRule(@"C:\a.exe", 1000, 2000, true, Priority.High)], 5, 6, "en", true, true, false);
        RuleStore.Save(p, s);
        var l = RuleStore.Load(p);
        Assert.Equal(s.Rules[0], l.Rules[0]);
        Assert.Equal(6, l.GlobalUpLimit);
        Assert.Equal(("en", true, true, false), (l.Language, l.StartMinimized, l.Autostart, l.EngineEnabled));
        Assert.True(RuleStore.Load("nonexistent.json").EngineEnabled); // Standard: Engine an
        File.Delete(p);
        Assert.Empty(RuleStore.Load(p).Rules);
    }

    [Fact]
    public void Settings_Profiles_Roundtrip_And_OldFileDefaults()
    {
        var p = Path.GetTempFileName();
        var prof = new Profile("Gaming", [new AppRule(@"C:.exe", 500, 0, false, Priority.Critical)], 100, 50);
        RuleStore.Save(p, new Settings([], Profiles: [prof], ActiveProfile: "Gaming"));
        var l = RuleStore.Load(p);
        Assert.Equal(prof.Rules[0], l.Profiles![0].Rules[0]);
        Assert.Equal(("Gaming", 100L), (l.ActiveProfile, l.Profiles[0].GlobalDownLimit));
        // Datei aus älterer Version ohne Profile-Felder
        File.WriteAllText(p, "{\"Rules\":[],\"Language\":\"de\"}");
        var old = RuleStore.Load(p);
        Assert.Equal("de", old.Language);
        Assert.Null(old.Profiles);
        File.Delete(p);
    }
}
