using System;
using CUETools.Ripper.SCSI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestRipper
{
    [TestClass]
    public sealed class CDDriveReaderTest
    {
        private const int PassCount = 64;
        private const int SectorCount = 32;

        [TestMethod]
        public void DeterministicC2WeightedCorpusRecoversReferenceSectors()
        {
            byte[] reference =
                new byte[SectorCount * SecureSectorVote.BytesPerSector];
            new Random(2314).NextBytes(reference);
            var userData = new long[
                SectorCount,
                2,
                SecureSectorVote.BytesPerSector];
            var c2Count = new byte[
                SectorCount,
                SecureSectorVote.C2GroupsPerSector];
            ulong[] byteToBitLanes = CreateByteToBitLanes();
            int flaggedCorruptBlocks = 0;
            int unflaggedCorruptBlocks = 0;

            for (int pass = 0; pass < PassCount; pass++)
            {
                for (int sector = 0; sector < SectorCount; sector++)
                {
                    for (int group = 0;
                        group < SecureSectorVote.C2GroupsPerSector;
                        group++)
                    {
                        int blockOrdinal =
                            sector * SecureSectorVote.C2GroupsPerSector +
                            group;
                        bool corrupt =
                            ((pass + blockOrdinal * 7) % 23) == 0;
                        bool c2 =
                            corrupt && ((pass + blockOrdinal) & 1) == 0;
                        if (corrupt)
                        {
                            if (c2)
                                flaggedCorruptBlocks++;
                            else
                                unflaggedCorruptBlocks++;
                        }

                        int plane = c2 ? 1 : 0;
                        if (c2)
                            c2Count[sector, group]++;
                        int firstByte = group * 8;
                        for (int withinGroup = 0;
                            withinGroup < 8;
                            withinGroup++)
                        {
                            int byteIndex = firstByte + withinGroup;
                            int referenceOffset =
                                sector * SecureSectorVote.BytesPerSector +
                                byteIndex;
                            byte value = reference[referenceOffset];
                            if (corrupt)
                            {
                                value ^= (byte)(
                                    1 << ((pass + referenceOffset) & 7));
                            }
                            userData[sector, plane, byteIndex] +=
                                (long)byteToBitLanes[value];
                        }
                    }
                }
            }

            Assert.IsTrue(
                flaggedCorruptBlocks > 0,
                "the corpus must include C2-flagged corruption");
            Assert.IsTrue(
                unflaggedCorruptBlocks > 0,
                "the corpus must include undetected corruption");

            var recovered = new byte[reference.Length];
            for (int sector = 0; sector < SectorCount; sector++)
            {
                bool lowConfidence = SecureSectorVote.CorrectSector(
                    userData,
                    c2Count,
                    recovered,
                    sector,
                    PassCount,
                    correctionQuality: 2);
                Assert.IsFalse(
                    lowConfidence,
                    "the deterministic majority should be conclusive");
            }

            CollectionAssert.AreEqual(
                reference,
                recovered,
                "the production C2-weighted vote did not recover the reference sectors");
        }

        [TestMethod]
        public void OneCleanPassDoesNotSatisfyTwoPassConfidencePolicy()
        {
            var userData = new long[
                1,
                2,
                SecureSectorVote.BytesPerSector];
            var c2Count = new byte[
                1,
                SecureSectorVote.C2GroupsPerSector];
            var destination =
                new byte[SecureSectorVote.BytesPerSector];
            ulong[] byteToBitLanes = CreateByteToBitLanes();
            for (int i = 0; i < SecureSectorVote.BytesPerSector; i++)
                userData[0, 0, i] = (long)byteToBitLanes[0xA5];

            bool lowConfidence = SecureSectorVote.CorrectSector(
                userData,
                c2Count,
                destination,
                sectorPosition: 0,
                passCount: 1,
                correctionQuality: 1);

            Assert.IsTrue(
                lowConfidence,
                "one clean read must not be declared secure when two are required");
            foreach (byte value in destination)
                Assert.AreEqual(
                    (byte)0xA5,
                    value,
                    "the best available byte should still be reconstructed");
        }

        [TestMethod]
        public void C2PlaneDeterminesBestByteWithoutClaimingSecureConfidence()
        {
            var userData = new long[
                1,
                2,
                SecureSectorVote.BytesPerSector];
            var c2Count = new byte[
                1,
                SecureSectorVote.C2GroupsPerSector];
            var destination =
                new byte[SecureSectorVote.BytesPerSector];
            ulong[] byteToBitLanes = CreateByteToBitLanes();
            for (int group = 0;
                group < SecureSectorVote.C2GroupsPerSector;
                group++)
                c2Count[0, group] = 3;
            for (int i = 0; i < SecureSectorVote.BytesPerSector; i++)
            {
                // Three C2-flagged observations, with a 2:1 majority for A5.
                userData[0, 1, i] =
                    (long)(
                        byteToBitLanes[0xA5] * 2 +
                        byteToBitLanes[0x5A]);
            }

            bool lowConfidence = SecureSectorVote.CorrectSector(
                userData,
                c2Count,
                destination,
                sectorPosition: 0,
                passCount: 3,
                correctionQuality: 0);

            Assert.IsTrue(
                lowConfidence,
                "C2-only evidence may select a byte but must not be called secure");
            foreach (byte value in destination)
                Assert.AreEqual(
                    (byte)0xA5,
                    value,
                    "the C2 vote plane must participate in reconstruction");
        }

        private static ulong[] CreateByteToBitLanes()
        {
            var byteToBitLanes = new ulong[256];
            for (ulong value = 0; value < 256; value++)
            {
                ulong lanes = 0;
                for (int bit = 0; bit < 8; bit++)
                    lanes += ((value >> bit) & 1) << (bit << 3);
                byteToBitLanes[value] = lanes;
            }
            return byteToBitLanes;
        }
    }
}
