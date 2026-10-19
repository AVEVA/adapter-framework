// Copyright 2018-2026 AVEVA Group Limited
// Licensed under the Apache License, Version 2.0 (the "License");
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.EndpointManager.Tests;

/// <summary>Tests inclusive coverage across duplicate timestamps, priority overtaking, and permanent gaps.</summary>
public class AcceptanceLowWaterTracker_Tests
{
    /// <summary>Duplicate timestamps remain uncovered until every matching body is accepted.</summary>
    [Fact]
    public void Register_DuplicateTimestamps_RequiresEveryAcceptance()
    {
        var tracker = new AcceptanceLowWaterTracker();
        Assert.Equal(DateTime.MinValue, tracker.GetWatermark());
        tracker.Register(100);
        tracker.Register(100);
        tracker.Register(200);
        tracker.Accept(200);
        tracker.Accept(100);
        Assert.Equal(99, tracker.GetWatermark().Ticks);
        tracker.Accept(100);
        Assert.Equal(200, tracker.GetWatermark().Ticks);
    }

    /// <summary>A later priority metadata acceptance cannot cover an older data body.</summary>
    [Fact]
    public void Accept_OutOfOrder_HoldsOldestPendingMinusOneTick()
    {
        var tracker = new AcceptanceLowWaterTracker();
        tracker.Register(300);
        tracker.Register(100);
        tracker.Register(200);
        tracker.Accept(300);
        Assert.Equal(99, tracker.GetWatermark().Ticks);
        tracker.Accept(100);
        Assert.Equal(199, tracker.GetWatermark().Ticks);
        tracker.Accept(200);
        Assert.Equal(300, tracker.GetWatermark().Ticks);
    }

    /// <summary>A permanent gap freezes coverage and releases tracking so ongoing admissions use bounded memory.</summary>
    [Fact]
    public void Freeze_PendingLoss_ClearsMultisetAndIgnoresFutureBodies()
    {
        var tracker = new AcceptanceLowWaterTracker();
        tracker.Register(100);
        tracker.Register(200);
        tracker.Freeze();
        tracker.Accept(100);
        for (var ticks = 201; ticks < 10000; ticks++)
        {
            tracker.Register(ticks);
        }

        Assert.Equal(99, tracker.GetWatermark().Ticks);
        Assert.Empty((IEnumerable)TestUtilities.GetFieldValueFromObject("_pending", tracker));
    }

    /// <summary>Every unknown body must be accepted before later timestamped bodies establish coverage.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Register_UnknownTimestamp_BlocksUntilEveryUnknownAccepted(long ticks)
    {
        var tracker = new AcceptanceLowWaterTracker();
        tracker.Register(ticks);
        tracker.Register(ticks);
        tracker.Register(100);
        tracker.Accept(100);
        Assert.Equal(DateTime.MinValue, tracker.GetWatermark());
        tracker.Accept(ticks);
        Assert.Equal(DateTime.MinValue, tracker.GetWatermark());
        tracker.Accept(ticks);
        Assert.Equal(100, tracker.GetWatermark().Ticks);
        tracker.Register(200);
        tracker.Accept(200);
        Assert.Equal(200, tracker.GetWatermark().Ticks);
    }

    /// <summary>Recovery tolerates unregistered records and timestamp collisions but gates coverage until the queues drain.</summary>
    [Fact]
    public void CompleteRecovery_AfterDrain_ResetsAmbiguousRegistrationsAndResumesCoverage()
    {
        var tracker = new AcceptanceLowWaterTracker();
        tracker.BeginRecovery();
        tracker.Register(200);
        tracker.Register(0);
        tracker.Accept(200); // Recovered body collides with a newly admitted timestamp.
        tracker.Accept(0); // Recovered legacy body can also consume an unknown registration.
        tracker.Accept(200);
        tracker.Accept(0);
        tracker.Accept(500); // Recovered process times contribute to the high boundary.
        Assert.Equal(DateTime.MinValue, tracker.GetWatermark());
        tracker.CompleteRecovery();
        Assert.Equal(500, tracker.GetWatermark().Ticks);
        tracker.Register(600);
        tracker.Accept(600);
        Assert.Equal(600, tracker.GetWatermark().Ticks);
    }

    /// <summary>A loss or rejection during recovery cannot be cleared by draining or beginning recovery again.</summary>
    [Fact]
    public void CompleteRecovery_AfterLoss_RemainsPermanentlyFrozen()
    {
        var tracker = new AcceptanceLowWaterTracker();
        tracker.BeginRecovery();
        tracker.Register(100);
        tracker.Freeze();
        tracker.Accept(100);
        tracker.CompleteRecovery();
        tracker.BeginRecovery();
        tracker.CompleteRecovery();
        tracker.Register(200);
        tracker.Accept(200);
        Assert.Equal(DateTime.MinValue, tracker.GetWatermark());
    }

    /// <summary>An unknown body lost before acceptance leaves a permanent zero boundary with bounded storage.</summary>
    [Fact]
    public void Freeze_UnknownBody_NeverResumesAfterLaterAcceptance()
    {
        var tracker = new AcceptanceLowWaterTracker();
        tracker.Register(0);
        tracker.Freeze();
        tracker.Accept(0);
        tracker.Register(100);
        tracker.Accept(100);
        Assert.Equal(DateTime.MinValue, tracker.GetWatermark());
        Assert.Empty((IEnumerable)TestUtilities.GetFieldValueFromObject("_pending", tracker));
        Assert.Equal(0L, TestUtilities.GetFieldValueFromObject("_pendingUnknown", tracker));
    }

    /// <summary>Unknown bodies use a single counter rather than accumulating one timestamp registration per body.</summary>
    [Fact]
    public void Register_ManyUnknownBodies_UsesBoundedCounterUntilAllAccepted()
    {
        var tracker = new AcceptanceLowWaterTracker();
        for (var index = 0; index < 10000; index++)
        {
            tracker.Register(0);
        }

        Assert.Empty((IEnumerable)TestUtilities.GetFieldValueFromObject("_pending", tracker));
        Assert.Equal(10000L, TestUtilities.GetFieldValueFromObject("_pendingUnknown", tracker));
        tracker.Register(100);
        tracker.Accept(100);
        for (var index = 0; index < 10000; index++)
        {
            Assert.Equal(DateTime.MinValue, tracker.GetWatermark());
            tracker.Accept(0);
        }

        Assert.Equal(100, tracker.GetWatermark().Ticks);
    }

    /// <summary>An acceptance without registration conservatively blocks the writer rather than advancing it.</summary>
    [Fact]
    public void Accept_UnregisteredBody_BlocksCoverage()
    {
        var tracker = new AcceptanceLowWaterTracker();
        tracker.Accept(100);
        Assert.Equal(DateTime.MinValue, tracker.GetWatermark());
    }
}