using System;
using System.Collections.Generic;
using CUETools.Wpf.Accuracy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class TestAndCopyResolverFuzzTests
    {
        [TestMethod]
        public void Fuzz_InvariantsHold()
        {
            var rnd = new Random(20260724);
            for (int iter = 0; iter < 5000; iter++)
            {
                int readCount = 2 + rnd.Next(2);           // 2 or 3 reads
                int tracks = 1 + rnd.Next(6);
                var reads = new List<VerifyRecord>();
                for (int r = 0; r < readCount; r++)
                {
                    int len = Math.Max(1, tracks + rnd.Next(-1, 2)); // occasionally ragged
                    var tc = new TrackCrc[len];
                    for (int t = 0; t < len; t++)
                    {
                        uint crc = (uint)rnd.Next(1, 4); // small domain -> collisions
                        tc[t] = new TrackCrc { ArV2 = crc, Crc32 = crc };
                    }
                    reads.Add(new VerifyRecord { Tracks = tc });
                }
                var staged = new bool[readCount];
                for (int i = 1; i < readCount; i++) staged[i] = true;

                var res = TestAndCopyResolver.Resolve(reads, staged);

                if (res.Outcome == TestCopyOutcome.Passed)
                    Assert.AreEqual(0, res.HeldTracks.Length);
                else
                    Assert.IsTrue(res.HeldTracks.Length > 0);

                foreach (var v in res.Tracks)
                {
                    if (v.Agreed)
                    {
                        Assert.IsTrue(staged[v.SourceReadIndex], "source must be staged");
                        CollectionAssert.Contains(v.AgreeingReads, v.SourceReadIndex);
                        Assert.AreEqual(2, v.AgreeingReads.Length);
                        var a = reads[v.AgreeingReads[0]].Tracks[v.TrackIndex];
                        var b = reads[v.AgreeingReads[1]].Tracks[v.TrackIndex];
                        Assert.IsTrue(VerifyHistoryStore.SameAudioForTestAndCopy(a, b), "agreeing pair must actually agree");
                    }
                    else
                    {
                        Assert.AreEqual(-1, v.SourceReadIndex);
                        CollectionAssert.Contains(res.HeldTracks, v.TrackIndex);
                    }
                }
            }
        }
    }
}
