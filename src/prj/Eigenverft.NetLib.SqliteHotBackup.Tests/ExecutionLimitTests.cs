using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Eigenverft.NetLib.SqliteHotBackup.Tests;

[TestClass]
public sealed class ExecutionLimitTests
{
    [TestMethod]
    public void ProductiveCopyHasNoOverallDeadlineAndGrowthAloneDoesNotRefreshClock()
    {
        ManualTime time = new();
        ProgressStallState state = new(new(), time);
        for (int copied = 1; copied <= 60; copied++)
        {
            time.Advance(TimeSpan.FromMinutes(4));
            state.Observe(100, 100 - copied);
            Assert.IsNull(state.ExceededReason());
        }
        time.Advance(TimeSpan.FromMinutes(4));
        state.Observe(1000, 940);
        Assert.IsNull(state.ExceededReason());
        time.Advance(TimeSpan.FromMinutes(1));
        StringAssert.Contains(state.ExceededReason()!, "no copied-prefix advance");
        Assert.AreEqual(1000, state.LargestPageCount);
        Assert.AreEqual(940, state.RemainingPages);
    }

    [TestMethod]
    public void RecopiedProgressDoesNotEraseRewindDebtAndEqualityIsAllowed()
    {
        ProgressStallState state = new(new(), new ManualTime());
        state.Observe(1000, 0);
        state.Observe(1000, 1000);
        state.Observe(1000, 0);
        state.Observe(1000, 1000);
        Assert.AreEqual(2000, state.RewoundPages);
        Assert.IsNull(state.ExceededReason());
        state.Observe(1000, 999);
        state.Observe(1000, 1000);
        Assert.AreEqual(2001, state.RewoundPages);
        StringAssert.Contains(state.ExceededReason()!, "rewind work");
        state.Observe(10000, 10000);
        Assert.IsNull(state.ExceededReason());
        Assert.AreEqual(10000, state.TotalPages);
        Assert.AreEqual(2001, state.RewoundPages);
    }

    [TestMethod]
    public void AttemptsHaveIndependentStateAndSnapshotTheirSettings()
    {
        ManualTime time = new();
        ProgressStallLimitCalculation recipe = new();
        ProgressStallState first = new(recipe, time, TimeSpan.FromMinutes(4));
        recipe.NoProgressTimeout = TimeSpan.FromSeconds(1);
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.IsNull(first.ExceededReason());
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.IsNotNull(first.ExceededReason());
        ProgressStallState next = new(new(), time);
        Assert.IsNull(next.ExceededReason());
        next.Observe(10, 9);
        time.Advance(TimeSpan.FromMinutes(4));
        Assert.IsNull(next.ExceededReason());
        Assert.AreEqual(0, next.RewoundPages);
    }

    [TestMethod]
    public void PartialOverridesInheritFinalGlobalSettingsWithoutMutatingThem()
    {
        SqliteHotBackupOptions options = new();
        options.ConfigureExecutionLimit(" archive ", limit => limit.NoProgressTimeout = TimeSpan.FromMinutes(9));
        options.ConfigureExecutionLimit("ARCHIVE", limit => limit.NoProgressTimeout += TimeSpan.FromMinutes(1));
        options.ExecutionLimit.MaxRewindWorkFactor = 4;
        SqliteHotBackupOptions copy = options.Copy();
        ProgressStallLimitCalculation effective = copy.LimitFor("archive");
        Assert.AreEqual(TimeSpan.FromMinutes(10), effective.NoProgressTimeout);
        Assert.AreEqual(4, effective.MaxRewindWorkFactor);
        effective.MaxRewindWorkFactor = 100;
        copy.ExecutionLimit.NoProgressTimeout = TimeSpan.FromMinutes(2);
        Assert.AreEqual(TimeSpan.FromMinutes(5), options.ExecutionLimit.NoProgressTimeout);
        Assert.AreEqual(4, options.LimitFor("other").MaxRewindWorkFactor);
    }

    [TestMethod]
    public void InvalidRecipesAreRejected()
    {
        foreach (TimeSpan timeout in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.MaxValue })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ProgressStallLimitCalculation { NoProgressTimeout = timeout }.Validate());
        }
        foreach (double factor in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ProgressStallLimitCalculation { MaxRewindWorkFactor = factor }.Validate());
        }
        Assert.ThrowsExactly<ArgumentNullException>(() => new SqliteHotBackupOptions().ConfigureExecutionLimit("id", null!));
    }
}
