using System;
using System.IO;
using System.Net;
using System.Text;
using CUETools.CDImage;
using CUETools.CTDB;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// A URL that cannot be parsed must be reported as a failed CTDB request, never thrown at
    /// the caller.
    ///
    /// Live on 2026-08-27, a salvage Test pass read the damaged disc in K: for 1,628 seconds and
    /// was then discarded whole because the post-read lookup threw UriFormatException out of
    /// WebRequest.Create. That particular malformed URL was a client bug and is fixed, but the
    /// shape was general: every request in this client was built outside its own try block, so a
    /// bad server name in the user's configuration, or a malformed location in the server's own
    /// response, took the same path. These tests build each request from an unparseable URL and
    /// require the documented failure result instead of an exception.
    ///
    /// None of them touch the network: Uri parsing fails before any connection is attempted.
    /// </summary>
    [TestClass]
    public class CtdbRequestFailureTests
    {
        // The exact shape the live failure produced: an empty authority, which is what
        // "https://" plus a server name beginning with a slash builds.
        private const string UnparseableUrl = "https:///db.cue.tools/lookup2.php";

        private const string OneEntryDoc =
            "<ctdb xmlns=\"http://db.cuetools.net/ns/mmd-1.0#\">" +
            "<entry confidence=\"1\" crc32=\"35bb2f0e\" hasparity=\"http://p.cuetools.net/11377169\" " +
            "id=\"11377169\" npar=\"8\" stride=\"5880\" syndrome=\"6++hyvHxVVrUTxoxhsGTCg==\" " +
            "toc=\"0:300:600\" trackcrcs=\"e7d27a34 101c9535\" />" +
            "</ctdb>";

        private static CUEToolsDB NewClient() =>
            new CUEToolsDB(new CDImageLayout("0 10 20"), null);

        private static DBEntry ParseOneEntry()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(OneEntryDoc));
            CTDBResponse response = CTDBResponseParser.Parse(stream);
            Assert.IsNotNull(response.entry, "fixture should carry one entry");
            Assert.AreEqual(1, response.entry.Length, "fixture should carry one entry");
            return new DBEntry(response.entry[0]);
        }

        [TestMethod]
        public void AServerNameThatWillNotParseFailsTheLookupInsteadOfThrowing()
        {
            var db = NewClient();
            // CTDBServer is user-editable in the advanced configuration, so this is reachable
            // without any client bug: a leading slash empties the authority.
            db.UseServer("/db.cue.tools");
            StringAssert.StartsWith(db.LookupUrl(true, true, CTDBMetadataSearch.None), "https:///");

            db.ContactDB(true, true, CTDBMetadataSearch.None);

            Assert.AreNotEqual(WebExceptionStatus.Success, db.QueryExceptionStatus,
                "an unbuildable request is a failed query");
            Assert.AreEqual(0, System.Linq.Enumerable.Count(db.Entries),
                "a failed query offers no matches");
            Assert.AreEqual(0, db.Total, "a failed query carries no confidence");
        }

        [TestMethod]
        public void AFetchUrlThatWillNotParseReportsAFailedFetch()
        {
            // FetchFile is handed locations out of a CTDB response (artwork and parity mirrors),
            // so a malformed one is untrusted input rather than a programming error.
            var db = NewClient();
            using var output = new MemoryStream();

            Assert.IsFalse(db.FetchFile(UnparseableUrl, output), "a bad location is a failed fetch");
            Assert.AreEqual(0, output.Length, "nothing is written from a request that was never made");
        }

        [TestMethod]
        public void AParityLocationThatWillNotParseReportsBadRequest()
        {
            var db = NewClient();
            db.UseServer("db.cue.tools");
            DBEntry entry = ParseOneEntry();
            entry.hasParity = "https:///p.cue.tools/11377169";

            Assert.IsNull(db.FetchDB(entry, 8, null), "no syndromes come back from a bad location");
            Assert.AreEqual(HttpStatusCode.BadRequest, entry.httpStatus,
                "DoVerify stops on any non-OK status, so repair never runs on nothing");
        }

        [TestMethod]
        public void AnEmptyParityLocationReportsBadRequest()
        {
            var db = NewClient();
            db.UseServer("db.cue.tools");
            DBEntry entry = ParseOneEntry();
            entry.hasParity = "";

            Assert.IsNull(db.FetchDB(entry, 8, null), "no syndromes come back from an empty location");
            Assert.AreEqual(HttpStatusCode.BadRequest, entry.httpStatus,
                "an empty location is a bad request, not an index-out-of-range through the caller");
        }

        [TestMethod]
        public void EveryRequestInTheClientIsBuiltInsideItsTry()
        {
            // The guarantee above is structural: if a later edit moves a WebRequest.Create back
            // above its try, that method throws at its caller again. Every construction site must
            // sit inside a try block.
            string root = DeadSwitchAnalyzer.FindRepoRoot(AppContext.BaseDirectory);
            string[] lines = File.ReadAllLines(Path.Combine(root, "CUETools.CTDB", "CUEToolsDB.cs"));

            int checkedSites = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf("WebRequest.Create", StringComparison.Ordinal) < 0)
                    continue;
                checkedSites++;
                bool guarded = false;
                // Walk back to the start of the enclosing method; a try must open on the way.
                for (int j = i - 1; j >= 0; j--)
                {
                    string text = lines[j].Trim();
                    if (text == "try")
                    {
                        guarded = true;
                        break;
                    }
                    if (text.StartsWith("public ", StringComparison.Ordinal) ||
                        text.StartsWith("private ", StringComparison.Ordinal) ||
                        text.StartsWith("internal ", StringComparison.Ordinal))
                        break;
                }
                Assert.IsTrue(guarded,
                    "WebRequest.Create at CUEToolsDB.cs line " + (i + 1) + " is outside a try block");
            }

            Assert.AreEqual(4, checkedSites,
                "expected the lookup, file fetch, parity fetch, and submit request sites");
        }
    }
}
