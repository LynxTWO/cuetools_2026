using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CUETools.Wpf.Services
{
    /// <summary>Shared JSON persistence that saves gzip-compressed and loads either format. Detects a
    /// gzip file by its magic bytes (0x1f 0x8b), so an existing plain-JSON file still opens - the next
    /// Save rewrites it compressed. Pure I/O, no UI - unit-tested with no drive.</summary>
    public static class GzJson
    {
        private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { WriteIndented = true };

        public static T Load<T>(string path)
        {
            try
            {
                if (!File.Exists(path)) return default;
                byte[] raw = File.ReadAllBytes(path);
                if (raw.Length == 0) return default;
                string json;
                if (raw.Length >= 2 && raw[0] == 0x1f && raw[1] == 0x8b)
                {
                    using var ms = new MemoryStream(raw);
                    using var gz = new GZipStream(ms, CompressionMode.Decompress);
                    using var sr = new StreamReader(gz, Encoding.UTF8);
                    json = sr.ReadToEnd();
                }
                else
                {
                    json = Encoding.UTF8.GetString(raw);
                }
                return JsonSerializer.Deserialize<T>(json);
            }
            catch { return default; }
        }

        public static void Save<T>(string path, T value)
        {
            // Write to a sibling temp file, then atomically replace the target. A crash mid-write
            // hits the temp file, not the store - so the store (one file for every disc's history)
            // never ends up half-written.
            string tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string json = JsonSerializer.Serialize(value, Opts);
                using (var fs = File.Create(tmp))
                using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
                using (var sw = new StreamWriter(gz, new UTF8Encoding(false)))
                    sw.Write(json);
                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort cleanup */ }
                /* best-effort persistence */
            }
        }
    }
}
