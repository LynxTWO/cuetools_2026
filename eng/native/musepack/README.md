# CUETools Musepack encoder build

This project builds only the Musepack SV8 encoder from the Debian-preserved
upstream r495 source archive pinned in
`eng/release/external-command-encoders.json`.

Apply `ThirdParty/musepack-r495-cuetools.patch` to the extracted
`musepack_src_r495` tree with `git apply --unidiff-zero`, then configure this directory with
`-DMUSEPACK_SOURCE_DIR=<patched tree>`. The release binary is x64, statically
links the Microsoft runtime, and uses MSVC `/Brepro` with embedded object debug
information. Two independent clean builds must produce the executable SHA-256
recorded in the release manifest.

The build makes all MSVC level-3 diagnostics fatal except the source's
pervasive, intentional float-domain narrowing (`C4244`), signed comparison
(`C4018`), and bounded bitstream-size (`C4267`) conversions. Those three
classes are named at the build boundary instead of being allowed to scroll by
as unreviewed warnings. `_CRT_SECURE_NO_WARNINGS` and
`_CRT_NONSTDC_NO_WARNINGS` retain the source's bounded legacy CRT calls and
portable POSIX spellings without masking unrelated compiler diagnostics.

The patch makes the old source compile safely with a current C compiler:

- it gives the scale-factor matrix and quantizer declarations their real types;
- it removes CRT name collisions without changing the math;
- it fixes the right-channel non-noise-shaped quantizer using the left-channel
  error history;
- it replaces build-time date/time text with a stable source identity.

The build defines `CUETOOLS_NO_TAGS` and does not compile `common/tags.c`.
That upstream file says “all rights reserved,” and the preserved source bundle
does not attach a clear redistribution grant to it. The resulting executable
therefore rejects encoder-side tag options instead of silently accepting them.
CUETools writes APEv2 metadata after encoding through its normal metadata
transaction.

The compiled files are LGPL-2.1-or-later or BSD-3-Clause as recorded by
Debian's source copyright inventory. The release accompanies the binary with
the complete upstream archive, this patch, this build project, the LGPL text,
and prominent modification/provenance notices.
