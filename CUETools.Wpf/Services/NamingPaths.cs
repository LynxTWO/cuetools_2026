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
    }
}
