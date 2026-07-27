using System;
using System.IO;
using System.Linq;
using CUETools.Compression.Rar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.TestCodecs
{
    [TestClass]
    public class RarCompressionProviderTest
    {
        [TestMethod]
        public void SignedUnrarDllStreamsAndSeeksRealRar5PayloadByteExactly()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string archivePath = Path.Combine(baseDirectory, "rar-stream-fixture.rar");
            string expectedPath = Path.Combine(baseDirectory, "rar-stream-fixture.txt");
            byte[] expected = File.ReadAllBytes(expectedPath);

            var provider = new RarCompressionProvider(archivePath);
            try
            {
                CollectionAssert.AreEqual(
                    new[] { "rar-stream-fixture.txt" },
                    provider.Contents.ToArray(),
                    "The production provider did not enumerate the exact fixture contents.");

                using (Stream stream = provider.Decompress("rar-stream-fixture.txt"))
                {
                    Assert.AreEqual(expected.Length, stream.Length);
                    CollectionAssert.AreEqual(expected, ReadExactly(stream, expected.Length));

                    const int seekPosition = 37;
                    Assert.AreEqual(
                        seekPosition,
                        stream.Seek(seekPosition, SeekOrigin.Begin));
                    byte[] replayed = ReadExactly(
                        stream,
                        expected.Length - seekPosition);
                    CollectionAssert.AreEqual(
                        expected.Skip(seekPosition).ToArray(),
                        replayed,
                        "Backward seek did not replay the same decoded bytes.");
                }
            }
            finally
            {
                provider.Close();
            }
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            byte[] result = new byte[count];
            int offset = 0;
            while (offset < result.Length)
            {
                int read = stream.Read(result, offset, result.Length - offset);
                if (read == 0)
                    break;
                offset += read;
            }

            Assert.AreEqual(
                result.Length,
                offset,
                "The RAR stream ended before the advertised payload length.");
            return result;
        }
    }
}
