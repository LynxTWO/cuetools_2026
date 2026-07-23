using System;
using System.Collections.Generic;
using System.Text;

namespace CUETools.Ripper.SCSI
{
    /// <summary>The decoded CD-Text of one disc: album-level title/performer plus per-track
    /// titles/performers, and pack accounting so a caller can judge the read's health.</summary>
    public sealed class CDTextInfo
    {
        public string AlbumTitle = "";
        public string AlbumPerformer = "";
        public readonly Dictionary<int, string> TrackTitles = new Dictionary<int, string>();
        public readonly Dictionary<int, string> TrackPerformers = new Dictionary<int, string>();
        public int PacksTotal;
        public int PacksBadCrc;

        /// <summary>Anything actually usable came back (an album title or at least one track title).</summary>
        public bool HasText
        {
            get { return AlbumTitle.Length > 0 || TrackTitles.Count > 0; }
        }
    }

    /// <summary>
    /// Parser for the raw READ TOC/PMA/ATIP format 0x05 (CD-TEXT) response: a 4-byte header
    /// followed by 18-byte PACKs. Each pack: type (0x80 TITLE, 0x81 PERFORMER, ...), track number,
    /// sequence, block/position byte (bit7 = double-byte charset), 12 text bytes, CRC-16 (X.25:
    /// CCITT polynomial, result complemented). Strings are NUL-terminated and run ACROSS packs;
    /// after a NUL the next string (for the next track) starts immediately. Only block 0 (the
    /// first language) is decoded, as ISO-8859-1 - the overwhelmingly common encoding; packs
    /// flagged double-byte (MS-JIS) are skipped rather than mis-decoded.
    /// </summary>
    public static class CDTextParser
    {
        private const int PackSize = 18;
        private const byte TypeTitle = 0x80;
        private const byte TypePerformer = 0x81;

        public static CDTextInfo Parse(byte[] raw)
        {
            var info = new CDTextInfo();
            if (raw == null || raw.Length < 4 + PackSize) return info;

            // per pack type: the text stream and the track number the current string belongs to
            var streams = new Dictionary<byte, PackStream>();

            for (int off = 4; off + PackSize <= raw.Length; off += PackSize)
            {
                info.PacksTotal++;
                byte type = raw[off];
                if (type != TypeTitle && type != TypePerformer) continue;   // other pack types not needed

                int block = (raw[off + 3] >> 4) & 0x07;
                bool doubleByte = (raw[off + 3] & 0x80) != 0;
                if (block != 0 || doubleByte) continue;   // first language, single-byte only

                if (!CrcOk(raw, off))
                {
                    info.PacksBadCrc++;
                    continue;   // a corrupt pack would garble the string it belongs to - drop it
                }

                PackStream ps;
                if (!streams.TryGetValue(type, out ps))
                {
                    ps = new PackStream { Track = raw[off + 1] & 0x7F };
                    streams[type] = ps;
                }

                for (int i = 0; i < 12; i++)
                {
                    byte b = raw[off + 4 + i];
                    if (b == 0)
                    {
                        Commit(info, type, ps);
                        ps.Track++;          // the next string belongs to the next track
                        ps.Text.Length = 0;
                    }
                    else
                        ps.Text.Append(Latin1(b));
                }
            }

            // a final string without a terminating NUL still counts
            foreach (var kv in streams)
                if (kv.Value.Text.Length > 0) Commit(info, kv.Key, kv.Value);

            return info;
        }

        private sealed class PackStream
        {
            public int Track;
            public readonly StringBuilder Text = new StringBuilder();
        }

        private static void Commit(CDTextInfo info, byte type, PackStream ps)
        {
            string s = ps.Text.ToString().Trim();
            if (s.Length == 0) return;
            // "TAB" ditto marks mean "same as previous track" per the spec
            if (s == "\t")
            {
                var dict = type == TypeTitle ? info.TrackTitles : info.TrackPerformers;
                string prev;
                if (ps.Track > 1 && dict.TryGetValue(ps.Track - 1, out prev)) s = prev;
                else return;
            }
            if (ps.Track == 0)
            {
                if (type == TypeTitle) info.AlbumTitle = s;
                else info.AlbumPerformer = s;
            }
            else
            {
                var dict = type == TypeTitle ? info.TrackTitles : info.TrackPerformers;
                if (!dict.ContainsKey(ps.Track)) dict[ps.Track] = s;
            }
        }

        private static char Latin1(byte b)
        {
            // ISO-8859-1 maps bytes to the same Unicode code points
            return (char)b;
        }

        /// <summary>CD-Text pack CRC: CRC-16/CCITT (poly 0x1021, init 0) over the first 16 bytes,
        /// stored complemented in the last two.</summary>
        private static bool CrcOk(byte[] raw, int off)
        {
            ushort crc = 0;
            for (int i = 0; i < 16; i++)
            {
                crc ^= (ushort)(raw[off + i] << 8);
                for (int bit = 0; bit < 8; bit++)
                    crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
            }
            ushort stored = (ushort)((raw[off + 16] << 8) | raw[off + 17]);
            return (ushort)~crc == stored;
        }
    }
}
