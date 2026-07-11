using System;

namespace CUETools.Wpf.Accuracy;

/// <summary>
/// Feature 1 (cache defeat), pure decision core. When a drive does not honor FUA for audio, the
/// EAC-style approach is to read a large unrelated region to evict the target sector from the
/// drive cache before a secure re-read. This finds the smallest flush size that actually evicts,
/// by binary-searching a monotonic "does size S evict?" predicate (an eviction happens once S is
/// at least the drive's cache size). The predicate is a timing oracle on hardware; here it is a
/// plain function so the search is testable with no drive.
///
/// See docs/superpowers/specs/2026-07-10-rip-accuracy-and-log-integrity-design.md, Feature 1.
/// </summary>
public static class CacheDefeatSearch
{
    /// <summary>Smallest flush size in [minSize, maxSize] for which <paramref name="evicts"/> is
    /// true, rounded up so the search stays on real read granularity, plus a safety margin.
    /// Returns maxSize+margin (capped) if nothing in range evicts (conservative fallback).</summary>
    /// <param name="evicts">Monotonic: false below the cache size, true at/above it.</param>
    public static int SmallestEvicting(int minSize, int maxSize, int step, int margin, Func<int, bool> evicts)
    {
        if (minSize <= 0 || maxSize < minSize || step <= 0) throw new ArgumentException("bad range");
        if (evicts is null) throw new ArgumentNullException(nameof(evicts));

        // snap bounds to the step grid
        int lo = minSize, hi = maxSize;

        if (!evicts(hi)) return Clamp(hi + margin);          // even max doesn't evict -> fallback
        if (evicts(lo)) return Clamp(lo + margin);           // even min evicts

        // invariant: evicts(lo) == false, evicts(hi) == true; shrink to adjacent step boundary
        while (hi - lo > step)
        {
            int mid = lo + ((hi - lo) / (2 * step)) * step;  // midpoint snapped down to step grid
            if (mid <= lo) mid = lo + step;
            if (evicts(mid)) hi = mid; else lo = mid;
        }
        return Clamp(hi + margin);

        int Clamp(int v) => Math.Min(v, maxSize + margin);
    }
}
