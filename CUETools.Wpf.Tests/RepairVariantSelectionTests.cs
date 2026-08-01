using System;
using System.IO;
using CUETools.CTDB;
using CUETools.Processor;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// R111: with several recoverable CTDB variants, repair must converge on the highest-confidence
/// pressing, not whichever entry the server happened to list first.
/// </summary>
[TestClass]
public sealed class RepairVariantSelectionTests
{
    // DBEntry's only constructor parses a full server response (parity, TOC string); the ranking
    // reads exactly one public field, so an uninitialized instance with conf set is the honest
    // minimal fixture.
    private static object Choice(int confidence)
    {
        var entry = (DBEntry)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(DBEntry));
        entry.conf = confidence;
        var choice = new CUEToolsSourceFile("variant", new StringReader(""));
        choice.data = entry;
        return choice;
    }

    [TestMethod]
    public void HighestConfidenceWinsOverServerOrder()
    {
        object[] choices = { Choice(1), Choice(40), Choice(12) };
        Assert.AreEqual(1, CueRepairEngine.SelectBestVariant(choices),
            "a conf=1 entry listed first must not outrank a conf=40 pressing");
    }

    [TestMethod]
    public void TiesKeepTheEarliestEntry()
    {
        object[] choices = { Choice(7), Choice(7), Choice(3) };
        Assert.AreEqual(0, CueRepairEngine.SelectBestVariant(choices));
    }

    [TestMethod]
    public void SingleAndEmptyListsKeepTheEngineDefault()
    {
        Assert.AreEqual(0, CueRepairEngine.SelectBestVariant(new[] { Choice(5) }));
        Assert.AreEqual(0, CueRepairEngine.SelectBestVariant(Array.Empty<object>()));
        Assert.AreEqual(0, CueRepairEngine.SelectBestVariant(null));
    }

    [TestMethod]
    public void EntriesWithoutADbPayloadRankLowest()
    {
        var bare = new CUEToolsSourceFile("no-entry", new StringReader(""));
        object[] choices = { bare, Choice(2) };
        Assert.AreEqual(1, CueRepairEngine.SelectBestVariant(choices));
    }

    [TestMethod]
    public void ConfidenceRankingIsPure()
    {
        Assert.AreEqual(2, CueRepairEngine.SelectBestConfidence(new[] { 3, 3, 9, 9 }),
            "the first of the tied maxima wins");
        Assert.AreEqual(0, CueRepairEngine.SelectBestConfidence(new[] { int.MinValue, int.MinValue }),
            "an all-unknown list falls back to the first entry");
    }
}
