namespace Bwg.Scsi
{
// Mutation-only compile seam. Test-MutationHarness.ps1 verifies these enum values against
// Bwg.Scsi/Device.cs before any profile runs. The production policy source remains linked.
public class Device
{
    public enum SenseKeyType
    {
        NoSense = 0,
        RecoveredError = 1,
        NotReady = 2,
        MediumError = 3,
        HardwareError = 4,
        IllegalRequest = 5,
        UnitAttention = 6,
        DataProtect = 7,
        BlankCheck = 8,
        VendorSpecific = 9
    }

    public enum CommandStatus
    {
        NotSupported = 0,
        IoctlFailed = 1,
        DeviceFailed = 2,
        Success = 3
    }

    public static string LookupSenseError(byte asc, byte ascq)
    {
        if (asc == 0x08 && ascq == 0x01)
            return "LOGICAL UNIT COMMUNICATION TIME-OUT";
        if (asc == 0x08)
            return "LOGICAL UNIT COMMUNICATION FAILURE: UNASSIGNED QUALIFIER " +
                ascq.ToString("X2") + " (ASC=08, ASCQ=" + ascq.ToString("X2") + ")";
        return "NO SENSE STRING FOR ASC=" + asc.ToString("X2") +
            ", ASCQ=" + ascq.ToString("X2");
    }
}
}

namespace CUETools.Ripper
{
    using System.Collections;

    public sealed class ReadProgressArgs
    {
        public int WindowGivenUpSectors { get; set; } = -1;
    }

    public static class BitArrayExtensions
    {
        public static int PopulationCount(this BitArray bits)
        {
            int count = 0;
            foreach (bool bit in bits)
                if (bit) count++;
            return count;
        }
    }
}
