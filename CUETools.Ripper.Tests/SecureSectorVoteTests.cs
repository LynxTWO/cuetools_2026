using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    [TestClass]
    public class SecureSectorVoteTests
    {
        [TestMethod]
        public void OneCleanPassReconstructsExactBytesAtTheRequestedSectorOffset()
        {
            var userData = new long[2, 2, SecureSectorVote.BytesPerSector];
            var c2 = new byte[2, SecureSectorVote.C2GroupsPerSector];
            var destination = Filled(2 * SecureSectorVote.BytesPerSector, 0xcc);
            var map = Filled(2 * SecureSectorVote.BytesPerSector, 0x7f);
            userData[1, 0, 0] = PackedVotes(0xa5, 1);
            userData[1, 0, 1] = PackedVotes(0x80, 1);
            userData[1, 0, SecureSectorVote.BytesPerSector - 1] =
                PackedVotes(0xff, 1);

            bool low = SecureSectorVote.CorrectSector(
                userData,
                c2,
                destination,
                sectorPosition: 1,
                passCount: 1,
                correctionQuality: 0,
                map);

            Assert.IsFalse(low);
            Assert.AreEqual(0xcc, destination[0]);
            Assert.AreEqual(0xa5, destination[SecureSectorVote.BytesPerSector]);
            Assert.AreEqual(0x80, destination[SecureSectorVote.BytesPerSector + 1]);
            Assert.AreEqual(
                0xff,
                destination[(2 * SecureSectorVote.BytesPerSector) - 1]);
            Assert.AreEqual(0x7f, map[0]);
            Assert.AreEqual(0, map[SecureSectorVote.BytesPerSector]);
            Assert.AreEqual(0, map[(2 * SecureSectorVote.BytesPerSector) - 1]);
        }

        [TestMethod]
        public void OnePassIsInsufficientAtHigherCorrectionQuality()
        {
            var userData = new long[1, 2, SecureSectorVote.BytesPerSector];
            var c2 = new byte[1, SecureSectorVote.C2GroupsPerSector];
            var destination = new byte[SecureSectorVote.BytesPerSector];
            var map = new byte[SecureSectorVote.BytesPerSector];

            bool low = SecureSectorVote.CorrectSector(
                userData,
                c2,
                destination,
                0,
                passCount: 1,
                correctionQuality: 1,
                map);

            Assert.IsTrue(low);
            foreach (byte flagged in map)
                Assert.AreEqual(1, flagged);
        }

        [TestMethod]
        public void TiedCleanVotesKeepTheBestGuessButMarkOnlyThatByteUnconfident()
        {
            var userData = new long[1, 2, SecureSectorVote.BytesPerSector];
            var c2 = new byte[1, SecureSectorVote.C2GroupsPerSector];
            var destination = new byte[SecureSectorVote.BytesPerSector];
            var map = new byte[SecureSectorVote.BytesPerSector];
            userData[0, 0, 0] = PackedVotes(0x01, 1);

            bool low = SecureSectorVote.CorrectSector(
                userData,
                c2,
                destination,
                0,
                passCount: 2,
                correctionQuality: 0,
                map);

            Assert.IsTrue(low);
            Assert.AreEqual(0, destination[0]);
            Assert.AreEqual(1, map[0]);
            for (int i = 1; i < map.Length; i++)
                Assert.AreEqual(0, map[i]);
        }

        [TestMethod]
        public void C2VotesHaveLowerWeightButStillParticipateInTheWinningBit()
        {
            var userData = new long[1, 2, SecureSectorVote.BytesPerSector];
            var c2 = new byte[1, SecureSectorVote.C2GroupsPerSector];
            var destination = new byte[SecureSectorVote.BytesPerSector];
            var map = new byte[SecureSectorVote.BytesPerSector];
            c2[0, 0] = 1;
            userData[0, 0, 0] = PackedVotes(0x01, 1);

            bool disagreement = SecureSectorVote.CorrectSector(
                userData,
                c2,
                destination,
                0,
                passCount: 2,
                correctionQuality: 0,
                map);

            Assert.IsTrue(disagreement);
            Assert.AreEqual(1, destination[0]);
            Assert.AreEqual(1, map[0]);

            userData[0, 1, 0] = PackedVotes(0x01, 1);
            bool agreement = SecureSectorVote.CorrectSector(
                userData,
                c2,
                destination,
                0,
                passCount: 2,
                correctionQuality: 0,
                map);

            Assert.IsFalse(agreement);
            Assert.AreEqual(1, destination[0]);
            Assert.AreEqual(0, map[0]);
        }

        [TestMethod]
        public void PackedC2VotesAdvanceAcrossAllEightBitLanes()
        {
            var userData = new long[1, 2, SecureSectorVote.BytesPerSector];
            var c2 = new byte[1, SecureSectorVote.C2GroupsPerSector];
            var destination = new byte[SecureSectorVote.BytesPerSector];
            var map = new byte[SecureSectorVote.BytesPerSector];
            c2[0, 0] = 1;
            userData[0, 0, 0] = PackedVotes(0xa5, 1);
            userData[0, 1, 0] = PackedVotes(0xa5, 1);

            bool low = SecureSectorVote.CorrectSector(
                userData,
                c2,
                destination,
                0,
                passCount: 2,
                correctionQuality: 0,
                map);

            Assert.IsFalse(low);
            Assert.AreEqual(0xa5, destination[0]);
            Assert.AreEqual(0, map[0]);
        }

        [TestMethod]
        public void AShortConfidenceMapIsOptionalForLaterSectors()
        {
            var userData = new long[2, 2, SecureSectorVote.BytesPerSector];
            var c2 = new byte[2, SecureSectorVote.C2GroupsPerSector];
            var destination = new byte[2 * SecureSectorVote.BytesPerSector];
            var shortMap = Filled(SecureSectorVote.BytesPerSector, 0x5a);

            bool low = SecureSectorVote.CorrectSector(
                userData,
                c2,
                destination,
                sectorPosition: 1,
                passCount: 1,
                correctionQuality: 1,
                shortMap);

            Assert.IsTrue(low);
            foreach (byte value in shortMap)
                Assert.AreEqual(0x5a, value);
        }

        private static long PackedVotes(byte value, byte count)
        {
            long packed = 0;
            for (int bit = 0; bit < 8; bit++)
                if ((value & (1 << bit)) != 0)
                    packed |= (long)count << (bit * 8);
            return packed;
        }

        private static byte[] Filled(int length, byte value)
        {
            var bytes = new byte[length];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = value;
            return bytes;
        }
    }
}
