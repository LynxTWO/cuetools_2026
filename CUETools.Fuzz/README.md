# CUETools.Fuzz

Property-based fuzz harness for the R12 features. Standalone tool - not in `CUETools.sln`, run it
directly with `dotnet run`.

## What it fuzzes

- **SCSI response parsers** (`Bwg.Scsi`: `InquiryResult`, `Feature`, `FeatureList`,
  `EventStatusNotification`, `SpeedDescriptor`, `SpeedDescriptorList`). These parse raw bytes the
  drive returns - the real untrusted-input surface behind `DriveInspector` / the Drive & Read
  screen. The parsers bounds-check via `Result.Get8` (throws a catchable `Exception` on over-read),
  so a malformed reply must never crash the process, only throw. Invariant: complete 300k random
  buffers with zero uncatchable crashes.
- **`CodecMath`** (the codec-scope predictor + Rice-cost math). Feeds adversarial sample windows
  (NaN, +/-Inf, +/-huge, denormal, zero, normal audio) through every codec family. Invariant: never
  throws, and the returned bits/sample is finite and in [1,16].
- **GUI random-walk** (`--gui`): attaches to the running `CUETools.Wpf` window and hammers it with
  SAFE actions only - random page navigation, switch toggling, window resizes - checking the process
  stays alive. It does NOT invoke Rip / Verify / Eject / Convert / Detect / folder-picker buttons
  (hardware, filesystem, or blocking-dialog side effects). MOUSE-FREE: it drives controls through
  UIAutomation patterns and never moves the physical cursor or steals focus, so you can keep using
  the machine while it runs. Stresses the DynamicResource theme swap, page switching, the GPU-drawn
  custom controls, and layout under random sizes.
- **Toggle-combination sweep** (`--toggles`): navigates to Settings and drives every switch through
  all 2^N combinations (or a 4096 random sample when N is large), checking health after each.
  Toggling only sets a config bool / the theme, so no combination should misbehave - this proves it
  and would catch a setter/binding surprise. Verified: 4096 combinations across 16 switches, healthy.

## Run

```sh
dotnet run -c Release                  # headless fuzzers, default seed + 300k iters
dotnet run -c Release -- 42 500000     # seed, iterations
dotnet run -c Release -- --gui         # random-walk the already-running app window
dotnet run -c Release -- --gui 42 300  # seed, steps
dotnet run -c Release -- --toggles     # sweep every switch combination (app running)
```

Exit code is non-zero on any failure, and failures print the seed to reproduce.

## Findings so far

- `SpeedDescriptor` reads speeds via `Result.Get32Int`, whose `Debug.Assert(b < Int32.MaxValue)`
  fires on a high-bit value - terminating the process in Debug, silently wrapping to a negative
  speed in Release. Fixed in the app by clamping absurd/negative speeds in
  `DriveInspector.ReadSpeeds`; the harness clears trace listeners so the vendored assert can't stop
  the run.

## Future

A coverage-guided fuzzer (SharpFuzz over libFuzzer/AFL) would explore deeper than random inputs.
This property-based harness is the CI-friendly first line and catches crash/robustness bugs today.
