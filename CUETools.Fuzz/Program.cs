using System;
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

    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--gui") >= 0)
            return GuiFuzzer.Run(args);

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

        Console.WriteLine(new string('-', 60));
        Console.WriteLine(_failures == 0 ? "ALL FUZZERS PASSED" : $"FAILURES: {_failures}");
        return _failures == 0 ? 0 : 1;
    }

    private static void Report(string name, bool ok, string detail)
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name,-16} {detail}");
        if (!ok) _failures++;
    }

    // ---- Fuzzer 1: SCSI response parsers (Bwg.Scsi) ----
    // These parse raw bytes returned by the drive (INQUIRY, GET CONFIGURATION, GET EVENT STATUS,
    // GET PERFORMANCE) - the real untrusted-input surface behind DriveInspector. Get8() bounds-
    // checks and throws a catchable Exception on over-read, so a malformed reply must never crash
    // the process, only throw. We over-allocate the native buffer so the one unchecked path
    // (EventStatusNotification's raw ReadByte loop) cannot AccessViolation the fuzzer; reaching the
    // end without the process dying is the pass condition.
    private static void FuzzScsiParsers(int seed, int iters)
    {
        var rnd = new Random(seed);
        const int Alloc = 70000;                 // > max EventData length (65535) + header
        IntPtr buf = Marshal.AllocHGlobal(Alloc);
        var raw = new byte[Alloc];
        int parsed = 0, rejected = 0;
        try
        {
            for (int i = 0; i < iters; i++)
            {
                rnd.NextBytes(raw);
                Marshal.Copy(raw, 0, buf, Alloc);
                int size = rnd.Next(0, 2049);
                try
                {
                    switch (rnd.Next(6))
                    {
                        case 0: { var r = new InquiryResult(buf, size); if (r.Valid) { _ = r.VendorIdentification; _ = r.ProductIdentification; _ = r.FirmwareVersion; } break; }
                        case 1: { int off = rnd.Next(0, Math.Max(1, size)); var f = new Feature(buf, size, ref off); _ = f.Name; _ = f.Current; _ = f.Data; break; }
                        case 2: { var fl = new FeatureList(buf, size); foreach (var f in fl.Features) { _ = f.Code; _ = f.Current; } break; }
                        case 3: { var e = new EventStatusNotification(buf, size); _ = e.EventData; _ = e.EventAvailable; break; }
                        case 4: { var sd = new SpeedDescriptor(buf, rnd.Next(0, Math.Max(1, size)), size); _ = sd.ReadSpeed; _ = sd.WriteSpeed; break; }
                        case 5: { var sl = new SpeedDescriptorList(buf, size); _ = sl.Count; foreach (var d in sl) _ = d.ReadSpeed; break; }
                    }
                    parsed++;
                }
                catch (Exception)
                {
                    rejected++;   // catchable rejection of malformed input is the CORRECT behaviour
                }
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        // completing without a process crash is the pass; report the split for insight
        Report("SCSI parsers", true, $"{parsed} parsed + {rejected} rejected, 0 uncatchable crashes");
    }

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
