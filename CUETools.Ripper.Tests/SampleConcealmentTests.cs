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
        public void NullEmptyAndNonPositiveInputsDoNothing()
        {
            Assert.AreEqual(0, SampleConcealment.Conceal(null, new byte[4], 1));
            Assert.AreEqual(0, SampleConcealment.Conceal(new byte[4], null, 1));
            Assert.AreEqual(0, SampleConcealment.Conceal(new byte[4], new byte[4], 0));
            Assert.AreEqual(0, SampleConcealment.Conceal(new byte[4], new byte[4], -1));
        }

        [TestMethod]
        public void UsableFramesAreClampedToBothBuffersAndWholeStereoFrames()
        {
            var pcm = Pcm(100, 200, -30000, -30000, 500, 600);
            var map = new byte[7];
            map[2] = 1;
            byte[] trailingFrame = { pcm[4], pcm[5], pcm[6], pcm[7], pcm[8], pcm[9], pcm[10], pcm[11] };

            int concealed = SampleConcealment.Conceal(pcm, map, 99);

            Assert.AreEqual(1, concealed);
            Assert.AreEqual(0, Sample(pcm, 0, 1));
            CollectionAssert.AreEqual(
                trailingFrame,
                new[] { pcm[4], pcm[5], pcm[6], pcm[7], pcm[8], pcm[9], pcm[10], pcm[11] });
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
        public void EitherByteOfAChannelMarksItsFrameBad()
        {
            var lowByte = Pcm(100, 0, -20000, 0, 300, 0);
            var highByte = (byte[])lowByte.Clone();
            var lowMap = new byte[lowByte.Length];
            var highMap = new byte[highByte.Length];
            lowMap[4] = 1;
            highMap[5] = 1;

            Assert.AreEqual(1, SampleConcealment.Conceal(lowByte, lowMap, 3));
            Assert.AreEqual(1, SampleConcealment.Conceal(highByte, highMap, 3));
            Assert.AreEqual(200, Sample(lowByte, 1, 0));
            Assert.AreEqual(200, Sample(highByte, 1, 0));
        }

        [TestMethod]
        public void ARunAtEitherEdgeUsesTheOnlyAvailableAnchor()
        {
            var atStart = Pcm(-30000, 0, -20000, 0, 1200, 0);
            var startMap = new byte[atStart.Length];
            startMap[0] = startMap[1] = 1;
            startMap[4] = startMap[5] = 1;

            Assert.AreEqual(2, SampleConcealment.Conceal(atStart, startMap, 3));
            Assert.AreEqual(1200, Sample(atStart, 0, 0));
            Assert.AreEqual(1200, Sample(atStart, 1, 0));

            var atEnd = Pcm(-1200, 0, 20000, 0, 30000, 0);
            var endMap = new byte[atEnd.Length];
            endMap[4] = endMap[5] = 1;
            endMap[8] = endMap[9] = 1;

            Assert.AreEqual(2, SampleConcealment.Conceal(atEnd, endMap, 3));
            Assert.AreEqual(-1200, Sample(atEnd, 1, 0));
            Assert.AreEqual(-1200, Sample(atEnd, 2, 0));
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
            Assert.AreEqual(9000, Sample(pcm, 1, 0), "the fade starts at the prior anchor");
            Assert.AreEqual(8938, Sample(pcm, 2, 0));
            Assert.AreEqual(61, Sample(pcm, 1 + 146, 0));
            Assert.AreEqual(0, Sample(pcm, 1 + 147, 0));
            Assert.AreEqual(8938, Sample(pcm, frames - 1, 0));
            Assert.AreEqual(9000, Sample(pcm, frames, 0), "the fade ends at the next anchor");
            Assert.AreEqual(9000, Sample(pcm, frames + 1, 0), "the next good sample is untouched");
        }

        [TestMethod]
        public void ExactlyTheInterpolationLimitStillUsesTheFullLinearRamp()
        {
            const int Anchor = 5890;
            int run = SampleConcealment.MaxInterpolatedFrames;
            var values = new short[(run + 2) * 2];
            values[(run + 1) * 2] = Anchor;
            var pcm = Pcm(values);
            var map = new byte[pcm.Length];
            for (int frame = 1; frame <= run; frame++)
                map[frame * 4] = 1;

            int concealed = SampleConcealment.Conceal(pcm, map, run + 2);

            Assert.AreEqual(run, concealed);
            Assert.AreEqual(10, Sample(pcm, 1, 0));
            Assert.AreEqual(2940, Sample(pcm, 294, 0));
            Assert.AreEqual(5880, Sample(pcm, run, 0));
            Assert.AreEqual(Anchor, Sample(pcm, run + 1, 0));
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
        public void RightChannelInterpolationReadsAndWritesItsOwnSignedSamples()
        {
            var pcm = Pcm(10, -3000, 20, 30000, 30, -1000);
            var map = new byte[pcm.Length];
            map[6] = 1;

            int concealed = SampleConcealment.Conceal(pcm, map, 3);

            Assert.AreEqual(1, concealed);
            Assert.AreEqual(-2000, Sample(pcm, 1, 1));
            Assert.AreEqual(20, Sample(pcm, 1, 0));
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
