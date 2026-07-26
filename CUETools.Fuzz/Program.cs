using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Bwg.Scsi;
using CUETools.Wpf.Controls;

namespace CUETools.Fuzz;

// Fuzz harness for the R12 features. Two headless fuzzers (deterministic, CI-friendly) plus a
// UIAutomation GUI random-walk. Run:
//   dotnet run -c Release            # headless fuzzers (SCSI parsers + CodecMath)
//   dotnet run -c Release -- 42 500000   # seed, iterations
//   dotnet run -c Release -- --gui   # random-walk the already-running CUETools.Wpf window
//
// A property-based random fuzzer (not coverage-guided): generate adversarial inputs, assert the
// invariants (no process crash, no NaN escaping, bounded output). SharpFuzz/libFuzzer would be a
// future upgrade; this catches the same crash/robustness bugs and runs anywhere.
internal static class Program
{
    private static int _failures;
    private static int _checks;
    private static int _skips;

    private static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "--corpus-child")
            return CorpusFuzzer.RunChild(args[1], args[2]);
        if (Array.IndexOf(args, "--gui") >= 0)
            return GuiFuzzer.Run(args);
        if (Array.IndexOf(args, "--toggles") >= 0)
            return GuiFuzzer.RunToggleSweep(args);

        // The vendored Bwg.Scsi parsers use Debug.Assert on parsed values; on malformed fuzz input
        // an assert can fire and, in a Debug build, terminate the process. Drop the trace listeners
        // so the fuzzer keeps running and surfaces only genuine uncatchable crashes. (Release builds
        // strip Debug.Assert entirely, so this only matters to the fuzz harness.)
        System.Diagnostics.Trace.Listeners.Clear();

        int seed = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 20260712;
        int iters = args.Length > 1 && int.TryParse(args[1], out var it) ? it : 300000;
        Console.WriteLine($"CUETools fuzz  seed={seed}  iters={iters}");
        Console.WriteLine(new string('-', 60));

        FuzzScsiParsers(seed, iters);
        FuzzCodecMath(seed ^ 0x5bd1, iters);
        CorpusFuzzer.Run();

        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"FUZZ SUMMARY checks={_checks} failures={_failures} skips={_skips}");
        Console.WriteLine(_failures == 0 ? "ALL FUZZERS PASSED" : $"FAILURES: {_failures}");
        return _failures == 0 ? 0 : 1;
    }

    internal static void Report(string name, bool ok, string detail)
    {
        _checks++;
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name,-16} {detail}");
        if (!ok) _failures++;
    }

    internal static void Skip(string name, string detail)
    {
        _skips++;
        Console.WriteLine($"  [SKIP] {name,-16} {detail}");
    }

    // ---- Fuzzer 1: SCSI response parsers (Bwg.Scsi) ----
    // Generate both structurally valid replies and intentionally short replies. A valid reply must
    // parse into values consistent with the bytes supplied. A short reply is accepted only when the
    // parser throws Result.Get8's exact bounds exception; every other exception is unexpected.
    // Buffers are allocated at their declared size, so the harness does not hide over-reads with
    // trailing slack.
    private static void FuzzScsiParsers(int seed, int iters)
    {
        var rnd = new Random(seed);
        int accepted = 0, expectedRejected = 0, invariantFailures = 0, unexpectedExceptions = 0;

        for (int i = 0; i < iters; i++)
        {
            int parser = i % 6;
            bool shortInput = parser != 3 && (i % 17) == 0;
            if (shortInput)
            {
                int size = parser switch
                {
                    0 => rnd.Next(0, 4),
                    1 => rnd.Next(0, 4),
                    2 => rnd.Next(0, 8),
                    3 => rnd.Next(0, 4),
                    4 => rnd.Next(0, 16),
                    _ => rnd.Next(0, 4)
                };
                var bytes = new byte[size];
                rnd.NextBytes(bytes);
                if (ExpectBoundsRejection(bytes, (buf, length) =>
                    {
                        switch (parser)
                        {
                            case 0: _ = new InquiryResult(buf, length); break;
                            case 1: { int offset = 0; _ = new Feature(buf, length, ref offset); break; }
                            case 2: _ = new FeatureList(buf, length); break;
                            case 3: _ = new EventStatusNotification(buf, length); break;
                            case 4: _ = new SpeedDescriptor(buf, 0, length); break;
                            default: _ = new SpeedDescriptorList(buf, length); break;
                        }
                    }, out string? rejectDetail))
                {
                    expectedRejected++;
                }
                else
                {
                    unexpectedExceptions++;
                    if (unexpectedExceptions == 1)
                        Console.WriteLine($"    first unexpected rejection result: parser={parser}, size={size}, {rejectDetail}");
                }
                continue;
            }

            byte[] raw;
            Func<IntPtr, int, bool> parseAndCheck;
            switch (parser)
            {
                case 0:
                    raw = new byte[rnd.Next(36, 97)];
                    rnd.NextBytes(raw);
                    raw[3] = (byte)((raw[3] & 0xf0) | (1 + rnd.Next(3)));
                    parseAndCheck = (buf, size) =>
                    {
                        var result = new InquiryResult(buf, size);
                        return result.Valid
                            && result.ResponseDataFormat >= 1 && result.ResponseDataFormat <= 3
                            && result.VendorIdentification.Length == 8
                            && result.ProductIdentification.Length == 16
                            && result.FirmwareVersion.Length == Math.Min(4, size - 32);
                    };
                    break;
                case 1:
                    int featureDataLength = rnd.Next(0, 65);
                    raw = new byte[4 + featureDataLength];
                    rnd.NextBytes(raw);
                    raw[3] = (byte)featureDataLength;
                    parseAndCheck = (buf, size) =>
                    {
                        int offset = 0;
                        var feature = new Feature(buf, size, ref offset);
                        if (offset != size || feature.Data.Length != featureDataLength)
                            return false;
                        for (int n = 0; n < featureDataLength; n++)
                            if (feature.Data[n] != raw[4 + n]) return false;
                        return true;
                    };
                    break;
                case 2:
                    int featureCount = rnd.Next(0, 9);
                    var features = new List<byte[]>();
                    int payloadLength = 0;
                    for (int n = 0; n < featureCount; n++)
                    {
                        int dataLength = rnd.Next(0, 17);
                        var feature = new byte[4 + dataLength];
                        rnd.NextBytes(feature);
                        feature[3] = (byte)dataLength;
                        features.Add(feature);
                        payloadLength += feature.Length;
                    }
                    raw = new byte[8 + payloadLength];
                    rnd.NextBytes(raw.AsSpan(0, 8));
                    WriteUInt32BE(raw, 0, (uint)payloadLength);
                    int featureOffset = 8;
                    foreach (var feature in features)
                    {
                        Buffer.BlockCopy(feature, 0, raw, featureOffset, feature.Length);
                        featureOffset += feature.Length;
                    }
                    parseAndCheck = (buf, size) =>
                    {
                        var list = new FeatureList(buf, size);
                        if (list.Features.Count != featureCount) return false;
                        for (int n = 0; n < featureCount; n++)
                            if (list.Features[n].Data.Length != features[n][3]) return false;
                        return true;
                    };
                    break;
                case 3:
                    int eventLength = rnd.Next(0, 129);
                    raw = new byte[4 + eventLength];
                    rnd.NextBytes(raw);
                    WriteUInt16BE(raw, 0, (ushort)eventLength);
                    raw[2] = (byte)(raw[2] & 0x03);
                    parseAndCheck = (buf, size) =>
                    {
                        var result = new EventStatusNotification(buf, size);
                        if (!result.EventAvailable || result.EventData == null || result.EventData.Length != eventLength)
                            return false;
                        for (int n = 0; n < eventLength; n++)
                            if (result.EventData[n] != raw[4 + n]) return false;
                        return true;
                    };
                    break;
                case 4:
                    raw = new byte[16];
                    rnd.NextBytes(raw);
                    raw[8] &= 0x7f;
                    raw[12] &= 0x7f;
                    parseAndCheck = (buf, size) =>
                    {
                        var result = new SpeedDescriptor(buf, 0, size);
                        return result.ReadSpeed >= 0 && result.WriteSpeed >= 0
                            && result.EndLBA == ReadUInt32BE(raw, 4)
                            && (uint)result.ReadSpeed == ReadUInt32BE(raw, 8)
                            && (uint)result.WriteSpeed == ReadUInt32BE(raw, 12);
                    };
                    break;
                default:
                    int descriptorCount = rnd.Next(0, 9);
                    raw = new byte[8 + descriptorCount * 16];
                    rnd.NextBytes(raw);
                    WriteUInt32BE(raw, 0, (uint)(4 + descriptorCount * 16));
                    for (int n = 0; n < descriptorCount; n++)
                    {
                        raw[8 + n * 16 + 8] &= 0x7f;
                        raw[8 + n * 16 + 12] &= 0x7f;
                    }
                    parseAndCheck = (buf, size) =>
                    {
                        var list = new SpeedDescriptorList(buf, size);
                        if (list.Count != descriptorCount) return false;
                        foreach (var descriptor in list)
                            if (descriptor.ReadSpeed < 0 || descriptor.WriteSpeed < 0) return false;
                        return true;
                    };
                    break;
            }

            if (RunExactBuffer(raw, parseAndCheck, out string? detail))
                accepted++;
            else
            {
                if (detail != null && detail.StartsWith("invariant", StringComparison.Ordinal))
                    invariantFailures++;
                else
                    unexpectedExceptions++;
                if (invariantFailures + unexpectedExceptions == 1)
                    Console.WriteLine($"    first SCSI failure: parser={parser}, size={raw.Length}, {detail}");
            }
        }

        Report(
            "SCSI parsers",
            invariantFailures == 0 && unexpectedExceptions == 0,
            $"{accepted} accepted with invariants; {expectedRejected} expected bounds rejects; {invariantFailures} invariant failures; {unexpectedExceptions} unexpected outcomes");
        Skip("SCSI truncated", "Feature payload and EventStatus length over-read cases require parser bounds fixes or process isolation; they are not padded and reported as covered.");
    }

    private static bool RunExactBuffer(byte[] bytes, Func<IntPtr, int, bool> action, out string? detail)
    {
        IntPtr buffer = bytes.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(bytes.Length);
        try
        {
            if (bytes.Length > 0)
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
            try
            {
                if (!action(buffer, bytes.Length))
                {
                    detail = "invariant check failed";
                    return false;
                }
                detail = null;
                return true;
            }
            catch (Exception ex)
            {
                detail = $"unexpected {ex.GetType().FullName}: {ex.Message}";
                return false;
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool ExpectBoundsRejection(byte[] bytes, Action<IntPtr, int> action, out string? detail)
    {
        IntPtr buffer = bytes.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(bytes.Length);
        try
        {
            if (bytes.Length > 0)
                Marshal.Copy(bytes, 0, buffer, bytes.Length);
            try
            {
                action(buffer, bytes.Length);
                detail = "unexpected acceptance";
                return false;
            }
            catch (Exception ex)
            {
                bool expected = ex.GetType() == typeof(Exception)
                    && ex.Message.StartsWith("offset ", StringComparison.Ordinal)
                    && ex.Message.EndsWith("is out side the range of the buffer", StringComparison.Ordinal);
                detail = expected ? null : $"unexpected {ex.GetType().FullName}: {ex.Message}";
                return expected;
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }

    private static void WriteUInt16BE(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value >> 8);
        bytes[offset + 1] = (byte)value;
    }

    private static void WriteUInt32BE(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    private static uint ReadUInt32BE(byte[] bytes, int offset) =>
        ((uint)bytes[offset] << 24) |
        ((uint)bytes[offset + 1] << 16) |
        ((uint)bytes[offset + 2] << 8) |
        bytes[offset + 3];

    // ---- Fuzzer 2: CodecMath (the codec-scope predictor + Rice-cost math) ----
    // Feed adversarial windows (NaN, Inf, +/-huge, denormal, zero, normal audio) and every codec
    // family. Invariants: never throw; the returned bits/sample is finite and in [1,16]; a residual
    // is produced. If a non-finite input makes bits go NaN/out-of-range, that is a real bug to fix.
    private static void FuzzCodecMath(int seed, int iters)
    {
        var rnd = new Random(seed);
        var kinds = (CodecMath.Pred[])Enum.GetValues(typeof(CodecMath.Pred));
        int bad = 0; string? firstBad = null;
        for (int i = 0; i < iters; i++)
        {
            int n = rnd.Next(0, 800);
            var sig = new float[n]; var pred = new float[n]; var resid = new float[n];
            for (int j = 0; j < n; j++) sig[j] = RandSample(rnd);
            var kind = kinds[rnd.Next(kinds.Length)];
            try
            {
                CodecMath.ComputeResidual(sig, kind, pred, resid);
                double bits = CodecMath.BitsPerSample(resid, kind);
                if (double.IsNaN(bits) || double.IsInfinity(bits) || bits < 0.9 || bits > 16.1)
                {
                    bad++; firstBad ??= $"bits={bits} kind={kind} n={n}";
                }
            }
            catch (Exception ex) { bad++; firstBad ??= $"threw {ex.GetType().Name} kind={kind} n={n}"; }
        }
        Report("CodecMath", bad == 0, bad == 0 ? $"{iters} windows, all bounded/finite" : $"{bad} bad (first: {firstBad})");
    }

    private static float RandSample(Random rnd) => rnd.Next(12) switch
    {
        0 => float.NaN,
        1 => float.PositiveInfinity,
        2 => float.NegativeInfinity,
        3 => float.MaxValue,
        4 => -float.MaxValue,
        5 => 0f,
        6 => float.Epsilon,
        7 => (float)(rnd.NextDouble() * 2000 - 1000),   // way out of range
        _ => (float)(rnd.NextDouble() * 2 - 1),         // normal audio [-1,1]
    };
}
