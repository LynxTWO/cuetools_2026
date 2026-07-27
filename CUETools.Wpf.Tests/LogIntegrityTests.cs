using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    [TestClass]
    public class LogIntegrityTests
    {
        [TestMethod]
        public void SealedLogVerifiesAcrossLineEndings()
        {
            string sealedLog = LogIntegrity.Seal("one\r\ntwo\r\n");

            Assert.IsTrue(LogIntegrity.Verify(sealedLog, out string embedded, out string recomputed));
            Assert.AreEqual(embedded, recomputed);
        }

        [TestMethod]
        public void AppendedTextInvalidatesSealedLog()
        {
            string sealedLog = LogIntegrity.Seal("one\ntwo");

            Assert.IsFalse(LogIntegrity.Verify(sealedLog + "APPENDED TAMPER\n", out _, out _));
        }

        [TestMethod]
        public void PrependedTextInvalidatesSealedLog()
        {
            string sealedLog = LogIntegrity.Seal("one\ntwo");

            Assert.IsFalse(LogIntegrity.Verify("PREPENDED TAMPER\n" + sealedLog, out _, out _));
        }

        [TestMethod]
        public void DuplicateFooterInvalidatesSealedLog()
        {
            string sealedLog = LogIntegrity.Seal("one\ntwo");
            string digest = LogIntegrity.ComputeDigest("one\ntwo");

            Assert.IsFalse(LogIntegrity.Verify(
                sealedLog + LogIntegrity.Footer(digest) + "\n", out _, out _));
        }
    }
}
