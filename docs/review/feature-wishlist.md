# Feature wishlist / parked ideas

Ideas captured for later, not yet scheduled. Each should get its own brainstorm + spec
when picked up.

## Apple high-resolution album art + correct downsizing (parked 2026-07-10)

Owner idea: fetch the highest-resolution cover art from Apple (in the spirit of Ben
Dodson's Apple Music Artwork Finder, https://bendodson.com/projects/apple-music-artwork-finder/),
then downsize it correctly to the user's max-art-size setting with a filter matching RIOT's
(https://riot-optimizer.com/) zero-ringing result.

### Getting Apple's max-resolution art
Apple artwork URLs (on mzstatic.com) carry an embedded size token, e.g.
`.../100x100bb.jpg`. Swapping that token for a larger dimension (or requesting the source
variant) makes Apple's CDN serve up to the source resolution - often 1400-3000 px, sometimes
much larger for Apple Music. Do it right by resolving the EXACT release: CUETools already
reads the disc UPC/barcode, so an exact-UPC lookup via the iTunes/Apple Music API beats the
fuzzy artist+title matching most tools use. Fall back to artist+title when no UPC match.

### Correct downsizing - RESOLVED empirically (2026-07-10)
The filter to use is **Mitchell-Netravali, B = C = 1/3, applied in gamma/sRGB space, with
no post-sharpening.** This was confirmed, not guessed: RIOT downscaled 40 varied 2400x2400
photos to 600x600; each source was re-downscaled here with every candidate filter (ImageSharp)
and pixel-diffed against RIOT's output. Mitchell-Netravali in gamma space won decisively -
mean abs error 0.09/255, 75.3% of pixels bit-identical, max channel deviation 3/255; the
next filter (Robidoux, a near-Mitchell variant) was well behind, and Catmull-Rom/Lanczos far
behind. Gamma space beat linear-light for every filter, and the near-exact match rules out
any sharpening. This matches theory: RIOT is built on FreeImage, whose `FILTER_BICUBIC` is
Mitchell-Netravali B=C=1/3.

Implementation:
- Downscale only when source dimension > `maxAlbumArtSize`; preserve aspect ratio.
- Use `SixLabors.ImageSharp` `KnownResamplers.MitchellNetravali` with `Compand = false`
  (gamma space). Cross-platform, no System.Drawing dependency.
- Output high-quality JPEG (q ~92) or lossless PNG per user preference.
- The probe used to prove this lives in the scratchpad (riotprobe); rebuild if re-checking.

Status: parked, post-rip-flow. Ties into the R13 album-art settings (`maxAlbumArtSize`,
`coversSize`, `coversSearch`, `embedAlbumArt`/`extractAlbumArt`).
