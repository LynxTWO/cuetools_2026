using System;
using System.IO;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests;

[TestClass]
public sealed class DiagnosticLogTests
{
    [TestMethod]
    public void ErrorRedactsCustomRootCaseInsensitivelyEverywhere()
    {
        string sandbox = Path.Combine(
            Path.GetTempPath(),
            "CUETools.Wpf.Tests",
            Guid.NewGuid().ToString("N"));
        string logPath = Path.Combine(sandbox, "diagnostic.log");
        string sensitiveRoot = Path.Combine(sandbox, "Customer-Library-MixedCase");

        try
        {
            var log = new DiagnosticLog(logPath);
            // "Red" deliberately occurs inside the replacement token. It must be evaluated against
            // the original diagnostic text, never against text inserted by an earlier replacement.
            log.Redact(sensitiveRoot, "Red");

            string upper = sensitiveRoot.ToUpperInvariant();
            string lower = sensitiveRoot.ToLowerInvariant();
            var inner = new SyntheticStackException(
                "nested exception opened " + lower + Path.DirectorySeparatorChar + "track.flac",
                "   at Decoder.Read() in " + upper +
                    Path.DirectorySeparatorChar + "Codec.cs:line 42");
            var outer = new InvalidOperationException(
                "outer exception finalized " + lower +
                    Path.DirectorySeparatorChar + "album.cue",
                inner);

            log.Error(
                "test",
                "direct message wrote " + upper +
                    Path.DirectorySeparatorChar + "output.flac",
                outer);

            string text = File.ReadAllText(logPath);
            Assert.AreEqual(
                -1,
                text.IndexOf(sensitiveRoot, StringComparison.OrdinalIgnoreCase),
                "The complete registered root must not survive in any casing.");
            Assert.AreEqual(
                -1,
                text.IndexOf("Customer-Library-MixedCase",
                    StringComparison.OrdinalIgnoreCase),
                "Replacing only an overlapping user/profile prefix still leaks the custom root.");
            StringAssert.Contains(text, "direct message wrote <redacted>");
            StringAssert.Contains(text, "nested exception opened <redacted>");
            StringAssert.Contains(text, "at Decoder.Read() in <redacted>");
        }
        finally
        {
            try
            {
                if (Directory.Exists(sandbox))
                    Directory.Delete(sandbox, recursive: true);
            }
            catch
            {
                // Test cleanup must not obscure a redaction assertion.
            }
        }
    }

    private sealed class SyntheticStackException : Exception
    {
        private readonly string _stackTrace;

        public SyntheticStackException(string message, string stackTrace)
            : base(message)
        {
            _stackTrace = stackTrace;
        }

        public override string StackTrace => _stackTrace;
    }
}
