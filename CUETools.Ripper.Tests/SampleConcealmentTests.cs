using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    /// <summary>
    /// R119: concealment of samples the vote never confirmed. A CD player interpolates or mutes
    /// uncorrectable samples, which is why a damaged disc still sounds like music; a CD-ROM drive
    /// reading in data mode does not. The first salvage build published the raw unconfirmed
    /// guesses and the result was unlistenable, so these rules are the difference between a
    /// usable capture and noise.
    /// </summary>
    [TestClass]
    public class SampleConcealmentTests
    {
        private static byte[] Pcm(params short[] leftRight)
        {
            var pcm = new byte[leftRight.Length * 2];
            for (int i = 0; i < leftRight.Length; i++)
            {
                pcm[i * 2] = (byte)(leftRight[i] & 0xff);
                pcm[i * 2 + 1] = (byte)((leftRight[i] >> 8) & 0xff);
            }
            return pcm;
        }

        private static short Sample(byte[] pcm, int frame, int channel)
        {
            int at = frame * 4 + channel * 2;
            return (short)(pcm[at] | (pcm[at + 1] << 8));
        }

        [TestMethod]
        public void ConfirmedSamplesAreNeverTouched()
        {
            // 3 frames, all confirmed: L = 1000, 2000, 3000
            var pcm = Pcm(1000, -1000, 2000, -2000, 3000, -3000);
            var map = new byte[pcm.Length];
            var original = (byte[])pcm.Clone();

            int concealed = SampleConcealment.Conceal(pcm, map, 3);

            Assert.AreEqual(0, concealed);
            CollectionAssert.AreEqual(original, pcm, "a clean capture must come through bit-exact");
        }

        [TestMethod]
        public void AnUnconfirmedRunIsInterpolatedAcrossItsNeighbours()
        {
            // L: 1000, [garbage], [garbage], 4000 -> expect a ramp, not the garbage.
            var pcm = Pcm(1000, 0, -32000, 0, 31000, 0, 4000, 0);
            var map = new byte[pcm.Length];
            map[4] = 1; map[5] = 1;      // frame 1 left
            map[8] = 1; map[9] = 1;      // frame 2 left

            int concealed = SampleConcealment.Conceal(pcm, map, 4);

            Assert.AreEqual(2, concealed);
            Assert.AreEqual(1000, Sample(pcm, 0, 0), "anchor untouched");
            Assert.AreEqual(4000, Sample(pcm, 3, 0), "anchor untouched");
            Assert.AreEqual(2000, Sample(pcm, 1, 0));
            Assert.AreEqual(3000, Sample(pcm, 2, 0));
        }

        [TestMethod]
        public void ChannelsAreConcealedIndependently()
        {
            // Left frame 1 bad, right frame 1 fine.
            var pcm = Pcm(100, 500, -20000, 600, 300, 700);
            var map = new byte[pcm.Length];
            map[4] = 1; map[5] = 1;      // frame 1 left only

            int concealed = SampleConcealment.Conceal(pcm, map, 3);

            Assert.AreEqual(1, concealed);
            Assert.AreEqual(200, Sample(pcm, 1, 0), "left interpolated between 100 and 300");
            Assert.AreEqual(600, Sample(pcm, 1, 1), "right was confirmed and must be untouched");
        }

        [TestMethod]
        public void AWideRunIsMutedRatherThanRamped()
        {
            int frames = SampleConcealment.MaxInterpolatedFrames + 100;
            var values = new short[(frames + 2) * 2];
            for (int i = 0; i < values.Length; i += 2) { values[i] = 9000; values[i + 1] = 9000; }
            var pcm = Pcm(values);
            var map = new byte[pcm.Length];
            for (int f = 1; f <= frames; f++) { map[f * 4] = 1; map[f * 4 + 1] = 1; }

            int concealed = SampleConcealment.Conceal(pcm, map, frames + 2);

            Assert.AreEqual(frames, concealed);
            int middle = 1 + frames / 2;
            Assert.AreEqual(0, Sample(pcm, middle, 0),
                "damage this wide fades to silence instead of drawing a quarter-second ramp");
            Assert.AreEqual(9000, Sample(pcm, 0, 0), "the good sample before it stands");
        }

        [TestMethod]
        public void NothingToAnchorOnBecomesSilence()
        {
            var pcm = Pcm(-30000, 30000, -31000, 31000);
            var map = new byte[pcm.Length];
            for (int i = 0; i < map.Length; i++) map[i] = 1;

            int concealed = SampleConcealment.Conceal(pcm, map, 2);

            Assert.AreEqual(4, concealed, "both channels of both frames");
            for (int f = 0; f < 2; f++)
                for (int c = 0; c < 2; c++)
                    Assert.AreEqual(0, Sample(pcm, f, c));
        }

        [TestMethod]
        public void TheVoteReportsWhichBytesItCouldNotConfirm()
        {
            // One pass at Burst quality confirms a byte by margin; the map must stay clear.
            var userData = new long[1, 2, SecureSectorVote.BytesPerSector];
            var c2 = new byte[1, SecureSectorVote.C2GroupsPerSector];
            var dest = new byte[SecureSectorVote.BytesPerSector];
            var map = new byte[SecureSectorVote.BytesPerSector];

            bool low = SecureSectorVote.CorrectSector(userData, c2, dest, 0, 1, 0, map);

            Assert.IsFalse(low, "a single clean pass is confident at Burst quality");
            foreach (byte flagged in map)
                Assert.AreEqual(0, flagged, "nothing to conceal in a confirmed sector");
        }
    }
}
