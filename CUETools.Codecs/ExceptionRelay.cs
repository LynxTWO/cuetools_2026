using System;
#if !NET20
using System.Runtime.ExceptionServices;
#endif

namespace CUETools.Codecs
{
    /// <summary>
    /// Rethrows an exception captured on a worker thread. Modern targets preserve the producer
    /// stack. .NET 2.0 cannot do that without changing the public exception type or relying on
    /// private runtime APIs, so its compatibility path preserves the original type and identity.
    /// </summary>
    internal static class ExceptionRelay
    {
        internal static void Throw(Exception exception)
        {
            if (exception == null)
                throw new ArgumentNullException("exception");

#if NET20
            // This resets the visible throw site on .NET 2.0, but matches the legacy API contract:
            // callers still receive the exact producer exception rather than a new wrapper type.
            throw exception;
#else
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw new InvalidOperationException("Unreachable after rethrow.");
#endif
        }
    }
}
