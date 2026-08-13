using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace CUETools.Wpf.Services
{
    public enum GzJsonLoadStatus
    {
        Missing,
        Loaded,
        Failed,
    }

    /// <summary>
    /// Distinguishes a store that has never been created from one that exists but cannot be read.
    /// The compatibility <see cref="GzJson.Load{T}"/> API intentionally maps both cases to default;
    /// persistence code must use this result so corrupt evidence is never mistaken for an empty
    /// database and overwritten on the next save.
    /// </summary>
    public readonly struct GzJsonLoadResult<T>
    {
        internal GzJsonLoadResult(
            GzJsonLoadStatus status,
            T? value,
            System.Exception? error)
        {
            Status = status;
            Value = value;
            Error = error;
        }

        public GzJsonLoadStatus Status { get; }
        public T? Value { get; }
        public System.Exception? Error { get; }
    }

    /// <summary>Shared JSON persistence that saves gzip-compressed and loads either format. Detects a
    /// gzip file by its magic bytes (0x1f 0x8b), so an existing plain-JSON file still opens - the next
    /// Save rewrites it compressed. Pure I/O, no UI - unit-tested with no drive.</summary>
    public static class GzJson
    {
        private const long MaximumStoredBytes = 64L * 1024 * 1024;
        private const long MaximumDecodedBytes = 128L * 1024 * 1024;
        // Serialization resolves through the source-generated StoreJsonContext
        // first (required under trimmed/AOT heads, where reflection-based
        // System.Text.Json is disabled - including JIT builds of a PublishAot
        // project). Hosts with dynamic code keep a reflection fallback for any
        // type outside the context's closed set.
        private static readonly JsonSerializerOptions Opts = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver resolver =
                System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported
                    ? System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(
                        StoreJsonContext.Default,
                        new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver())
                    : StoreJsonContext.Default;
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = resolver,
            };
        }

        public static T? Load<T>(string path)
        {
            GzJsonLoadResult<T> result = TryLoad<T>(path);
            return result.Status == GzJsonLoadStatus.Loaded ? result.Value : default;
        }

        public static GzJsonLoadResult<T> TryLoad<T>(string path)
            => TryLoadCore<T>(
                path,
                MaximumStoredBytes,
                MaximumDecodedBytes);

        public static void Save<T>(string path, T value)
        {
            string fullPath = Path.GetFullPath(path);
            using StoreFileLock storeLock = StoreFileLock.Acquire(fullPath);
            SaveCore(fullPath, value);
        }

        /// <summary>
        /// Serializes a complete read-modify-write operation for one store. Callers that append
        /// or merge records must use this instead of a separate <see cref="TryLoad{T}"/> and
        /// <see cref="Save{T}"/>, which can lose another process's update between those calls.
        /// </summary>
        internal static TResult Update<T, TResult>(
            string path,
            Func<GzJsonLoadResult<T>, (T Value, TResult Result)> update)
        {
            if (update == null)
                throw new ArgumentNullException(nameof(update));

            string fullPath = Path.GetFullPath(path);
            using StoreFileLock storeLock = StoreFileLock.Acquire(fullPath);
            GzJsonLoadResult<T> loaded = TryLoadCore<T>(
                fullPath,
                MaximumStoredBytes,
                MaximumDecodedBytes);
            (T Value, TResult Result) next = update(loaded);
            SaveCore(fullPath, next.Value);
            return next.Result;
        }

        internal static GzJsonLoadResult<T> TryLoadWithLimits<T>(
            string path,
            long maximumStoredBytes,
            long maximumDecodedBytes)
            => TryLoadCore<T>(
                path,
                maximumStoredBytes,
                maximumDecodedBytes);

        internal static string GetStoreLockPath(string path)
            => Path.GetFullPath(path) + ".lock";

        private static GzJsonLoadResult<T> TryLoadCore<T>(
            string path,
            long maximumStoredBytes,
            long maximumDecodedBytes)
        {
            try
            {
                if (maximumStoredBytes <= 0)
                    throw new ArgumentOutOfRangeException(nameof(maximumStoredBytes));
                if (maximumDecodedBytes <= 0)
                    throw new ArgumentOutOfRangeException(nameof(maximumDecodedBytes));
                if (!File.Exists(path))
                    return new GzJsonLoadResult<T>(
                        GzJsonLoadStatus.Missing, default, null);

                using var file = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
                if (file.Length == 0)
                    throw new InvalidDataException(
                        "The persistence file is empty.");
                if (file.Length > maximumStoredBytes)
                    throw new InvalidDataException(
                        "The persistence file exceeds the stored-size limit.");

                int first = file.ReadByte();
                int second = file.ReadByte();
                file.Position = 0;
                Stream payload = file;
                GZipStream? gzip = null;
                try
                {
                    if (first == 0x1f && second == 0x8b)
                    {
                        gzip = new GZipStream(
                            file,
                            CompressionMode.Decompress,
                            leaveOpen: true);
                        payload = gzip;
                    }

                    using var bounded = new BoundedReadStream(
                        payload,
                        maximumDecodedBytes);
                    T? value = JsonSerializer.Deserialize<T>(bounded, Opts);
                    if (value == null)
                        throw new InvalidDataException(
                            "The persistence file contains a null root value.");
                    return new GzJsonLoadResult<T>(
                        GzJsonLoadStatus.Loaded, value, null);
                }
                finally
                {
                    if (gzip != null)
                        gzip.Dispose();
                }
            }
            catch (System.Exception ex)
            {
                return new GzJsonLoadResult<T>(
                    GzJsonLoadStatus.Failed, default, ex);
            }
        }

        private static void SaveCore<T>(string path, T value)
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                throw new IOException("Persistence path has no parent directory.");

            string tmp = Path.Combine(
                directory,
                Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Directory.CreateDirectory(directory);
                string json = JsonSerializer.Serialize(value, Opts);

                using (var fs = new FileStream(
                    tmp,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None))
                {
                    using (var gz = new GZipStream(
                        fs,
                        CompressionLevel.Optimal,
                        leaveOpen: true))
                    using (var sw = new StreamWriter(
                        gz,
                        new UTF8Encoding(false),
                        bufferSize: 1024,
                        leaveOpen: false))
                    {
                        sw.Write(json);
                    }

                    // The gzip footer is complete when the wrapper closes. Flush file data before
                    // publication. The rename below gives atomic visibility; without flushing the
                    // parent directory this deliberately does not claim power-loss durability.
                    fs.Flush(flushToDisk: true);
                }

                File.Move(tmp, path, overwrite: true);
            }
            catch
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort cleanup */ }
                // Callers decide whether their store is best-effort. Swallowing here made a failed
                // verify-history or calibration write indistinguishable from a durable success.
                throw;
            }
        }

        private sealed class StoreFileLock : IDisposable
        {
            private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
            private readonly FileStream _stream;

            private StoreFileLock(FileStream stream)
            {
                _stream = stream;
            }

            internal static StoreFileLock Acquire(string path)
            {
                string? directory = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(directory))
                    throw new IOException(
                        "Persistence path has no parent directory.");
                Directory.CreateDirectory(directory);

                string lockPath = GetStoreLockPath(path);
                DateTime deadline = DateTime.UtcNow + WaitTimeout;
                IOException? lastSharingFailure = null;
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        return new StoreFileLock(
                            new FileStream(
                                lockPath,
                                FileMode.OpenOrCreate,
                                FileAccess.ReadWrite,
                                FileShare.None));
                    }
                    catch (IOException ex) when (IsSharingViolation(ex))
                    {
                        lastSharingFailure = ex;
                        Thread.Sleep(25);
                    }
                }

                throw new TimeoutException(
                    "Timed out waiting for another process to finish updating the store.",
                    lastSharingFailure);
            }

            private static bool IsSharingViolation(IOException ex)
            {
                int code = ex.HResult & 0xffff;
                return code == 32 || code == 33;
            }

            public void Dispose() => _stream.Dispose();
        }

        private sealed class BoundedReadStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _maximumBytes;
            private long _bytesRead;

            internal BoundedReadStream(Stream inner, long maximumBytes)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _maximumBytes = maximumBytes;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => _bytesRead;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = _inner.Read(buffer, offset, count);
                Count(read);
                return read;
            }

            public override int Read(Span<byte> buffer)
            {
                int read = _inner.Read(buffer);
                Count(read);
                return read;
            }

            public override int ReadByte()
            {
                int value = _inner.ReadByte();
                if (value >= 0)
                    Count(1);
                return value;
            }

            private void Count(int read)
            {
                if (read < 0 || _bytesRead > _maximumBytes - read)
                    throw new InvalidDataException(
                        "The persistence file exceeds the decoded-size limit.");
                _bytesRead += read;
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) =>
                throw new NotSupportedException();
            public override void SetLength(long value) =>
                throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();
        }
    }
}
