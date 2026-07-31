using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Ripper.Tests
{
    [TestClass]
    public class ReadCommunicationRetrySourceContractTests
    {
        [TestMethod]
        public void CommunicationRetryStaysOutsideSharedFetchAndUsesNoAbortRetry()
        {
            string source = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "CUETools.Ripper.SCSI",
                "SCSIDrive.cs"));
            const string PolicyCall =
                "ShouldRetryObservedReadCommunicationFailure";

            Assert.AreEqual(
                1,
                CountOccurrences(source, PolicyCall),
                "The observed communication retry must have one top-level route.");

            int fetchStart = source.IndexOf(
                "private unsafe Device.CommandStatus FetchSectors",
                StringComparison.Ordinal);
            int fetchEnd = source.IndexOf(
                "private void MarkSectorUnreadable",
                fetchStart,
                StringComparison.Ordinal);
            Assert.IsTrue(fetchStart >= 0 && fetchEnd > fetchStart);
            Assert.IsFalse(
                source.Substring(fetchStart, fetchEnd - fetchStart)
                    .Contains(PolicyCall),
                "Pinpoint and batch-decomposition reads share FetchSectors and " +
                "must not inherit this transport retry.");

            int retryStart = source.IndexOf(PolicyCall, StringComparison.Ordinal);
            int retryEnd = source.IndexOf(
                "_cacheDefeatJustFlushed = false;",
                retryStart,
                StringComparison.Ordinal);
            Assert.IsTrue(retryStart > fetchEnd && retryEnd > retryStart);
            string retryBlock = source.Substring(
                retryStart,
                retryEnd - retryStart);
            StringAssert.Contains(
                retryBlock,
                "FetchSectors(sector, Sectors2Read, false)");
            StringAssert.Contains(retryBlock, "communication-retry=True");
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(
                value,
                index,
                StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "CUETools.sln")))
                    return current.FullName;
                current = current.Parent;
            }
            Assert.Fail("Could not locate the repository root from the test output.");
            return null;
        }
    }
}
