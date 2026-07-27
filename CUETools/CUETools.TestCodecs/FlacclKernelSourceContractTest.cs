using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.TestCodecs
{
    [TestClass]
    public class FlacclKernelSourceContractTest
    {
        [TestMethod]
        public void ResidualEstimator_UsesBarrieredThirtyTwoSampleReduction()
        {
            string root = FindRepoRoot();
            if (root == null)
            {
                Assert.Inconclusive("Repository source is unavailable for the FLACCL kernel contract.");
                return;
            }

            string source = File.ReadAllText(Path.Combine(
                root, "CUETools.Codecs.FLACCL", "flac.cl"));

            const string reductionPattern =
                @"for\s*\(int l = 1 << \(ESTPARTLOG - 1\); l > 0; l >>= 1\)\s*" +
                @"\{\s*if \(\(tid & \(\(1 << ESTPARTLOG\) - 1\)\) < l\)\s*" +
                @"idata\[tid\] \+= idata\[tid \+ l\];\s*" +
                @"barrier\(CLK_LOCAL_MEM_FENCE\);\s*\}";

            Assert.AreEqual(
                2,
                Regex.Matches(source, reductionPattern, RegexOptions.CultureInvariant).Count,
                "Both the full-block and tail estimator paths must synchronize every reduction step.");
            StringAssert.Contains(source, "__local volatile uint idata[GROUP_SIZE];");
            Assert.IsFalse(
                source.Contains("idata[GROUP_SIZE + 16]"),
                "The estimator must not rely on out-of-segment scratch reads.");
            Assert.IsFalse(
                source.Contains("idata[tid] + idata[tid + 1]"),
                "The old implicit warp-synchronous reduction must not return.");
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CUETools.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            return null;
        }
    }
}
