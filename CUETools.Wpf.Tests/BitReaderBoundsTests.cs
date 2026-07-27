using System;
using CUETools.Codecs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class BitReaderBoundsTests
    {
        [TestMethod]
        public unsafe void RiceBlock_AcceptsTerminatorCachedAfterSpeculativeLookahead()
        {
            // Unary quotient 8 followed by a zero remainder: the first byte has no terminator,
            // while the second starts with one. fill() advances beyond this exact-length input
            // when it tops up its seven-byte cache, but both bytes remain logically readable.
            byte[] encoded = { 0x00, 0x80 };
            int decoded = Int32.MinValue;

            fixed (byte* input = encoded)
            {
                var reader = new BitReader(input, 0, encoded.Length);
                reader.read_rice_block(1, 0, &decoded);
            }

            Assert.AreEqual(4, decoded);
        }

        [TestMethod]
        public unsafe void RiceBlock_RejectsMissingUnaryTerminator()
        {
            byte[] truncated = { 0x00, 0x00 };
            int decoded = Int32.MinValue;

            fixed (byte* input = truncated)
            {
                var reader = new BitReader(input, 0, truncated.Length);
                try
                {
                    reader.read_rice_block(1, 0, &decoded);
                    Assert.Fail("A Rice code without a unary terminator must be rejected.");
                }
                catch (IndexOutOfRangeException)
                {
                }
            }
        }

        [TestMethod]
        public unsafe void FixedWidthRead_RejectsPastLogicalEnd()
        {
            byte[] oneByte = { 0xff };

            fixed (byte* input = oneByte)
            {
                var reader = new BitReader(input, 0, oneByte.Length);
                Assert.AreEqual((uint)0xff, reader.readbits(8));
                Assert.ThrowsException<System.IO.InvalidDataException>(
                    () => reader.readbit());
            }
        }
    }
}
