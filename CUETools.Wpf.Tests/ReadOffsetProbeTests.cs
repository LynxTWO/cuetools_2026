using System;
using System.Collections.Generic;
using CUETools.AccurateRip;
using CUETools.CDImage;
using CUETools.Codecs;
using CUETools.Wpf.Accuracy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

/// <summary>
/// R122 measurement. The question this answers is narrow on purpose: is one completed read a
/// constant sample shift of another? Nothing here changes audio or any verdict - it exists so a
/// decision about realignment rests on numbers from the user's own disc rather than a hypothesis.
/// The search window is +/-4096 samples, so "no constant offset" must never be read as "aligned".
/// </summary>
[TestClass]
public sealed class ReadOffsetProbeTests
{
    // One 400-sector audio track: comfortably longer than the +/-8192-sample fingerprint window.
    private const string Toc = "0 400";
    private const int TotalSamples = 400 * 588;

    private static string Fingerprint(int shiftSamples)
    {
        var ar = new AccurateRipVerify(new CDImageLayout(Toc), null);
        var buffer = new AudioBuffer(AudioPCMConfig.RedBook, TotalSamples);
        buffer.Length = TotalSamples;
        // A deterministic, non-repeating stream: a repeating one would make every shift look
        // like every other shift and the test would prove nothing.
        var samples = buffer.Samples;
        uint state = 0x1234567u;
        for (int i = 0; i < TotalSamples; i++)
        {
            int source = i + shiftSamples;
            state = 1664525u * (uint)(source < 0 ? source + TotalSamples : source) + 1013904223u;
            samples[i, 0] = (int)((state >> 16) & 0x7fff) - 16384;
            samples[i, 1] = (int)((state >> 8) & 0x7fff) - 16384;
        }
        ar.Write(buffer);
        return ar.OffsetSafeCRC.Base64;
    }

    [TestMethod]
    public void IdenticalReadsMeasureAsAligned()
    {
        var pairs = ReadOffsetProbe.Compare(new List<string> { Fingerprint(0), Fingerprint(0) });

        Assert.AreEqual(1, pairs.Count);
        Assert.IsTrue(pairs[0].Comparable);
        Assert.IsTrue(pairs[0].OffsetFound);
        Assert.AreEqual(0, pairs[0].OffsetSamples);
        Assert.IsFalse(ReadOffsetProbe.AnyConstantShift(pairs),
            "aligned reads are not a constant SHIFT - realignment would have nothing to do");
        StringAssert.Contains(ReadOffsetProbe.Describe(pairs), "aligned");
    }

    [TestMethod]
    public void AConstantShiftIsMeasuredWithItsSizeAndDirection()
    {
        var pairs = ReadOffsetProbe.Compare(new List<string> { Fingerprint(0), Fingerprint(640) });

        Assert.IsTrue(pairs[0].Comparable);
        Assert.IsTrue(pairs[0].OffsetFound, "a shift inside the search window must be found");
        Assert.AreEqual(640, Math.Abs(pairs[0].OffsetSamples),
            "the measured magnitude must equal the real shift");
        Assert.IsTrue(ReadOffsetProbe.AnyConstantShift(pairs));
        StringAssert.Contains(ReadOffsetProbe.Describe(pairs), "samples");
    }

    [TestMethod]
    public void UnrelatedReadsReportNoConstantOffsetRatherThanZero()
    {
        // Two independent streams: the honest answer is "no constant offset within the window",
        // which is NOT the same claim as "aligned".
        string a = Fingerprint(0);
        var other = new AccurateRipVerify(new CDImageLayout(Toc), null);
        var buffer = new AudioBuffer(AudioPCMConfig.RedBook, TotalSamples);
        buffer.Length = TotalSamples;
        var samples = buffer.Samples;
        for (int i = 0; i < TotalSamples; i++) { samples[i, 0] = (i * 7919) % 20000 - 10000; samples[i, 1] = (i * 6271) % 20000 - 10000; }
        other.Write(buffer);

        var pairs = ReadOffsetProbe.Compare(new List<string> { a, other.OffsetSafeCRC.Base64 });

        Assert.IsTrue(pairs[0].Comparable);
        Assert.IsFalse(pairs[0].OffsetFound);
        Assert.IsFalse(ReadOffsetProbe.AnyConstantShift(pairs));
        StringAssert.Contains(ReadOffsetProbe.Describe(pairs), "no-constant-offset-within-4096");
    }

    [TestMethod]
    public void MissingOrCorruptFingerprintsAreNotComparable()
    {
        var pairs = ReadOffsetProbe.Compare(new List<string> { Fingerprint(0), "", null, "not base64 at all" });

        foreach (var pair in pairs)
            if (pair.FromRead != 0 || pair.ToRead != 0)
                Assert.IsFalse(pair.Comparable && pair.OffsetFound && pair.OffsetSamples != 0,
                    "an unusable fingerprint must never produce a measurement");
        StringAssert.Contains(ReadOffsetProbe.Describe(pairs), "not-comparable");
    }

    [TestMethod]
    public void ThreeReadsProduceEveryOrderedPair()
    {
        var pairs = ReadOffsetProbe.Compare(
            new List<string> { Fingerprint(0), Fingerprint(0), Fingerprint(0) });

        Assert.AreEqual(3, pairs.Count, "read1->read2, read1->read3, read2->read3");
    }
}
