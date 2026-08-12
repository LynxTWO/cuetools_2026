using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace CUETools.CTDB
{
    /// <summary>
    /// Explicit XmlReader-based parser for CTDB lookup/submit responses,
    /// replacing the sgen-era pre-generated XmlSerializer. Motivation: under
    /// AOT-mode feature switches (IsDynamicCodeSupported=false, set by
    /// PublishAot even for JIT builds) modern .NET's XmlSerializer abandons
    /// the pre-generated fast path and dies in reflection mode with
    /// ArgumentNullException, so every CTDB lookup failed with "error in XML
    /// document (0, 0)" on the Linux head. This parser is deterministic,
    /// culture-free (XmlConvert), rejects DTDs, ignores unknown elements and
    /// attributes for forward compatibility, and matches XmlSerializer's
    /// materialization semantics field for field (differential tests pin
    /// this, including the empty-element-creates-empty-entry case).
    /// </summary>
    public static class CTDBResponseParser
    {
        private const string Ns = "http://db.cuetools.net/ns/mmd-1.0#";

        public static CTDBResponse Parse(Stream stream)
        {
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                CloseInput = false,
                XmlResolver = null,
#if NET20
                ProhibitDtd = true,
#else
                DtdProcessing = DtdProcessing.Prohibit,
#endif
            };
            using (XmlReader reader = XmlReader.Create(stream, settings))
            {
                reader.MoveToContent();
                if (reader.NodeType != XmlNodeType.Element ||
                    reader.LocalName != "ctdb" ||
                    reader.NamespaceURI != Ns)
                {
                    throw new InvalidOperationException(
                        "Unexpected CTDB response root element.");
                }

                var response = new CTDBResponse
                {
                    status = reader.GetAttribute("status"),
                    updateurl = reader.GetAttribute("updateurl"),
                    updatemsg = reader.GetAttribute("updatemsg"),
                    message = reader.GetAttribute("message"),
                    npar = ReadIntAttribute(reader, "npar"),
                };

                var entries = new List<CTDBResponseEntry>();
                var metadata = new List<CTDBResponseMeta>();
                ForEachChild(reader, delegate(XmlReader child)
                {
                    if (child.NamespaceURI != Ns)
                    {
                        child.Skip();
                        return;
                    }
                    switch (child.LocalName)
                    {
                        case "entry":
                            entries.Add(ParseEntry(child));
                            break;
                        case "metadata":
                            metadata.Add(ParseMeta(child));
                            break;
                        default:
                            child.Skip();
                            break;
                    }
                });

                if (entries.Count > 0)
                    response.entry = entries.ToArray();
                if (metadata.Count > 0)
                    response.metadata = metadata.ToArray();
                return response;
            }
        }

        private static CTDBResponseEntry ParseEntry(XmlReader reader)
        {
            var entry = new CTDBResponseEntry
            {
                id = ReadLongAttribute(reader, "id"),
                crc32 = reader.GetAttribute("crc32"),
                confidence = ReadIntAttribute(reader, "confidence"),
                npar = ReadIntAttribute(reader, "npar"),
                stride = ReadIntAttribute(reader, "stride"),
                hasparity = reader.GetAttribute("hasparity"),
                parity = reader.GetAttribute("parity"),
                syndrome = reader.GetAttribute("syndrome"),
                trackcrcs = reader.GetAttribute("trackcrcs"),
                toc = reader.GetAttribute("toc"),
            };
            reader.Skip();
            return entry;
        }

        private static CTDBResponseMeta ParseMeta(XmlReader reader)
        {
            var meta = new CTDBResponseMeta
            {
                source = reader.GetAttribute("source"),
                id = reader.GetAttribute("id"),
                artist = reader.GetAttribute("artist"),
                album = reader.GetAttribute("album"),
                year = reader.GetAttribute("year"),
                genre = reader.GetAttribute("genre"),
                discnumber = reader.GetAttribute("discnumber"),
                disccount = reader.GetAttribute("disccount"),
                discname = reader.GetAttribute("discname"),
                infourl = reader.GetAttribute("infourl"),
                barcode = reader.GetAttribute("barcode"),
            };

            var coverart = new List<CTDBResponseMetaImage>();
            var tracks = new List<CTDBResponseMetaTrack>();
            var labels = new List<CTDBResponseMetaLabel>();
            var releases = new List<CTDBResponseMetaRelease>();
            ForEachChild(reader, delegate(XmlReader child)
            {
                if (child.NamespaceURI != Ns)
                {
                    child.Skip();
                    return;
                }
                switch (child.LocalName)
                {
                    case "extra":
                        meta.extra = child.ReadElementContentAsString();
                        break;
                    case "coverart":
                        coverart.Add(new CTDBResponseMetaImage
                        {
                            uri = child.GetAttribute("uri"),
                            uri150 = child.GetAttribute("uri150"),
                            height = ReadIntAttribute(child, "height"),
                            width = ReadIntAttribute(child, "width"),
                            primary = ReadBoolAttribute(child, "primary"),
                        });
                        child.Skip();
                        break;
                    case "track":
                        tracks.Add(ParseTrack(child));
                        break;
                    case "label":
                        labels.Add(new CTDBResponseMetaLabel
                        {
                            name = child.GetAttribute("name"),
                            catno = child.GetAttribute("catno"),
                        });
                        child.Skip();
                        break;
                    case "release":
                        releases.Add(new CTDBResponseMetaRelease
                        {
                            date = child.GetAttribute("date"),
                            country = child.GetAttribute("country"),
                        });
                        child.Skip();
                        break;
                    default:
                        child.Skip();
                        break;
                }
            });

            if (coverart.Count > 0) meta.coverart = coverart.ToArray();
            if (tracks.Count > 0) meta.track = tracks.ToArray();
            if (labels.Count > 0) meta.label = labels.ToArray();
            if (releases.Count > 0) meta.release = releases.ToArray();
            return meta;
        }

        private static CTDBResponseMetaTrack ParseTrack(XmlReader reader)
        {
            var track = new CTDBResponseMetaTrack
            {
                name = reader.GetAttribute("name"),
                artist = reader.GetAttribute("artist"),
            };
            ForEachChild(reader, delegate(XmlReader child)
            {
                if (child.NamespaceURI == Ns && child.LocalName == "extra")
                    track.extra = child.ReadElementContentAsString();
                else
                    child.Skip();
            });
            return track;
        }

        private delegate void ChildHandler(XmlReader reader);

        /// <summary>
        /// Invokes the handler positioned on each child element of the
        /// current element, then leaves the reader past the element's end.
        /// The handler must consume its element (Skip or
        /// ReadElementContentAsString). Handles empty elements.
        /// </summary>
        private static void ForEachChild(XmlReader reader, ChildHandler handle)
        {
            if (reader.IsEmptyElement)
            {
                reader.Skip();
                return;
            }
            reader.ReadStartElement();
            while (reader.NodeType != XmlNodeType.EndElement && reader.NodeType != XmlNodeType.None)
            {
                if (reader.NodeType == XmlNodeType.Element)
                    handle(reader);
                else
                    reader.Read();
            }
            reader.ReadEndElement();
        }

        private static int ReadIntAttribute(XmlReader reader, string name)
        {
            string value = reader.GetAttribute(name);
            return string.IsNullOrEmpty(value) ? 0 : XmlConvert.ToInt32(value);
        }

        private static long ReadLongAttribute(XmlReader reader, string name)
        {
            string value = reader.GetAttribute(name);
            return string.IsNullOrEmpty(value) ? 0 : XmlConvert.ToInt64(value);
        }

        private static bool ReadBoolAttribute(XmlReader reader, string name)
        {
            string value = reader.GetAttribute(name);
            return !string.IsNullOrEmpty(value) && XmlConvert.ToBoolean(value);
        }
    }
}
