using System;
using System.Diagnostics;

namespace CUETools.Codecs.CommandLine
{
    /// <summary>
    /// Converts inactivity timeouts to monotonic deadlines. Timer callbacks can already be queued
    /// when progress rearms a timer, so callbacks must recheck the current deadline before acting.
    /// </summary>
    internal static class ProcessTimeoutDeadline
    {
        internal static long FromNow(int timeoutMilliseconds)
        {
            return FromTimestamp(
                timeoutMilliseconds,
                Stopwatch.GetTimestamp(),
                Stopwatch.Frequency);
        }

        internal static int RemainingMilliseconds(long deadline)
        {
            return RemainingMilliseconds(
                deadline,
                Stopwatch.GetTimestamp(),
                Stopwatch.Frequency);
        }

        internal static long FromTimestamp(
            int timeoutMilliseconds,
            long timestamp,
            long frequency)
        {
            if (timeoutMilliseconds <= 0)
                throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            if (frequency <= 0)
                throw new ArgumentOutOfRangeException("frequency");

            long duration = (long)Math.Ceiling(
                (double)timeoutMilliseconds * frequency / 1000.0);
            return timestamp + Math.Max(1L, duration);
        }

        internal static int RemainingMilliseconds(
            long deadline,
            long timestamp,
            long frequency)
        {
            if (frequency <= 0)
                throw new ArgumentOutOfRangeException("frequency");

            long remaining = deadline - timestamp;
            if (remaining <= 0)
                return 0;

            double milliseconds = Math.Ceiling(
                (double)remaining * 1000.0 / frequency);
            if (milliseconds >= Int32.MaxValue)
                return Int32.MaxValue;
            return Math.Max(1, (int)milliseconds);
        }
    }
}
