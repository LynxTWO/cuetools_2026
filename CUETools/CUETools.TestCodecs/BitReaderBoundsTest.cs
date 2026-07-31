using CUETools.Codecs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace CUETools.TestCodecs
{
    [TestClass]
    public class BitReaderBoundsTest
    {
        [TestMethod]
        public unsafe void RiceBlockAcceptsTerminatorAtLogicalEnd()
        {
            // The backing array is intentionally longer than the declared input. Cache
            // lookahead may use zero padding, but the terminator in the second byte is real.
            byte[] backing = new byte[16];
            backing[1] = 0x80;
            int decoded = int.MinValue;

            fixed (byte* input = backing)
            {
                BitReader reader = new BitReader(input, 0, 2);
                reader.read_rice_block(1, 0, &decoded);
            }

            Assert.AreEqual(4, decoded);
        }

        [TestMethod]
        public unsafe void RiceBlockRejectsTerminatorPastLogicalEnd()
        {
            // The sentinel makes an over-read deterministic and memory-safe for the test.
            // It must not terminate a Rice code because it lies outside the declared input.
            byte[] backing = new byte[16];
            backing[2] = 0x80;
            int decoded = int.MinValue;

            fixed (byte* input = backing)
            {
                BitReader reader = new BitReader(input, 0, 2);
                try
                {
                    reader.read_rice_block(1, 0, &decoded);
                    Assert.Fail("The reader accepted a Rice terminator outside the declared input buffer.");
                }
                catch (InvalidDataException)
                {
                }
            }
        }

        [TestMethod]
        public unsafe void FixedWidthReadRejectsBytesPastLogicalEnd()
        {
            byte[] backing = new byte[16];
            backing[0] = 0xff;
            backing[1] = 0xff;

            fixed (byte* input = backing)
            {
                BitReader reader = new BitReader(input, 0, 1);
                Assert.AreEqual((uint)0xff, reader.readbits(8));
                AssertInvalidData(delegate { reader.readbit(); });
            }
        }

        [TestMethod]
        public unsafe void UnaryReadRejectsTerminatorPastLogicalEnd()
        {
            byte[] backing = new byte[16];
            backing[2] = 0x80;

            fixed (byte* input = backing)
            {
                BitReader reader = new BitReader(input, 0, 2);
                AssertInvalidData(delegate { reader.read_unary(); });
            }
        }

        [TestMethod]
        public unsafe void FlushConsumesDiscardedAlignmentBits()
        {
            byte[] backing = new byte[16];
            backing[0] = 0xff;
            backing[1] = 0xff;

            fixed (byte* input = backing)
            {
                BitReader reader = new BitReader(input, 0, 1);
                Assert.AreEqual((uint)1, reader.readbit());
                reader.flush();
                AssertInvalidData(delegate { reader.readbit(); });
            }
        }

        private static void AssertInvalidData(System.Action action)
        {
            try
            {
                action();
                Assert.Fail("The reader accepted bits outside the declared input buffer.");
            }
            catch (InvalidDataException)
            {
            }
        }
    }
}
