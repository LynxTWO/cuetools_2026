# CUETools.Fuzz

Deterministic property/corpus harness for untrusted parser and decoder inputs. It is a standalone
tool (not in `CUETools.sln`) and runs directly with `dotnet run`.

## What it fuzzes

- **SCSI response parsers** (`Bwg.Scsi`: `InquiryResult`, `Feature`, `FeatureList`,
  `EventStatusNotification`, `SpeedDescriptor`, `SpeedDescriptorList`). These parse raw bytes the
  drive returns. The harness generates exact-size, structurally valid replies and checks decoded
  values against the supplied bytes. Deliberately short replies pass only when they produce the
  parser's exact bounds rejection; unexpected acceptance and all other exception shapes fail.
  Unsafe truncated `EventStatusNotification` cases remain an explicit skip because that vendored
  parser can read outside the managed buffer.
- **`CodecMath`** (the codec-scope predictor + Rice-cost math). Feeds adversarial sample windows
  (NaN, +/-Inf, +/-huge, denormal, zero, normal audio) through every codec family. Invariant: never
  throws, and the returned bits/sample is finite and in [1,16].
- **Checked-in parser/decoder corpora**:
  - CUE: one valid sheet with TOC invariants and two exact malformed-file rejections.
  - Managed FLAC and ALAC: complete valid decodes with PCM/length/termination invariants, plus fixed
    corruptions in isolated child processes with a five-second bound. The exact 16-byte FLAC
    metadata prefix that previously did not terminate is a mandatory regression.
  - ZIP: deterministic entry/content/seek checks and an exact corrupt-archive rejection.
  - TagLib: valid MP3, FLAC, and M4A metadata invariants plus an unsupported-input rejection.
- **GUI random-walk** (`--gui`): attaches to the running `CUETools.Wpf` window and hammers it with
  SAFE actions only - random page navigation, switch toggling, window resizes - checking the process
  stays alive. It does NOT invoke Rip / Verify / Eject / Convert / Detect / folder-picker buttons
  (hardware, filesystem, or blocking-dialog side effects). MOUSE-FREE: it drives controls through
  UIAutomation patterns and never moves the physical cursor or steals focus, so you can keep using
  the machine while it runs. Stresses the DynamicResource theme swap, page switching, the GPU-drawn
  custom controls, and layout under random sizes.
- **Toggle-combination sweep** (`--toggles`): navigates to Settings and drives every switch through
  all 2^N combinations (or a 4096 random sample when N is large), checking health after each.
  This is a bounded UI-health exercise, not proof that every downstream feature combination is
  semantically correct.

## Run

```sh
dotnet run -c Release                  # headless property + corpus lanes, default seed + 300k iters
dotnet run -c Release -- 42 500000     # seed, iterations
dotnet run -c Release -- --gui         # random-walk the already-running app window
dotnet run -c Release -- --gui 42 300  # seed, steps
dotnet run -c Release -- --toggles     # sweep every switch combination (app running)
```

Exit code is non-zero on any failed invariant, unexpected exception/acceptance, child-process
timeout, or missing required corpus file. The summary reports checks, failures, and explicit skips;
property-lane failures print the seed to reproduce.

## Findings so far

- `SpeedDescriptor` reads speeds via `Result.Get32Int`, whose `Debug.Assert(b < Int32.MaxValue)`
  fires on a high-bit value - terminating the process in Debug, silently wrapping to a negative
  speed in Release. Fixed in the app by clamping absurd/negative speeds in
  `DriveInspector.ReadSpeeds`; the harness clears trace listeners so the vendored assert can't stop
  the run.
- A 16-byte FLAC prefix declared a 34-byte STREAMINFO block and made the managed metadata decoder
  loop at EOF. The decoder now rejects incomplete metadata, and the exact input is retained as a
  bounded regression. See `Findings/managed-flac-16-byte-prefix.md`.

## Future

A coverage-guided fuzzer (for example SharpFuzz over libFuzzer/AFL) would explore beyond these
generated structures and fixed mutations. The current harness is a deterministic CI gate, not a
claim of exhaustive input coverage.
