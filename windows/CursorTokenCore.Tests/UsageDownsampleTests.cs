using System;
using System.IO;
using System.Collections.Generic;
using Xunit;

namespace CursorTokenCore.Tests;

public class UsageDownsampleTests {
    [Fact]
    public void DownsamplePreservesFirstAndLastAndLimitsCount() {
        var list = new List<HistoryPoint>();
        var now = 1700000000.0;
        for (var i = 0; i < 5000; i++) {
            list.Add(new HistoryPoint(now + i * 60, 100.0 - (i * 0.01), null, null));
        }
        var ds = UsageHistory.Downsample(list, 100);
        Assert.True(ds.Count <= 100);
        Assert.Equal(list[0].Ts, ds[0].Ts);
        Assert.Equal(list[^1].Ts, ds[^1].Ts);
    }

    [Fact]
    public void DeduplicatesPointsWithin15MinutesIfSame() {
        var list = new List<HistoryPoint> {
            new(1000, 80, null, null),
            new(1060, 80, null, null), // +1 min, same -> should be skipped
            new(1120, 80, null, null), // +2 min, same -> should be skipped
            new(2000, 80, null, null), // +1000s (>15m) -> kept
            new(2060, 79, null, null), // changed -> kept
            new(3000, 79, null, null), // last -> kept
        };
        var ds = UsageHistory.Downsample(list, 100);
        Assert.Equal(4, ds.Count);
        Assert.Equal(1000, ds[0].Ts);
        Assert.Equal(2000, ds[1].Ts);
        Assert.Equal(2060, ds[2].Ts);
        Assert.Equal(3000, ds[3].Ts);
    }
}
