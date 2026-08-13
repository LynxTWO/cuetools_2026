using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using CUETools.CDImage;
using CUETools.Codecs;
using System.Text;

namespace CUETools.Ripper
{
	public interface ICDRipper : IAudioSource, IDisposable
	{
		bool Open(char Drive);
        void EjectDisk();
        void DisableEjectDisc(bool bDisable);
		bool DetectGaps();
		bool GapsDetected { get; }
		CDImageLayout TOC { get; }
		string ARName { get; }
		string EACName { get; }
		int DriveOffset { get; set; }
		int DriveC2ErrorMode { get; set; }
		bool ForceBE { get; set; }
		bool ForceD8 { get; set; }
		string RipperVersion { get; }
		string CurrentReadCommand { get; }
		int CorrectionQuality { get; set; }
		BitArray FailedSectors { get; }
        byte[] RetryCount { get; }
		/// <summary>Bytes the reader evicts per secure re-read pass to defeat the drive cache;
		/// 0 when cache defeat is not active. Human-facing logs must report this truthfully
		/// instead of hardcoding a claim (R113).</summary>
		int CacheDefeatBytes { get; }

		event EventHandler<ReadProgressArgs> ReadProgress;
	}

	public class CDDrivesList
	{
		public static char[] DrivesAvailable()
		{
			List<char> result = new List<char>();
			if (Environment.OSVersion.Platform == PlatformID.Unix)
			{
				// Letters map 1:1 to kernel sr numbers (A = /dev/sr0, B =
				// /dev/sr1, ...). Bwg.Scsi.LinuxSg applies the same pure
				// function when opening by letter; keep the two in agreement.
				for (int n = 0; n < 26; n++)
					if (Directory.Exists("/sys/block/sr" + n))
						result.Add((char)('A' + n));
				return result.ToArray();
			}
			foreach (DriveInfo info in DriveInfo.GetDrives())
				if (info.DriveType == DriveType.CDRom)
					result.Add(info.Name[0]);
			return result.ToArray();
		}
	}

	public sealed class ReadProgressArgs : EventArgs
	{
		public string Action;
		public int Position;
		public int Pass;
		public int PassStart, PassEnd;
		public int ErrorsCount;
		/// <summary>Diagnostic only (read-only): sectors flagged by THIS single pass, as opposed to
		/// ErrorsCount (the running consensus across passes). A value near the window size means the
		/// pass slipped (wholesale disagreement), not that the media is that damaged.</summary>
		public int ThisPassErrors;
		/// <summary>Deep recovery slip classification, surfaced once when a persistent slip is probed.
		/// SlipStrengthPct >= 0 means a verdict is present this event: high with a nonzero SlipOffset =
		/// recoverable jitter; high with offset 0 = identical reads; low = dead media. -1 = no verdict.</summary>
		public int SlipStrengthPct = -1;
		public int SlipOffset;
		/// <summary>Engine give-up verdict, surfaced exactly once per window like the slip verdict:
		/// -1 = no verdict this event; &gt;= 0 means the window's pass loop just ended with this many
		/// sectors still unresolved after the retry policy classified them failed. Consumers that
		/// stop on unrecoverable damage must key on this, not on running mid-pass error counts.</summary>
		public int WindowGivenUpSectors = -1;
		public DateTime PassTime;

		public ReadProgressArgs()
		{
		}

		public ReadProgressArgs(int position, int pass, int passStart, int passEnd, int errorsCount, DateTime passTime)
		{
			Position = position;
			Pass = pass;
			PassStart = passStart;
			PassEnd = passEnd;
			ErrorsCount = errorsCount;
			PassTime = passTime;
		}
	}

    public static class BitArrayUtils
    {
        public static int PopulationCount(this BitArray bits, int start, int len)
        {
            int cnt = 0;
            for (int i = start; i < start + len; i++)
                if (bits[i])
                    cnt++;
            return cnt;
        }

        public static int PopulationCount(this BitArray bits)
        {
            return bits.PopulationCount(0, bits.Count);
        }
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsageAttribute(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
    internal sealed class ExtensionAttribute : Attribute
    {
        public ExtensionAttribute() { }
    }
}
