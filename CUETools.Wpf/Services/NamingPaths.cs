using System;
using System.Collections.Generic;

namespace CUETools.Wpf.Services
{
    /// <summary>Splits NamingEngine relative track paths into the shared album directory (where the
    /// .cue/.log/cover go) and each track's remainder under it (the per-track name the engine writes,
    /// which keeps a "Disc N/" segment for multi-disc sets). Pure - no I/O.</summary>
    public static class NamingPaths
    {
        public static (string commonDir, string[] remainders) Split(IReadOnlyList<string> relPaths)
        {
            if (relPaths == null || relPaths.Count == 0) return ("", Array.Empty<string>());

            // directory segments of each path (everything before the last '/')
            var dirs = new List<string[]>();
            var full = new List<string[]>();
            foreach (var p in relPaths)
            {
                var segs = (p ?? "").Split('/');
                full.Add(segs);
                var d = new string[Math.Max(0, segs.Length - 1)];
                Array.Copy(segs, d, d.Length);
                dirs.Add(d);
            }

            // longest common leading run of directory segments
            int common = int.MaxValue;
            foreach (var d in dirs) common = Math.Min(common, d.Length);
            int shared = 0;
            for (int i = 0; i < common; i++)
            {
                string seg = dirs[0][i];
                bool all = true;
                foreach (var d in dirs) if (d[i] != seg) { all = false; break; }
                if (!all) break;
                shared++;
            }

            string commonDir = shared > 0 ? string.Join("/", dirs[0], 0, shared) : "";
            var remainders = new string[full.Count];
            for (int i = 0; i < full.Count; i++)
                remainders[i] = string.Join("/", full[i], shared, full[i].Length - shared);
            return (commonDir, remainders);
        }

        // splits an entry into its directory part (including trailing '/', or "" if none) and its
        // filename part (the text after the last '/')
        private static (string dir, string name) SplitFilename(string entry)
        {
            entry ??= "";
            int slash = entry.LastIndexOf('/');
            return slash >= 0 ? (entry.Substring(0, slash + 1), entry.Substring(slash + 1)) : ("", entry);
        }

        /// <summary>Guarantees every remainder has a non-empty filename part and that no two remainders
        /// are equal (case-insensitive, since Windows paths are case-insensitive). Deterministic and
        /// 1-based on index: an empty/whitespace filename part becomes the 2-digit index ("01"), and a
        /// duplicate (of an earlier, already-processed entry) gets " (NN)" appended to its filename part,
        /// NN being its own 2-digit index. Directory parts are never touched. Pure - no I/O.</summary>
        public static string[] EnsureUniqueTrackNames(IReadOnlyList<string> remainders)
        {
            if (remainders == null || remainders.Count == 0) return Array.Empty<string>();

            var result = new string[remainders.Count];
            for (int i = 0; i < remainders.Count; i++)
            {
                var (dir, name) = SplitFilename(remainders[i]);
                if (string.IsNullOrWhiteSpace(name))
                    name = (i + 1).ToString("00");
                result[i] = dir + name;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < result.Length; i++)
            {
                if (seen.Add(result[i])) continue;

                var (dir, name) = SplitFilename(result[i]);
                string disambiguated = dir + name + " (" + (i + 1).ToString("00") + ")";
                result[i] = disambiguated;
                seen.Add(disambiguated);
            }
            return result;
        }

        /// <summary>Caps the total assembled path length (outDirLength + 1 separator + the remainder's
        /// own length) at <paramref name="maxTotal"/>, truncating only the FILENAME part of an
        /// over-budget entry (never the directory part - there is no extension in these names yet) while
        /// keeping at least 8 characters of filename. Entries already within budget are returned
        /// unchanged. Pure - no I/O. Callers should run this BEFORE EnsureUniqueTrackNames so truncation
        /// collisions can still be disambiguated.</summary>
        public static string[] CapPathLength(IReadOnlyList<string> remainders, int outDirLength, int maxTotal = 250)
        {
            if (remainders == null || remainders.Count == 0) return Array.Empty<string>();

            var result = new string[remainders.Count];
            for (int i = 0; i < remainders.Count; i++)
            {
                string entry = remainders[i] ?? "";
                int overBy = outDirLength + 1 + entry.Length - maxTotal;
                if (overBy <= 0) { result[i] = entry; continue; }

                var (dir, name) = SplitFilename(entry);
                int minNameLen = Math.Min(8, name.Length);
                int targetNameLen = Math.Max(name.Length - overBy, minNameLen);
                if (targetNameLen < name.Length)
                    name = name.Substring(0, targetNameLen);
                result[i] = dir + name;
            }
            return result;
        }
    }
}
