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

		event EventHandler<ReadProgressArgs> ReadProgress;
	}

	public class CDDrivesList
	{
		public static char[] DrivesAvailable()
		{
			List<char> result = new List<char>();
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
