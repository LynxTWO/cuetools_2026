using System;
using System.IO;
using System.Text;
using System.Xml.Serialization;
using CUETools.CTDB;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CUETools.Wpf.Tests
{
    /// <summary>
    /// Pins CTDBResponseParser to XmlSerializer's materialization semantics.
    /// The parser replaced the sgen-era pre-generated serializer, which broke
    /// under AOT-mode feature switches (IsDynamicCodeSupported=false) with
    /// "error in XML document (0, 0)" on the Linux head. These differential
    /// tests run the reflection XmlSerializer (valid on this JIT test host)
    /// against the parser on the same documents and require identical object
    /// graphs, including the subtle case where an empty element still
    /// materializes an entry object with default field values.
    /// </summary>
    [TestClass]
    public class CtdbResponseParserTests
    {
        private const string EmptyEntryDoc =
            "<ctdb xmlns=\"http://db.cuetools.net/ns/mmd-1.0#\"><entry /></ctdb>";

        private const string RootAttributesDoc =
            "<ctdb xmlns=\"http://db.cuetools.net/ns/mmd-1.0#\" status=\"parity needed\" " +
            "updateurl=\"http://example.invalid/update\" updatemsg=\"update me\" " +
            "message=\"hello\" npar=\"8\" />";

        // Shape captured live from lookup2.php (fuzzy) on 2026-08-12, trimmed
        // to one entry and two metadata records; values anonymized where they
        // did not matter to parsing.
        private const string FullDoc =
            "<ctdb xmlns=\"http://db.cuetools.net/ns/mmd-1.0#\" xmlns:ext=\"http://db.cuetools.net/ns/ext-1.0#\">\n" +
            " <entry confidence=\"1\" crc32=\"35bb2f0e\" hasparity=\"http://p.cuetools.net/11377169\" " +
            "id=\"11377169\" npar=\"8\" stride=\"5880\" syndrome=\"6++hyvHxVVrUTxoxhsGTCg==\" " +
            "toc=\"0:300:600\" trackcrcs=\"e7d27a34 101c9535\" />\n" +
            " <metadata album=\"Some Album\" artist=\"Some Artist\" disccount=\"1\" discname=\"\" " +
            "discnumber=\"1\" id=\"37a2293b-d51f-4092-ae11-9a6602354ba4\" source=\"musicbrainz\" year=\"2017\">\n" +
            "  <track artist=\"Some Artist\" name=\"First Song\" />\n" +
            "  <track artist=\"Some Artist\" name=\"Second Song\"><extra>bonus notes</extra></track>\n" +
            "  <label catno=\"CAT-001\" name=\"Some Label\" />\n" +
            "  <release country=\"XE\" date=\"2017-04-01\" />\n" +
            "  <coverart uri=\"http://example.invalid/full.jpg\" uri150=\"http://example.invalid/t.jpg\" " +
            "height=\"500\" width=\"500\" primary=\"true\" />\n" +
            "  <extra>album extra text</extra>\n" +
            " </metadata>\n" +
            " <metadata album=\"Other Album\" artist=\"Other Artist\" source=\"freedb\" year=\"1999\" " +
            "barcode=\"012345678905\" genre=\"Rock\" infourl=\"http://example.invalid/info\" />\n" +
            " <unknownelement someattr=\"1\"><nested /></unknownelement>\n" +
            "</ctdb>";

        [TestMethod]
        public void ParserMatchesXmlSerializerOnEmptyEntryDocument()
        {
            AssertParserMatchesSerializer(EmptyEntryDoc);
        }

        [TestMethod]
        public void ParserMatchesXmlSerializerOnRootAttributesDocument()
        {
            AssertParserMatchesSerializer(RootAttributesDoc);
        }

        [TestMethod]
        public void ParserMatchesXmlSerializerOnFullDocument()
        {
            AssertParserMatchesSerializer(FullDoc);
        }

        [TestMethod]
        public void EmptyEntryElementMaterializesDefaultEntry()
        {
            CTDBResponse parsed = Parse(EmptyEntryDoc);
            Assert.IsNotNull(parsed.entry);
            Assert.AreEqual(1, parsed.entry.Length);
            Assert.AreEqual(0, parsed.entry[0].confidence);
            Assert.IsNull(parsed.entry[0].crc32);
            Assert.IsNull(parsed.metadata);
        }

        [TestMethod]
        public void FullDocumentFieldsSurviveParsing()
        {
            CTDBResponse parsed = Parse(FullDoc);
            Assert.AreEqual(1, parsed.entry.Length);
            Assert.AreEqual(11377169L, parsed.entry[0].id);
            Assert.AreEqual(8, parsed.entry[0].npar);
            Assert.AreEqual("0:300:600", parsed.entry[0].toc);
            Assert.AreEqual(2, parsed.metadata.Length);
            Assert.AreEqual("Some Album", parsed.metadata[0].album);
            Assert.AreEqual("album extra text", parsed.metadata[0].extra);
            Assert.AreEqual(2, parsed.metadata[0].track.Length);
            Assert.AreEqual("bonus notes", parsed.metadata[0].track[1].extra);
            Assert.AreEqual("CAT-001", parsed.metadata[0].label[0].catno);
            Assert.AreEqual("XE", parsed.metadata[0].release[0].country);
            Assert.AreEqual(500, parsed.metadata[0].coverart[0].width);
            Assert.IsTrue(parsed.metadata[0].coverart[0].primary);
            Assert.AreEqual("freedb", parsed.metadata[1].source);
            Assert.IsNull(parsed.metadata[1].track);
        }

        [TestMethod]
        public void DoctypeDeclarationsAreRejected()
        {
            string doc = "<!DOCTYPE ctdb [<!ENTITY x \"y\">]>" + EmptyEntryDoc;
            Assert.ThrowsException<System.Xml.XmlException>(() => Parse(doc));
        }

        private static CTDBResponse Parse(string xml)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
                return CTDBResponseParser.Parse(stream);
        }

        private static void AssertParserMatchesSerializer(string xml)
        {
            CTDBResponse fromParser = Parse(xml);
            CTDBResponse fromSerializer;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml)))
                fromSerializer = (CTDBResponse)new XmlSerializer(typeof(CTDBResponse)).Deserialize(stream);
            AssertResponsesEqual(fromSerializer, fromParser);
        }

        private static void AssertResponsesEqual(CTDBResponse expected, CTDBResponse actual)
        {
            Assert.AreEqual(expected.status, actual.status, "status");
            Assert.AreEqual(expected.updateurl, actual.updateurl, "updateurl");
            Assert.AreEqual(expected.updatemsg, actual.updatemsg, "updatemsg");
            Assert.AreEqual(expected.message, actual.message, "message");
            Assert.AreEqual(expected.npar, actual.npar, "npar");

            Assert.AreEqual(expected.entry == null, actual.entry == null, "entry null-ness");
            if (expected.entry != null)
            {
                Assert.AreEqual(expected.entry.Length, actual.entry.Length, "entry count");
                for (int i = 0; i < expected.entry.Length; i++)
                {
                    CTDBResponseEntry e = expected.entry[i], a = actual.entry[i];
                    Assert.AreEqual(e.id, a.id, "entry.id");
                    Assert.AreEqual(e.crc32, a.crc32, "entry.crc32");
                    Assert.AreEqual(e.confidence, a.confidence, "entry.confidence");
                    Assert.AreEqual(e.npar, a.npar, "entry.npar");
                    Assert.AreEqual(e.stride, a.stride, "entry.stride");
                    Assert.AreEqual(e.hasparity, a.hasparity, "entry.hasparity");
                    Assert.AreEqual(e.parity, a.parity, "entry.parity");
                    Assert.AreEqual(e.syndrome, a.syndrome, "entry.syndrome");
                    Assert.AreEqual(e.trackcrcs, a.trackcrcs, "entry.trackcrcs");
                    Assert.AreEqual(e.toc, a.toc, "entry.toc");
                }
            }

            Assert.AreEqual(expected.metadata == null, actual.metadata == null, "metadata null-ness");
            if (expected.metadata != null)
            {
                Assert.AreEqual(expected.metadata.Length, actual.metadata.Length, "metadata count");
                for (int i = 0; i < expected.metadata.Length; i++)
                {
                    CTDBResponseMeta e = expected.metadata[i], a = actual.metadata[i];
                    Assert.AreEqual(e.source, a.source, "meta.source");
                    Assert.AreEqual(e.id, a.id, "meta.id");
                    Assert.AreEqual(e.artist, a.artist, "meta.artist");
                    Assert.AreEqual(e.album, a.album, "meta.album");
                    Assert.AreEqual(e.year, a.year, "meta.year");
                    Assert.AreEqual(e.genre, a.genre, "meta.genre");
                    Assert.AreEqual(e.extra, a.extra, "meta.extra");
                    Assert.AreEqual(e.discnumber, a.discnumber, "meta.discnumber");
                    Assert.AreEqual(e.disccount, a.disccount, "meta.disccount");
                    Assert.AreEqual(e.discname, a.discname, "meta.discname");
                    Assert.AreEqual(e.infourl, a.infourl, "meta.infourl");
                    Assert.AreEqual(e.barcode, a.barcode, "meta.barcode");
                    AssertArraysEqual(e.coverart, a.coverart, "coverart",
                        (x, y, tag) =>
                        {
                            Assert.AreEqual(x.uri, y.uri, tag + ".uri");
                            Assert.AreEqual(x.uri150, y.uri150, tag + ".uri150");
                            Assert.AreEqual(x.height, y.height, tag + ".height");
                            Assert.AreEqual(x.width, y.width, tag + ".width");
                            Assert.AreEqual(x.primary, y.primary, tag + ".primary");
                        });
                    AssertArraysEqual(e.track, a.track, "track",
                        (x, y, tag) =>
                        {
                            Assert.AreEqual(x.name, y.name, tag + ".name");
                            Assert.AreEqual(x.artist, y.artist, tag + ".artist");
                            Assert.AreEqual(x.extra, y.extra, tag + ".extra");
                        });
                    AssertArraysEqual(e.label, a.label, "label",
                        (x, y, tag) =>
                        {
                            Assert.AreEqual(x.name, y.name, tag + ".name");
                            Assert.AreEqual(x.catno, y.catno, tag + ".catno");
                        });
                    AssertArraysEqual(e.release, a.release, "release",
                        (x, y, tag) =>
                        {
                            Assert.AreEqual(x.date, y.date, tag + ".date");
                            Assert.AreEqual(x.country, y.country, tag + ".country");
                        });
                }
            }
        }

        private static void AssertArraysEqual<T>(
            T[] expected, T[] actual, string tag, Action<T, T, string> compare)
        {
            Assert.AreEqual(expected == null, actual == null, tag + " null-ness");
            if (expected == null)
                return;
            Assert.AreEqual(expected.Length, actual.Length, tag + " count");
            for (int i = 0; i < expected.Length; i++)
                compare(expected[i], actual[i], tag + "[" + i + "]");
        }
    }
}
