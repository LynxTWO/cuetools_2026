using System;
using System.Collections.Generic;
using System.Linq;
using CUETools.Wpf.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// Disc-number padding across every set size tier: up to 9 discs, up to 99, up to 999, up to 9999.
    /// The property that actually matters is not the digit count but the CONSEQUENCE of it: the disc
    /// folders must sort in disc order in a plain lexical (file browser) sort. Unpadded names interleave
    /// - "Disc 1", "Disc 10", "Disc 100", "Disc 11", "Disc 2" - which is what a big box set looked like
    /// on disk before this. Padding is to the SET's width, so ordinary 2-CD releases are unchanged.
    /// </summary>
    [TestClass]
    public class DiscPaddingTests
    {
        private static string DiscFolderOf(int discNumber, int totalDiscs, string subtitle = "")
        {
            var c = new NamingContext
            {
                AlbumArtist = "A", Artist = "A", Album = "B", Title = "T",
                DiscNumber = discNumber, TotalDiscs = totalDiscs, DiscSubtitle = subtitle,
                TrackNumber = 1, TotalTracks = 1,
            };
            // %disc% renders "Disc N/" (with the trailing separator) for a multi-disc set
            string path = NamingEngine.Render(c, new NamingScheme
            {
                Template = "%disc%x", ReleaseDescriptor = false,
            });
            // the template yields "Disc N/x" - take the folder segment
            int slash = path.LastIndexOf('/');
            return slash >= 0 ? path.Substring(0, slash) : "";
        }

        // ---- the four tiers the owner asked for ----

        [DataTestMethod]
        [DataRow(9, 1)]       // up to 9 discs   -> width 1: "Disc 1".."Disc 9"
        [DataRow(99, 2)]      // up to 99 discs  -> width 2: "Disc 01".."Disc 99"
        [DataRow(999, 3)]     // up to 999 discs -> width 3: "Disc 001".."Disc 999"
        [DataRow(9999, 4)]    // up to 9999      -> width 4: "Disc 0001".."Disc 9999"
        public void PadsToTheWidthOfTheSet(int totalDiscs, int expectedWidth)
        {
            string first = DiscFolderOf(1, totalDiscs);
            Assert.AreEqual("Disc " + new string('0', expectedWidth - 1) + "1", first,
                $"set of {totalDiscs} should pad disc 1 to {expectedWidth} digits");

            string last = DiscFolderOf(totalDiscs, totalDiscs);
            Assert.AreEqual("Disc " + totalDiscs.ToString(), last,
                "the highest disc is already full width, so it must not gain zeros");
        }

        [DataTestMethod]
        [DataRow(9)]
        [DataRow(99)]
        [DataRow(999)]
        [DataRow(9999)]
        public void LexicalFolderOrderEqualsDiscOrder(int totalDiscs)
        {
            // the whole point of padding, asserted directly over a COMPLETE set
            var inDiscOrder = new List<string>(totalDiscs);
            for (int d = 1; d <= totalDiscs; d++) inDiscOrder.Add(DiscFolderOf(d, totalDiscs));

            var lexical = inDiscOrder.OrderBy(x => x, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(inDiscOrder, lexical,
                $"a {totalDiscs}-disc set does not sort in disc order lexically");

            // and every disc still has its own distinct folder
            Assert.AreEqual(totalDiscs, new HashSet<string>(inDiscOrder, StringComparer.OrdinalIgnoreCase).Count);
        }

        // ---- tier boundaries: the digit count must step exactly at 10, 100 and 1000 ----

        [DataTestMethod]
        [DataRow(9, "Disc 1")]
        [DataRow(10, "Disc 01")]
        [DataRow(99, "Disc 01")]
        [DataRow(100, "Disc 001")]
        [DataRow(999, "Disc 001")]
        [DataRow(1000, "Disc 0001")]
        [DataRow(9999, "Disc 0001")]
        public void WidthStepsExactlyAtEachBoundary(int totalDiscs, string expectedFirstFolder)
        {
            Assert.AreEqual(expectedFirstFolder, DiscFolderOf(1, totalDiscs));
        }

        // ---- unchanged behaviour ----

        [TestMethod]
        public void SingleDisc_HasNoDiscFolderAtAll()
        {
            Assert.AreEqual("", DiscFolderOf(1, 1));
        }

        [TestMethod]
        public void TwoDiscSet_IsUnchangedByPadding()
        {
            // ordinary releases must look exactly as they did before
            Assert.AreEqual("Disc 1", DiscFolderOf(1, 2));
            Assert.AreEqual("Disc 2", DiscFolderOf(2, 2));
        }

        [TestMethod]
        public void SubtitleStillFollowsThePaddedNumber()
        {
            Assert.AreEqual("Disc 007 - Live in Tokyo", DiscFolderOf(7, 250, "Live in Tokyo"));
        }

        // ---- defensive: bad metadata must never collide two discs ----

        [TestMethod]
        public void DiscNumberHigherThanTheTotal_IsNotTruncated()
        {
            // real-world tagging error ("disc 12 of 5"): padding to the TOTAL's width alone would render
            // "Disc 12" for both 12 and 2 if it truncated, so the number's own width has to win
            Assert.AreEqual("Disc 12", DiscFolderOf(12, 5));
            Assert.AreEqual("Disc 2", DiscFolderOf(2, 5));
            Assert.AreNotEqual(DiscFolderOf(12, 5), DiscFolderOf(2, 5));
        }

        [TestMethod]
        public void BeyondTenThousandDiscs_StillDistinctAndOrdered()
        {
            // no realistic set, but the width rule must not cap out
            Assert.AreEqual("Disc 00001", DiscFolderOf(1, 10000));
            Assert.AreEqual("Disc 10000", DiscFolderOf(10000, 10000));
            var a = DiscFolderOf(9999, 10000);
            var b = DiscFolderOf(10000, 10000);
            Assert.IsTrue(string.CompareOrdinal(a, b) < 0, $"{a} should sort before {b}");
        }

        [TestMethod]
        public void DiscNumberToken_IsPaddedForSetsAndBareForSingles()
        {
            var scheme = new NamingScheme { Template = "%discnumber%", ReleaseDescriptor = false };
            var big = new NamingContext { Album = "B", Title = "T", DiscNumber = 7, TotalDiscs = 250, TrackNumber = 1 };
            var one = new NamingContext { Album = "B", Title = "T", DiscNumber = 1, TotalDiscs = 1, TrackNumber = 1 };
            Assert.AreEqual("007", NamingEngine.Render(big, scheme));
            Assert.AreEqual("1", NamingEngine.Render(one, scheme));
        }
    }
}
