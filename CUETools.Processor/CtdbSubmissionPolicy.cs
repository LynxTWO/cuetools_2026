using System;

namespace CUETools.Processor
{
    /// <summary>
    /// Central consent boundary for CTDB contribution. Verification and repair queries do not
    /// require submission, and no application may infer consent from performing a rip.
    /// </summary>
    public static class CtdbSubmissionPolicy
    {
        public static bool IsEnabled(CUEConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            return config.advanced.CTDBSubmit;
        }
    }
}
