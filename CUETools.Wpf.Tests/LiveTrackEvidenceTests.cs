using System.Collections.Generic;
using System.IO;
using CUETools.AccurateRip;
using CUETools.CDImage;
using CUETools.Codecs;
using CUETools.Wpf.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// R120: a track's evidence appears when that track finishes, not when the album does.
/// The clock matters more than the plumbing: AccurateRipVerify memoizes CRC32 permanently, so a
/// value read one sample early is cached wrong for the whole rip, and the drive's sector
/// position runs seconds ahead of what the CRC engine has consumed. These tests pin the real
/// completion signal and the presentation rules around it.
/// </summary>
[TestClass]
public sealed class LiveTrackEvidenceTests
{
    // Two 10-sector audio tracks: offsets are in sectors, the last value is the lead-out.
    private const string TwoTrackToc = "0 10 20";
    private const int SectorSamples = 588;

    private static AudioBuffer Buffer(int samples)
    {
        var buffer = new AudioBuffer(AudioPCMConfig.RedBook, samples);
        buffer.Length = samples;
        return buffer;
    }

    [TestMethod]
    public void TrackCompletedFiresAtTheTrackBoundaryWithFinalChecksums()
    {
        var toc = new CDImageLayout(TwoTrackToc);
        var ar = new AccurateRipVerify(toc, null);
        var completed = new List<int>();
        var crcAtEvent = new Dictionary<int, uint>();
        ar.TrackCompleted += (_, track) =>
        {
            completed.Add(track);
            crcAtEvent[track] = ar.CRC32(track);
        };

        // Feed track 1 exactly, in two chunks, so the event cannot depend on buffer size.
        ar.Write(Buffer(5 * SectorSamples));
        Assert.AreEqual(0, completed.Count, "no track has ended yet");
        ar.Write(Buffer(5 * SectorSamples));

        CollectionAssert.AreEqual(new[] { 1 }, completed,
            "track 1 completes the moment its last sample is consumed");

        ar.Write(Buffer(10 * SectorSamples));
        CollectionAssert.AreEqual(new[] { 1, 2 }, completed);

        // The whole point of the clock: the value read at the event is the final value.
        Assert.AreEqual(ar.CRC32(1), crcAtEvent[1],
            "a CRC read at the event must equal the final CRC - CRC32 memoizes, so an early read poisons it");
        Assert.AreEqual(ar.CRC32(2), crcAtEvent[2]);
    }

    [TestMethod]
    public void AThrowingListenerCannotDisturbTheRead()
    {
        var ar = new AccurateRipVerify(new CDImageLayout(TwoTrackToc), null);
        int seen = 0;
        ar.TrackCompleted += (_, _) => { seen++; throw new IOException("a display listener died"); };

        ar.Write(Buffer(10 * SectorSamples));
        ar.Write(Buffer(10 * SectorSamples));

        Assert.AreEqual(2, seen, "both tracks still reported");
        Assert.AreEqual(20 * SectorSamples, ar.Position, "the read consumed every sample regardless");
    }

    [TestMethod]
    public void ALiveCrcFillsOnlyItsOwnColumn()
    {
        var track = new TrackItem();
        track.ApplyLiveCrc(0xAABBCCDD, isCopyRead: false);
        Assert.AreEqual("AABBCCDD", track.TestCrc);
        Assert.AreEqual("-", track.CopyCrc, "the Test read must not touch the Copy column");

        track.ApplyLiveCrc(0xAABBCCDD, isCopyRead: true);
        Assert.AreEqual("AABBCCDD", track.CopyCrc);
        Assert.AreEqual("AABBCCDD", track.TestCrc, "and the Copy read must not erase the Test column");
        Assert.IsTrue(track.CrcsMatch);
        Assert.IsFalse(track.CrcsDiffer);
    }

    [TestMethod]
    public void DisagreementIsVisibleAsSoonAsBothColumnsExist()
    {
        var track = new TrackItem();
        track.ApplyLiveCrc(0x11111111, isCopyRead: false);
        track.ApplyLiveCrc(0x22222222, isCopyRead: true);

        Assert.IsTrue(track.CrcsDiffer);
        Assert.IsFalse(track.CrcsMatch);
    }

    [TestMethod]
    public void AnUnknownCrcIsIgnoredRatherThanShownAsZero()
    {
        var track = new TrackItem();
        track.ApplyLiveCrc(0, isCopyRead: false);
        Assert.AreEqual("-", track.TestCrc, "0 means no evidence, not a checksum of zero");
    }

    [TestMethod]
    public void TheServiceUsesTheEngineClockAndDetachesFromIt()
    {
        string root = DeadSwitchAnalyzer.FindRepoRoot(System.AppContext.BaseDirectory);
        Assert.IsNotNull(root);
        string source = File.ReadAllText(
            Path.Combine(root, "CUETools.App.Core", "Services", "RipService.cs"));

        StringAssert.Contains(source, "liveAr.TrackCompleted += onArTrackCompleted;",
            "per-track evidence must come from the engine's own completion signal");
        StringAssert.Contains(source, "liveAr.TrackCompleted -= onArTrackCompleted;",
            "and must always detach, including when the read throws");
        StringAssert.Contains(source, "(int[])(read.ArPerTrack ?? Array.Empty<int>()).Clone()",
            "verdict arrays must be copied - the engine instances stay live in the commit path");
    }
}
