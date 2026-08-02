// ****************************************************************************
// 
// CUERipper
// Copyright (C) 2008-2025 Grigory Chudov (gchudov@gmail.com)
// 
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
// 
// ****************************************************************************

using System;
using System.Runtime.InteropServices;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using Bwg.Scsi;
using Bwg.Logging;
using CUETools.CDImage;
using CUETools.Codecs;
using CUETools.Ripper;
using System.Threading;

namespace CUETools.Ripper.SCSI
{
	/// <summary>
	/// 
	/// </summary>
	public class CDDriveReader : ICDRipper
	{
		private Device m_device;
		int _sampleOffset = 0;
		int _driveOffset = 0;
		DriveC2ErrorModeSetting _driveC2ErrorMode = DriveC2ErrorModeSetting.Auto;
		int _correctionQuality = 1;

		/// <summary>Opt-in deep recovery (see AppSettings.DeepRecovery). Effective only at
		/// CorrectionQuality &gt; 0 (decision D9): Burst keeps the classic 16-pass cap and never
		/// enters the extension. When false the re-read loop
		/// and read path behave exactly as before. Set before the rip starts.</summary>
		public bool DeepRecovery { get; set; } = false;
		int _currentStart = -1, _currentEnd = -1, _currentErrorsCount = 0;
		// Diagnostic only (read-only): sectors THIS pass flagged as erroneous, reset each pass. A value
		// near the window size means the pass slipped (wholesale disagreement), not that much media is
		// damaged. Never feeds bestValue, _currentErrorsCount, m_retryCount, or the early-break.
		int _thisPassErrors = 0;
		// Deep recovery: pure progress-aware cap decision for a stuck window. No SCSI, no state that
		// feeds the vote. Only consulted when DeepRecovery is on.
		private readonly RecoveryPolicy _recovery = new RecoveryPolicy();
		// Deep recovery slip classification (read-only): per-window state. Classifies a persistent slip
		// as recoverable jitter vs dead media by correlating repeated raw reads. Never touches the vote.
		private bool _slipClassified, _slipVerdictPending;
		private int _slipStreak, _slipVerdictOff, _slipVerdictPct;
		// Cache defeat (opt-in): flush this many bytes of an unrelated in-program region into scratch
		// before a secure re-read, evicting the target from the drive cache so the re-read hits media
		// (a real second read) instead of the cached first read. 0 = off. Scratch-only, cannot corrupt.
		private int _cacheDefeatBytes;
		private byte[] _flushScratch;
		private bool _cacheDefeatJustFlushed;
		private string _cacheDefeatFailure = "no cache-defeat attempt recorded";
		private int _cacheDefeatRetryCount;
		private int _cacheDefeatChunkFallbackCount;
		private int _cacheDefeatWakeCount;
		private int _cacheDefeatWakeReadinessRetryCount;
		private int _cacheDefeatWakeReadinessIndeterminateCount;
		private bool _speedChangeJustApplied;
		private int _controlTransitionRetryCount;
		private int _readCommunicationRetryCount;
		private int _payloadBatchFallbackCount;
		private int _pinpointRetryCount;
		private int _corroboratedUnreadablePinpointCount;
		private int _givenUpWindowCount;
		private int _shortPayloadTransferCount;
		private int _extendedTimeoutReadCount;
		private int _driveReportedTimeoutPinpointCount;
		public void SetCacheDefeat(int flushBytes) => _cacheDefeatBytes = Math.Max(0, flushBytes);
		public int CacheDefeatBytes => _cacheDefeatBytes;
		public int ControlTransitionRetryCount => _controlTransitionRetryCount;
		public int ReadCommunicationRetryCount => _readCommunicationRetryCount;
		public int CacheDefeatRetryCount => _cacheDefeatRetryCount;
		public int CacheDefeatChunkFallbackCount => _cacheDefeatChunkFallbackCount;
		public int CacheDefeatWakeCount => _cacheDefeatWakeCount;
		public int CacheDefeatWakeReadinessRetryCount =>
			_cacheDefeatWakeReadinessRetryCount;
		public int CacheDefeatWakeReadinessIndeterminateCount =>
			_cacheDefeatWakeReadinessIndeterminateCount;
		public int PayloadBatchFallbackCount => _payloadBatchFallbackCount;
		public int PinpointRetryCount => _pinpointRetryCount;
		public int CorroboratedUnreadablePinpointCount =>
			_corroboratedUnreadablePinpointCount;
		/// <summary>Windows whose pass loop ended with unresolved sectors (engine-classified
		/// give-up, R106). Complements FailedSectors, which carries the per-sector bits.</summary>
		public int GivenUpWindowCount => _givenUpWindowCount;
		/// <summary>GOOD-status payload reads rejected because the transferred byte count did
		/// not match the exact request (R112). Live occurrence evidence for the open
		/// GOOD-status-underrun unknown; expected to stay zero.</summary>
		public int ShortPayloadTransferCount => _shortPayloadTransferCount;
		/// <summary>Low-speed recovery reads issued with the extended R118 timeout instead of
		/// the baseline. Activation evidence for the ERROR_SEM_TIMEOUT mitigation.</summary>
		public int ExtendedTimeoutReadCount => _extendedTimeoutReadCount;
		/// <summary>Pinpoint sectors the drive itself surrendered on (3E/02 after a MediumError
		/// parent) that entered the untrusted path instead of failing the job (R118).</summary>
		public int DriveReportedTimeoutPinpointCount => _driveReportedTimeoutPinpointCount;
		private bool IsObservedReadCommunicationDrive =>
			m_inqury_result != null &&
			string.Equals(
				m_inqury_result.VendorIdentification.TrimEnd(' ', '\0'),
				"HL-DT-ST",
				StringComparison.OrdinalIgnoreCase) &&
			string.Equals(
				m_inqury_result.ProductIdentification.TrimEnd(' ', '\0'),
				"BD-RE  WH16NS40",
				StringComparison.OrdinalIgnoreCase) &&
			string.Equals(
				m_inqury_result.FirmwareVersion.TrimEnd(' ', '\0'),
				"1.05",
				StringComparison.OrdinalIgnoreCase);
		// Offset correction needs samples just outside the nominal audio program. These switches are
		// enabled only after calibration has proved that this exact drive accepts the corresponding
		// READ CD address. Without proof the historic zero-padding behavior remains unchanged.
		private bool _overreadLeadIn;
		private bool _overreadLeadOut;
		private int _retrySectorBase;
		public void SetOverread(bool leadIn, bool leadOut)
		{
			_overreadLeadIn = leadIn;
			_overreadLeadOut = leadOut;
			ResetRetryState();
		}
		const int CB_AUDIO = 4 * 588 + 2 + 294 + 16;
		const int NSECTORS = 16;
		//const int MSECTORS = 5*1024*1024 / (4 * 588);
		const int MSECTORS = 2400;
		Logger m_logger;
		CDImageLayout _toc;
		CDImageLayout _toc2;
		char m_device_letter;
		InquiryResult m_inqury_result;
		int m_max_sectors;
		int _timeout = 10;
		Crc16Ccitt _crc;
		public long[,,] UserData;
		public byte[,] C2Count;
		public long[] byte2long;
		BitArray m_failedSectors;
        byte[] m_retryCount;
		AudioBuffer currentData = new AudioBuffer(AudioPCMConfig.RedBook, MSECTORS * 588);
		short[] _valueScore = new short[256];
		bool _debugMessages = false;
		ReadCDCommand _readCDCommand = ReadCDCommand.Unknown;
		ReadCDCommand _forceReadCommand = ReadCDCommand.Unknown;
		Device.MainChannelSelection _mainChannelMode = Device.MainChannelSelection.UserData;
		Device.C2ErrorMode _c2ErrorMode = Device.C2ErrorMode.Mode296;
		string _autodetectResult;
		byte[] _readBuffer = new byte[NSECTORS * CB_AUDIO];
        byte[] _subchannelBuffer = new byte[NSECTORS * CB_AUDIO];
		bool _qChannelInBCD = true;

		private ReadProgressArgs progressArgs = new ReadProgressArgs();
		public event EventHandler<ReadProgressArgs> ReadProgress;

        public IAudioDecoderSettings Settings => null;

        public CDImageLayout TOC
		{
			get
			{
				return gapsDetected && _toc2 != null ? _toc2 : _toc;
			}
		}

		/// <summary>Read the disc's CD-Text (READ TOC format 0x05) on demand and decode it. Kept
		/// OUT of the TOC path so a drive that stalls on the command cannot break disc detection;
		/// callers treat null as "this disc has no CD-Text". Cached after the first read.</summary>
		public CDTextInfo ReadCDText()
		{
			if (_cdTextRead) return _cdText;
			_cdTextRead = true;
			try
			{
				byte[] raw;
				if (m_device.ReadCDText(out raw, _timeout) != Device.CommandStatus.Success || raw == null)
					return null;
				var parsed = CDTextParser.Parse(raw);
				_cdText = parsed.HasText ? parsed : null;
			}
			catch { _cdText = null; }
			return _cdText;
		}
		private CDTextInfo _cdText;
		private bool _cdTextRead;

        public byte[] RetryCount
        {
            get
            {
                return m_retryCount;
            }
        }

        public BitArray FailedSectors
        {
            get
            {
                // Maintained by the engine: FailedSectorAccounting.FinalizeWindow sets bits when
                // a window's pass loop ends unresolved (R106). The lazy fallback covers a drive
                // that never ripped (or was closed, when _toc is null) so log generation reports
                // "no failures" instead of throwing. Never derive failure from retry-count
                // arithmetic here: deep recovery's extension overshoots the legacy sentinel.
                if (m_failedSectors == null)
                    m_failedSectors = new BitArray(_toc == null ? 0 : (int)_toc.AudioLength);
                return m_failedSectors;
            }
        }

		public int Timeout
		{
			get
			{
				return _timeout;
			}
			set
			{
				_timeout = value;
			}
		}

		public bool DebugMessages
		{
			get
			{
				return _debugMessages;
			}
			set
			{
				_debugMessages = value;
			}
		}

		public string AutoDetectReadCommand
		{
			get
			{
				TestReadCommand();
				return _autodetectResult;
			}
		}

		public bool ForceD8
		{
			get
			{
				return _forceReadCommand == ReadCDCommand.ReadCdD8h;
			}
			set
			{
				_forceReadCommand = value ? ReadCDCommand.ReadCdD8h : ReadCDCommand.Unknown;
			}
		}


		public bool ForceBE
		{
			get
			{
				return _forceReadCommand == ReadCDCommand.ReadCdBEh;
			}
			set
			{
				_forceReadCommand = value ? ReadCDCommand.ReadCdBEh : ReadCDCommand.Unknown;
			}
		}

		public string CurrentReadCommand
		{
			get
			{
				return _readCDCommand == ReadCDCommand.Unknown ? "unknown" :
					string.Format("{0}, {1:X2}h, {2}{3}, {4} blocks at a time", 
					(_readCDCommand == ReadCDCommand.ReadCdBEh ? "BEh" : "D8h"),
					(_mainChannelMode == Device.MainChannelSelection.UserData ? 0x10 : 0xF8) +
					(_c2ErrorMode == Device.C2ErrorMode.None ? 0 : _c2ErrorMode == Device.C2ErrorMode.Mode294 ? 2 : 4),
					(_gapDetection == GapDetectionMethod.ReadCD ? "BEh" : _gapDetection == GapDetectionMethod.ReadSubchannel ? "42h" : ""),
					_qChannelInBCD ? "" : "nonBCD",
					m_max_sectors);
			}
		}

		public CDDriveReader()
		{
			m_logger = new Logger();
			_crc = new Crc16Ccitt(InitialCrcValue.Zeros);
			byte2long = new long[256];
			for (long i = 0; i < 256; i++)
			{
				long bl = 0;
				for (int b = 0; b < 8; b++)
					bl += ((i >> b) & 1) << (b << 3);
				byte2long[i] = bl;
			}
		}

		public bool Open(char Drive)
		{
			Device.CommandStatus st;

			m_inqury_result = null;

			// Open the base device
			m_device_letter = Drive;
			if (m_device != null)
				Close();

			m_device = new Device(m_logger);
			if (!m_device.Open(m_device_letter))
				throw new ReadCDException(Resource1.DeviceOpenError, Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
			//throw new ReadCDException(Resource1.DeviceOpenError + ": " + WinDev.Win32ErrorToString(m_device.LastError));

			// Get device info
			st = m_device.Inquiry(out m_inqury_result);
			if (st != Device.CommandStatus.Success)
				throw new SCSIException(Resource1.DeviceInquiryError, m_device, st);
			if (!m_inqury_result.Valid || m_inqury_result.PeripheralQualifier != 0 || m_inqury_result.PeripheralDeviceType != Device.MMCDeviceType)
				throw new ReadCDException(Resource1.DeviceNotMMC);

			m_max_sectors = Math.Min(NSECTORS, m_device.MaximumTransferLength / CB_AUDIO - 1);
			//// Open/Initialize the driver
			//Drive m_drive = new Drive(dev);
			//DiskOperationError status = m_drive.Initialize();
			//if (status != null)
			//    throw new Exception("SCSI error");

			// {
			//Drive.FeatureState readfeature = m_drive.GetFeatureState(Feature.FeatureType.CDRead);
			//if (readfeature == Drive.FeatureState.Error || readfeature == Drive.FeatureState.NotPresent)
			//    throw new Exception("SCSI error");
			// }{
			//st = m_device.GetConfiguration(Device.GetConfigType.OneFeature, 0, out flist);
			//if (st != Device.CommandStatus.Success)
			//    return CreateErrorObject(st, m_device);

			//Feature f = flist.Features[0];
			//ParseProfileList(f.Data);
			// }

			//SpeedDescriptorList speed_list;
			//st = m_device.GetSpeed(out speed_list);
			//if (st != Device.CommandStatus.Success)
			//    throw new Exception("GetSpeed failed: SCSI error");

			//m_device.SetCdSpeed(Device.RotationalControl.CLVandNonPureCav, (ushort)(0x7fff), (ushort)(0x7fff));
			//int bytesPerSec = 4 * 588 * 75 * (pass > 8 ? 4 : pass > 4 ? 8 : pass > 0 ? 16 : 32);
			//Device.CommandStatus st = m_device.SetStreaming(Device.RotationalControl.CLVandNonPureCav, start, end, bytesPerSec, 1, bytesPerSec, 1);
			//if (st != Device.CommandStatus.Success)
			//    System.Console.WriteLine("SetStreaming: ", (st == Device.CommandStatus.DeviceFailed ? Device.LookupSenseError(m_device.GetSenseAsc(), m_device.GetSenseAscq()) : st.ToString()));
			//st = m_device.SetCdSpeed(Device.RotationalControl.CLVandNonPureCav, (ushort)(bytesPerSec / 1024), (ushort)(bytesPerSec / 1024));
			//if (st != Device.CommandStatus.Success)
			//    System.Console.WriteLine("SetCdSpeed: ", (st == Device.CommandStatus.DeviceFailed ? Device.LookupSenseError(m_device.GetSenseAsc(), m_device.GetSenseAscq()) : st.ToString()));


			//st = m_device.SetCdSpeed(Device.RotationalControl.CLVandNonPureCav, 32767/*Device.OptimumSpeed*/, Device.OptimumSpeed);
			//if (st != Device.CommandStatus.Success)
			//    throw new Exception("SetCdSpeed failed: SCSI error");

			IList<TocEntry> toc;
			st = m_device.ReadToc((byte)0, false, out toc);
			if (st != Device.CommandStatus.Success)
				throw new SCSIException(Resource1.ReadTOCError, m_device, st);
				//throw new Exception("ReadTOC: " + (st == Device.CommandStatus.DeviceFailed ? Device.LookupSenseError(m_device.GetSenseAsc(), m_device.GetSenseAscq()) : st.ToString()));

			//byte[] qdata = null;
			//st = m_device.ReadPMA(out qdata);
			//if (st != Device.CommandStatus.Success)
			//    throw new SCSIException("ReadPMA", m_device, st);

			//st = m_device.ReadCDText(out cdtext, _timeout);
			// new CDTextEncoderDecoder

			_toc2 = null;
			_toc = new CDImageLayout();
			for (int iTrack = 0; iTrack < toc.Count - 1; iTrack++)
				_toc.AddTrack(new CDTrack((uint)iTrack + 1, 
					toc[iTrack].StartSector,
					toc[iTrack + 1].StartSector - toc[iTrack].StartSector - 
					    ((toc[iTrack + 1].Control < 4 || iTrack + 1 == toc.Count - 1) ? 0U : 152U * 75U), 
					toc[iTrack].Control < 4,
					(toc[iTrack].Control & 1) == 1));			
			if (_toc.AudioLength > 0)
			{
				if (_toc[1].IsAudio)
					_toc[1][0].Start = 0;
				Position = 0;
			} else
				throw new ReadCDException(Resource1.NoAudio);

            UserData = new long[MSECTORS, 2, 4 * 588];
            C2Count = new byte[MSECTORS, 294];

			return true;
		}

		public void Close()
		{
			UserData = null;
			C2Count = null;
			if (m_device != null)
				m_device.Close();
			m_device = null;
			_toc = null;
			_toc2 = null;
			gapsDetected = false;
			readCommandFound = false;
			_currentStart = -1;
			_currentEnd = -1;
		}

		public void Dispose()
		{
			Close();
		}

		public int BestBlockSize
		{
			get
			{
				return m_max_sectors * 588;
			}
		}

		private enum GapDetectionMethod
		{
			ReadCD,
			ReadSubchannel,
			None
		}

		private GapDetectionMethod _gapDetection = GapDetectionMethod.None;

		private unsafe void LocateLastSector(int sec0, int sec1, int iTrack, int iIndex, ref int maxIndex, out int pos)
		{
			if (sec1 <= sec0)
			{
				pos = Math.Min(sec0, sec1);
				return;
			}
			int msector = (sec0 + sec1) / 2;
			int fsector = Math.Max(sec0, msector - 1);
			int lsector = Math.Min(sec1, msector + 3);

			if (lsector >= fsector && _gapDetection == GapDetectionMethod.ReadCD)
			{
				Device.CommandStatus st = m_device.ReadSubChannel(2, (uint)fsector, (uint)(lsector - fsector + 1), ref _subchannelBuffer, _timeout);
				if (st != Device.CommandStatus.Success)
					lsector = fsector - 1;
			}

			fixed (byte * data = _subchannelBuffer)
				for (int sector = fsector; sector <= lsector; sector++)
				{
					Device.CommandStatus st = Device.CommandStatus.Success;
					int sTrack, sIndex, sPos, ctl;
					switch (_gapDetection)
					{
						case GapDetectionMethod.ReadSubchannel:
							{
								// seek to given sector
								if (_readCDCommand == ReadCDCommand.ReadCdBEh)
									st = m_device.ReadCDAndSubChannel(_mainChannelMode, Device.SubChannelMode.None, _c2ErrorMode, 1, false, (uint)sector, 1, (IntPtr)((void*)data), _timeout);
								else
									st = m_device.ReadCDDA(Device.SubChannelMode.None, (uint)sector, 1, (IntPtr)((void*)data), _timeout);
								if (st != Device.CommandStatus.Success)
									continue;
								st = m_device.ReadSubChannel42(1, 0, ref _subchannelBuffer, 0, _timeout);
								// x x x x 01 adrctl tr ind abs abs abs rel rel rel
								if (st != Device.CommandStatus.Success)
									continue;
								if (_subchannelBuffer[0] != 0 || _subchannelBuffer[2] != 0 || _subchannelBuffer[3] != 12 || _subchannelBuffer[4] != 1)
									continue;

								ctl = _subchannelBuffer[5] & 0xf;
								int adr = (_subchannelBuffer[5] >> 4) & 0xf;
								sTrack = _subchannelBuffer[6];
								sIndex = _subchannelBuffer[7];
								sPos = (_subchannelBuffer[8] << 24) | (_subchannelBuffer[9] << 16) | (_subchannelBuffer[10] << 8) | (_subchannelBuffer[11]);
								//int sRel = (_subchannelBuffer[12] << 24) | (_subchannelBuffer[13] << 16) | (_subchannelBuffer[14] << 8) | (_subchannelBuffer[15]);
								if (adr != 1)
									continue;

								if (sTrack < _toc2.FirstAudio || sTrack >= _toc2.FirstAudio + _toc2.AudioTracks)
									continue;

								break;
							}
						case GapDetectionMethod.ReadCD:
							{
								int offs = 16 * (sector - fsector);
								ctl = _subchannelBuffer[offs + 0] >> 4;
								int adr = _subchannelBuffer[offs + 0] & 7;
								if (!_qChannelInBCD && adr == 1)
								{
									_subchannelBuffer[offs + 3] = toBCD(_subchannelBuffer[offs + 3]);
									_subchannelBuffer[offs + 4] = toBCD(_subchannelBuffer[offs + 4]);
									_subchannelBuffer[offs + 5] = toBCD(_subchannelBuffer[offs + 5]);
									_subchannelBuffer[offs + 7] = toBCD(_subchannelBuffer[offs + 7]);
									_subchannelBuffer[offs + 8] = toBCD(_subchannelBuffer[offs + 8]);
									_subchannelBuffer[offs + 9] = toBCD(_subchannelBuffer[offs + 9]);
								}

								ushort crc = _crc.ComputeChecksum(_subchannelBuffer, offs, 10);
								crc ^= 0xffff;
								ushort scrc = (ushort)((_subchannelBuffer[offs + 10] << 8) | _subchannelBuffer[offs + 11]);
								if (scrc != 0 && scrc != crc)
									continue;
								if (adr != 1)
									continue;

								sTrack = fromBCD(_subchannelBuffer[offs + 1]);
								sIndex = fromBCD(_subchannelBuffer[offs + 2]);

								if (sTrack < _toc2.FirstAudio || sTrack >= _toc2.FirstAudio + _toc2.AudioTracks)
									continue;

								int mm = fromBCD(_subchannelBuffer[offs + 7]);
								int ss = fromBCD(_subchannelBuffer[offs + 8]);
								int ff = fromBCD(_subchannelBuffer[offs + 9]);
								sPos = ff + 75 * (ss + 60 * mm) - 150;
								break;
							}
						default:
							continue;
					}

					bool preemph = (ctl & 1) == 1;
					bool dcp = (ctl & 2) == 2;
					if (preemph)
						_toc2[sTrack].PreEmphasis = true;
					if (dcp)
						_toc2[sTrack].DCP = true;

					if (sPos <= sec0 || sPos > sec1)
						continue;
					if (sTrack > iTrack || (sTrack == iTrack && iIndex >= 0 && sIndex > iIndex))
					{
						LocateLastSector(sec0, sPos - 1, iTrack, iIndex, ref maxIndex, out pos);
						return;
					}
					if (sTrack < iTrack || (sTrack == iTrack && (iIndex < 0 || sIndex <= iIndex)))
					{
						if (sTrack == iTrack && iIndex < 0)
							maxIndex = sIndex;
						LocateLastSector(sPos, sec1, iTrack, iIndex, ref maxIndex, out pos);
						return;
					}
				}
			if (sec1 <= sec0 + 16)
			{
				pos = Math.Min(sec0, sec1);
				return;
			}

			// TODO: catch?
			throw new Exception("gap detection failed");
		}

		private unsafe void TestGaps()
		{
			_gapDetection = GapDetectionMethod.None;

			//st = m_device.Seek((uint)(sector + i * 33) + _toc[_toc.FirstAudio][0].Start);
			//if (st != Device.CommandStatus.Success)
			//    break;
			//bool ready;
			//st = m_device.TestUnitReady(out ready);
			//if (st != Device.CommandStatus.Success)
			//    break;
			//if (!ready)
			//{
			//    st = Device.CommandStatus.NotSupported;
			//    break;
			//}

			// try ReadCD:
			Device.CommandStatus st;
			int sector = 3;

			if (_readCDCommand == ReadCDCommand.ReadCdBEh)
			{
                // PLEXTOR PX-W1210A always returns data, even if asked only for subchannel.
                // So we fill the buffer with magic data, give extra space for command so it won't hang the drive,
                // request subchannel data and check if magic data was overwritten.
                bool overwritten = false;
                for (int i = 0; i < 16; i++)
                {
                    _subchannelBuffer[m_max_sectors * (588 * 4 + 16) - 16 + i] = (byte)(13 + i);
                }
                fixed (byte* data = _subchannelBuffer)
                {
                    st = m_device.ReadCDAndSubChannel(Device.MainChannelSelection.None, Device.SubChannelMode.QOnly, Device.C2ErrorMode.None, 1, false, (uint)sector + _toc[_toc.FirstAudio][0].Start, (uint)m_max_sectors, (IntPtr)((void*)data), _timeout);
                }
                for (int i = 0; i < 16; i++)
                {
                    if (_subchannelBuffer[m_max_sectors * (588 * 4 + 16) - 16 + i] != (byte)(13 + i))
                        overwritten = true;
                }
                if (overwritten)
                    st = Device.CommandStatus.NotSupported;
                //else
                //    st = m_device.ReadSubChannel(2, (uint)sector + _toc[_toc.FirstAudio][0].Start, (uint)m_max_sectors, ref _subchannelBuffer, _timeout);
				if (st == Device.CommandStatus.Success)
				{
					int[] goodsecs = new int[2];
					for (int bcd = 1; bcd >= 0; bcd--)
					{
						for (int i = 0; i < m_max_sectors; i++)
						{
							int adr = _subchannelBuffer[i * 16 + 0] & 7;
							if (bcd == 0 && adr == 1)
							{
								_subchannelBuffer[i * 16 + 3] = toBCD(_subchannelBuffer[i * 16 + 3]);
								_subchannelBuffer[i * 16 + 4] = toBCD(_subchannelBuffer[i * 16 + 4]);
								_subchannelBuffer[i * 16 + 5] = toBCD(_subchannelBuffer[i * 16 + 5]);
								_subchannelBuffer[i * 16 + 7] = toBCD(_subchannelBuffer[i * 16 + 7]);
								_subchannelBuffer[i * 16 + 8] = toBCD(_subchannelBuffer[i * 16 + 8]);
								_subchannelBuffer[i * 16 + 9] = toBCD(_subchannelBuffer[i * 16 + 9]);
							}

							ushort crc = _crc.ComputeChecksum(_subchannelBuffer, i * 16, 10);
							crc ^= 0xffff;
							ushort scrc = (ushort)((_subchannelBuffer[i * 16 + 10] << 8) | _subchannelBuffer[i * 16 + 11]);
							if (scrc != 0 && scrc != crc)
								continue;
							if (adr != 1)
								continue;

							int sTrack = fromBCD(_subchannelBuffer[i * 16 + 1]);
							int sIndex = fromBCD(_subchannelBuffer[i * 16 + 2]);

							if (sTrack == 0 || sTrack == 110)
								continue;

							int mm = fromBCD(_subchannelBuffer[i * 16 + 7]);
							int ss = fromBCD(_subchannelBuffer[i * 16 + 8]);
							int ff = fromBCD(_subchannelBuffer[i * 16 + 9]);
							int sPos = ff + 75 * (ss + 60 * mm) - 150;

							if (sPos < sector + i - 8 || sPos > sector + i + 8)
								continue;

							goodsecs[bcd]++;
						}
					}

					if (goodsecs[0] > 0 || goodsecs[1] > 0)
					{
						_qChannelInBCD = goodsecs[1] >= goodsecs[0];
						_gapDetection = GapDetectionMethod.ReadCD;
					}
				}
			}

			if (_gapDetection == GapDetectionMethod.None)
			{
				fixed (byte* data = _subchannelBuffer)
				{
					// seek to given sector
					if (_readCDCommand == ReadCDCommand.ReadCdBEh)
						st = m_device.ReadCDAndSubChannel(_mainChannelMode, Device.SubChannelMode.None, _c2ErrorMode, 1, false, (uint)sector + _toc[_toc.FirstAudio][0].Start, 1, (IntPtr)((void*)data), _timeout);
					else
						st = m_device.ReadCDDA(Device.SubChannelMode.None, (uint)sector + _toc[_toc.FirstAudio][0].Start, 1, (IntPtr)((void*)data), _timeout);
				}
				if (st == Device.CommandStatus.Success)
				{
					st = m_device.ReadSubChannel42(1, 0, ref _subchannelBuffer, 0, _timeout);
					if (st == Device.CommandStatus.Success)
					{
						if (_subchannelBuffer[0] == 0 && _subchannelBuffer[2] == 0 && _subchannelBuffer[3] == 12 && _subchannelBuffer[4] == 1)
						{
							int ctl = _subchannelBuffer[5] & 0xf;
							int adr = (_subchannelBuffer[5] >> 4) & 0xf;
							if (adr == 1)
							{
								_gapDetection = GapDetectionMethod.ReadSubchannel;
							}
						}
					}
				}
			}
		}

		public bool GapsDetected
		{
			get
			{
				return gapsDetected;
			}
		}

        private void DoEjectDisk(Device.StartState action)
        {
            EventStatusNotification status;
            m_device.GetEventStatusNotification(true, Device.NotificationClass.Media, out status);
            if (status.Valid && status.EventAvailable && status.EventData.Length > 1)
            {
                // 0 - tray closed no disc
                // 1 - tray open
                // 2 - tray closed, disc
                action = status.EventData[1] == 1 ? Device.StartState.LoadDisk : Device.StartState.EjectDisk;
            }
            m_device.StartStopUnit(true, Device.PowerControl.NoChange, action);
        }

        public void EjectDisk()
        {
            if (m_device != null)
            {
                DoEjectDisk(Device.StartState.EjectDisk);
            }
            else
            {
                try
                {
                    m_device = new Device(m_logger);
                    if (m_device.Open(m_device_letter))
                    {
                        try
                        {
                            DoEjectDisk(Device.StartState.LoadDisk);
                        }
                        finally
                        {
                            m_device.Close();
                        }
                    }
                }
                finally
                {
                    m_device = null;
                }
            }
        }

        // ---- adaptive read speed (Feature 3) ----
        // The request is only STORED here; the read loop applies it at the next fresh-window
        // boundary in PrefetchSector (see there for why it must never be applied mid-window).
        private volatile int _pendingSpeedKbps;
        private int _appliedSpeedKbps;

        /// <summary>Ask the reader to change read speed (kB/s, CD 1x = 176) at the next safe point.
        /// Safe from any thread; only changes HOW FAST sectors are read - the audio data is
        /// identical at any speed, so accuracy is unaffected.</summary>
        public void RequestReadSpeed(int kbps) { if (kbps > 0) _pendingSpeedKbps = kbps; }

        /// <summary>The read speed (kB/s) most recently accepted by the drive, 0 if never set.</summary>
        public int AppliedReadSpeed => _appliedSpeedKbps;

        /// <summary>The read speeds (kB/s) the drive can step through for adaptive speed, ascending.
        /// Most drives report only their MAX via GET PERFORMANCE, so the standard CD ladder
        /// (4x..max) is synthesized from it - SET CD SPEED rounds each step to what the drive
        /// really supports. Empty if the drive reports no speed. Read-only query.</summary>
        public int[] GetSupportedSpeeds()
        {
            try
            {
                if (m_device == null) return new int[0];
                SpeedDescriptorList list;
                if (m_device.GetSpeed(out list) != Device.CommandStatus.Success || list == null) return new int[0];

                // the drive's true max read speed (guard a malformed high-bit reply, per DriveInspector)
                const int SaneMax = 200000, X = 176;
                int maxKBps = 0;
                foreach (SpeedDescriptor d in list) if (d.ReadSpeed > maxKBps && d.ReadSpeed <= SaneMax) maxKBps = d.ReadSpeed;
                if (maxKBps < 2 * X) return new int[0];   // no usable range

                // ladder steps clearly BELOW the max, then the true max on top - so the top step is
                // not a near-duplicate of the max (e.g. an 8448 "48x" step vs the drive's 8467 max,
                // whose first step-down would be imperceptible)
                int[] steps = { 4 * X, 8 * X, 12 * X, 16 * X, 24 * X, 32 * X, 40 * X, 48 * X };
                var speeds = new List<int>();
                // steps is already strictly ascending, and the 97% cutoff guarantees every
                // retained rung is below the final max, so no set/deduplication is needed.
                // List<T> also keeps the declared .NET 2.0 target buildable.
                foreach (int s in steps)
                    if (s <= maxKBps * 97 / 100)
                        speeds.Add(s);
                speeds.Add(maxKBps);
                return speeds.ToArray();
            }
            catch { return new int[0]; }
        }

        /// <summary>Raw result of a read-only calibration probe (no rip). All timings in ms.</summary>
        public struct DriveProbe
        {
            public bool Probed;              // a probe actually ran
            public int[] SupportedSpeeds;    // synthesized ladder (kB/s)
            public double FirstReadMs;       // time to read the target region from media
            public double ReReadMs;          // time to read the SAME region again
            public bool CachesReReads;       // re-read much faster => the drive cached it
            public int FlushEvictBytes;      // smallest flush (bytes) that evicts the cache (0 = not caching / not found / flush not tolerated)
            public bool OverreadLeadIn;      // READ CD accepted the sector immediately before track 1
            public bool OverreadLeadOut;     // READ CD accepted the sector immediately after the audio program
        }

        /// <summary>Read-only cache-behaviour probe. Read a mid-disc audio region to warm any cache,
        /// time an immediate re-read (fast if the drive caches), then read a LARGE unrelated region
        /// to flush the cache and time the target again (a real media read if it was evicted). If
        /// the warm re-read is much faster than the post-flush read, the drive serves re-reads from
        /// cache - so a secure re-read must defeat it. Runs OUTSIDE a rip, synchronously; every
        /// ReadCDDA status is checked (a failure => Probed false, honest "Unconfirmed"). Reads audio
        /// sectors only - never writes rip output, cannot corrupt anything.</summary>
        public unsafe DriveProbe Probe(int driveOffsetSamples = 0)
        {
            var r = new DriveProbe { SupportedSpeeds = GetSupportedSpeeds() };
            try
            {
                if (m_device == null || _toc == null || _toc.AudioLength < 8000) return r;   // need room for a flush region
                if (!TestReadCommand()) return r;   // autodetect the drive's working read command/mode

                uint firstStart = (uint)_toc[_toc.FirstAudio][0].Start;
                long len = _toc.AudioLength;
                int chunk = Math.Max(1, m_max_sectors);              // sectors per read command
                int c2Size = _c2ErrorMode == Device.C2ErrorMode.None ? 0 : _c2ErrorMode == Device.C2ErrorMode.Mode294 ? 294 : 296;
                int perSector = 4 * 588 + c2Size;                    // bytes the drive returns per sector
                var buf = new byte[chunk * perSector];

                uint target = firstStart + (uint)(len / 2);          // mid-disc (past the 10-13% pin-holes)
                uint flushBase = firstStart + (uint)(len / 5);       // ~20%, far from the target

                // one read command via the autodetected path (same dispatch the rip uses)
                bool ReadAt(uint lba, uint sectors, out double ms)
                {
                    fixed (byte* p = buf)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        Device.CommandStatus st = _readCDCommand == ReadCDCommand.ReadCdBEh
                            ? m_device.ReadCDAndSubChannel(_mainChannelMode, Device.SubChannelMode.None, _c2ErrorMode, 1, false, lba, sectors, (IntPtr)p, _timeout)
                            : m_device.ReadCDDA(Device.SubChannelMode.None, lba, sectors, (IntPtr)p, _timeout);
                        ms = sw.Elapsed.TotalMilliseconds;
                        return st == Device.CommandStatus.Success;
                    }
                }
                bool ReadOne(uint lba, out double ms) =>
                    ReadAt(lba, (uint)chunk, out ms);

                // warm the cache, then time an immediate re-read (fast if the drive caches)
                if (!ReadOne(target, out _)) return r;
                if (!ReadOne(target, out double warmMs)) return r;

                // flush with a big unrelated read (~6.5 MB, larger than any optical read cache),
                // looping chunks since one command is limited to m_max_sectors
                int flushChunks = (int)(6_500_000L / (chunk * 2352)) + 1;
                for (int i = 0; i < flushChunks; i++)
                    if (!ReadOne(flushBase + (uint)(i * chunk), out _)) return r;

                // time the target again - a real media read if the flush evicted it
                if (!ReadOne(target, out double flushedMs)) return r;

                r.FirstReadMs = flushedMs;   // honest media-read time
                r.ReReadMs = warmMs;         // cached re-read time
                r.CachesReReads = flushedMs > 3 && warmMs < flushedMs * 0.5;

                // Flush-SIZE search at 3 speeds (gentle cache defeat): the smallest flush that evicts,
                // measured at the fastest / middle / slowest supported speed and taking the LARGEST -
                // cache eviction can differ by speed, so one stored size must cover all. Each "does size
                // S evict?" test repeats 3x and requires all three to read at media speed (unanimity errs
                // conservative against jitter). Doubling search then refine to 256 KB, +512 KB margin.
                // Every flush read stays strictly inside the audio program (a past-end read is what throws
                // INVALID FIELD IN CDB). Read-only into scratch; a read error abandons that speed (0).
                if (r.CachesReReads)
                {
                    int maxFlush = 0;
                    var testSpeeds = new System.Collections.Generic.List<int>();
                    void AddSp(int s) { if (s > 0 && !testSpeeds.Contains(s)) testSpeeds.Add(s); }
                    if (r.SupportedSpeeds != null && r.SupportedSpeeds.Length > 0)
                    {
                        AddSp(r.SupportedSpeeds[r.SupportedSpeeds.Length - 1]);   // fastest
                        AddSp(r.SupportedSpeeds[r.SupportedSpeeds.Length / 2]);   // middle
                        AddSp(r.SupportedSpeeds[0]);                             // slowest
                    }
                    else testSpeeds.Add(0);   // unknown ladder -> current speed only

                    foreach (int speed in testSpeeds)
                    {
                        if (speed > 0) { try { ushort sv = (ushort)Math.Min(0xFFFE, speed); m_device.SetCdSpeed(Device.RotationalControl.CLVandNonPureCav, sv, sv); } catch { } }
                        bool bad = false;
                        // per-speed baseline: cached (immediate re-read) vs media (after a 2 MB flush)
                        if (!ReadOne(target, out _)) continue;
                        if (!ReadOne(target, out double cachedMs)) continue;
                        int warmChunks = Math.Max(1, (2 * 1024 * 1024) / (chunk * 2352));
                        for (int i = 0; i < warmChunks && !bad; i++)
                        {
                            uint lba = flushBase + (uint)(i * chunk);
                            if (lba + (uint)chunk > firstStart + (uint)len) break;
                            if (!ReadOne(lba, out _)) bad = true;
                        }
                        if (bad) continue;
                        if (!ReadOne(target, out double mediaMs)) continue;
                        if (mediaMs <= 3 || cachedMs >= mediaMs * 0.5) continue;   // not clearly caching here
                        double threshold = (cachedMs + mediaMs) / 2;

                        bool Evicts(int bytes)
                        {
                            for (int rep = 0; rep < 3; rep++)
                            {
                                if (!ReadOne(target, out _)) { bad = true; return false; }
                                int fc = Math.Max(1, bytes / (chunk * 2352));
                                for (int i = 0; i < fc; i++)
                                {
                                    uint lba = flushBase + (uint)(i * chunk);
                                    if (lba + (uint)chunk > firstStart + (uint)len) break;   // stay in-program
                                    if (!ReadOne(lba, out _)) { bad = true; return false; }
                                }
                                if (!ReadOne(target, out double ms)) { bad = true; return false; }
                                if (ms < threshold) return false;
                            }
                            return true;   // all 3 hit media -> reliably evicts
                        }
                        int lo = 0, hi = 0;
                        for (int s = 256 * 1024; s <= 8 * 1024 * 1024 && !bad; s *= 2) { if (Evicts(s)) { hi = s; break; } lo = s; }
                        while (hi > 0 && !bad && hi - lo > 256 * 1024) { int mid = (lo + hi) / 2; if (Evicts(mid)) hi = mid; else lo = mid; }
                        if (!bad && hi > 0) maxFlush = Math.Max(maxFlush, Math.Min(8 * 1024 * 1024, hi + 512 * 1024));
                    }
                    r.FlushEvictBytes = maxFlush;
                    try { if (r.SupportedSpeeds != null && r.SupportedSpeeds.Length > 0) { ushort sv = (ushort)Math.Min(0xFFFE, r.SupportedSpeeds[r.SupportedSpeeds.Length - 1]); m_device.SetCdSpeed(Device.RotationalControl.CLVandNonPureCav, sv, sv); } } catch { }
                }

                // Probe with the exact command/C2 shape the ripper will use. Boundary probes MUST
                // request one sector: using the normal multi-sector chunk at lead-out asks mostly
                // beyond the readable edge and falsely reports that an overreading drive cannot
                // supply the single sector offset correction needs. Prove the entire offset-derived
                // range (at least one sector for capability display), not merely the nearest sector.
                // Run after the timing-sensitive search so rejected edge addresses cannot perturb it.
                bool ReadBoundaryRange(uint start, int sectors)
                {
                    for (int i = 0; i < sectors; i++)
                        if (!ReadAt(unchecked(start + (uint)i), 1, out _))
                            return false;
                    return true;
                }
                int requiredLeadIn = Math.Max(
                    1,
                    driveOffsetSamples < 0
                        ? (-driveOffsetSamples + 587) / 588
                        : 1);
                int requiredLeadOut = Math.Max(
                    1,
                    driveOffsetSamples > 0
                        ? (driveOffsetSamples + 587) / 588
                        : 1);
                r.OverreadLeadIn = ReadBoundaryRange(
                    unchecked(firstStart - (uint)requiredLeadIn),
                    requiredLeadIn);
                r.OverreadLeadOut = ReadBoundaryRange(
                    firstStart + (uint)len,
                    requiredLeadOut);

                r.Probed = true;
            }
            catch { }
            return r;
        }

        /// <summary>Read-only: find the lowest read speed the drive actually accepts, by stepping SET
        /// CD SPEED down through the slow rungs and doing one probe read at each. Returns kB/s (a
        /// multiple of 176), or 0 if none below max is accepted or probing failed. Uses the
        /// autodetected read command; restores a fast speed on the way out so a normal rip is not left
        /// crawling. Reads audio only - never writes rip output, cannot corrupt anything.</summary>
        public unsafe int ProbeMinSpeedKbps()
        {
            int floor = 0;
            try
            {
                if (m_device == null || _toc == null || _toc.AudioLength < 100) return 0;
                if (!TestReadCommand()) return 0;
                int chunk = Math.Max(1, m_max_sectors);
                int c2Size = _c2ErrorMode == Device.C2ErrorMode.None ? 0 : _c2ErrorMode == Device.C2ErrorMode.Mode294 ? 294 : 296;
                var buf = new byte[chunk * (4 * 588 + c2Size)];
                uint target = (uint)_toc[_toc.FirstAudio][0].Start + (uint)(_toc.AudioLength / 2);

                bool ReadOne(uint lba)
                {
                    fixed (byte* p = buf)
                        return (_readCDCommand == ReadCDCommand.ReadCdBEh
                            ? m_device.ReadCDAndSubChannel(_mainChannelMode, Device.SubChannelMode.None, _c2ErrorMode, 1, false, lba, (uint)chunk, (IntPtr)p, _timeout)
                            : m_device.ReadCDDA(Device.SubChannelMode.None, lba, (uint)chunk, (IntPtr)p, _timeout)) == Device.CommandStatus.Success;
                }

                // step down 8x..0.25x INCLUDING sub-1x rungs (some drives read below 176 kB/s, an even
                // lower floor for the hardest damaged sectors). kB/s directly, not multiples of 176.
                foreach (int kbps in new[] { 1408, 704, 352, 176, 132, 88, 44 })   // 8x,4x,2x,1x,0.75x,0.5x,0.25x
                {
                    ushort v = (ushort)kbps;
                    if (m_device.SetCdSpeed(Device.RotationalControl.CLVandNonPureCav, v, v) != Device.CommandStatus.Success)
                        break;                 // drive refused this speed -> the previous accepted one is the floor
                    if (!ReadOne(target)) break; // could not read at this speed -> stop
                    floor = kbps;
                }
            }
            catch { }
            finally
            {
                try { var s = GetSupportedSpeeds(); if (s.Length > 0) { ushort mx = (ushort)Math.Min(0xFFFE, s[s.Length - 1]); m_device.SetCdSpeed(Device.RotationalControl.CLVandNonPureCav, mx, mx); } } catch { }
            }
            return floor;
        }

        /// <summary>Read-only slip classification: raw-read the window's first chunk a few times and
        /// cross-correlate the extra reads against the first. Strong correlation at a nonzero offset =
        /// real audio shifting between reads (recoverable jitter); strong at offset 0 = identical reads
        /// (cache or stable); weak = no shared signal (dead media). Reads audio only, into its OWN
        /// buffer - does NOT fold into the vote and never modifies rip data. Returns the best
        /// offset and correlation strength (0-100) through out parameters so the declared net20
        /// target does not acquire a System.ValueTuple dependency.</summary>
        private unsafe void ClassifySlip(
            int startSector,
            out int bestOffset,
            out int bestStrengthPct)
        {
            bestOffset = 0;
            bestStrengthPct = 0;
            try
            {
                int chunk = Math.Min(m_max_sectors, _currentEnd - startSector);
                if (chunk < 1) return;
                int c2Size = _c2ErrorMode == Device.C2ErrorMode.None ? 0 : _c2ErrorMode == Device.C2ErrorMode.Mode294 ? 294 : 296;
                int perSector = 4 * 588 + c2Size;
                var buf = new byte[chunk * perSector];
                uint lba = (uint)startSector + (uint)_toc[_toc.FirstAudio][0].Start;

                bool RawRead()
                {
                    fixed (byte* p = buf)
                        return (_readCDCommand == ReadCDCommand.ReadCdBEh
                            ? m_device.ReadCDAndSubChannel(_mainChannelMode, Device.SubChannelMode.None, _c2ErrorMode, 1, false, lba, (uint)chunk, (IntPtr)p, _timeout)
                            : m_device.ReadCDDA(Device.SubChannelMode.None, lba, (uint)chunk, (IntPtr)p, _timeout)) == Device.CommandStatus.Success;
                }
                short[] AudioShorts()
                {
                    var s = new short[chunk * 588 * 2];
                    for (int iSec = 0; iSec < chunk; iSec++)
                        Buffer.BlockCopy(buf, iSec * perSector, s, iSec * 2352, 2352);   // audio bytes only, skip C2
                    return s;
                }

                if (!RawRead()) return;
                short[] reference = AudioShorts();
                int bestPct = 0, bestOff = 0;
                for (int k = 0; k < 3; k++)
                {
                    if (!RawRead()) continue;
                    SlipCorrelationResult correlation =
                        SlipCorrelator.FindOffset(
                            reference,
                            AudioShorts(),
                            32);
                    int pct = (int)(correlation.Strength * 100);
                    if (pct > bestPct)
                    {
                        bestPct = pct;
                        bestOff = correlation.Offset;
                    }
                }
                bestOffset = bestOff;
                bestStrengthPct = bestPct;
            }
            catch
            {
                bestOffset = 0;
                bestStrengthPct = 0;
            }
        }

        public void DisableEjectDisc(bool bDisable)
        {
            if (m_device != null)
                m_device.DisableEjectDisc(bDisable);
            else
            {
                try
                {
                    m_device = new Device(m_logger);
                    if (m_device.Open(m_device_letter))
                    {
                        try
                        {
                            m_device.DisableEjectDisc(bDisable);
                        }
                        finally
                        {
                            m_device.Close();
                        }
                    }
                }
                finally
                {
                    m_device = null;
                }
            }
        }

		bool gapsDetected = false;

		public unsafe bool DetectGaps()
		{
			if (!TestReadCommand())
				throw new ReadCDException(Resource1.AutodetectReadCommandFailed + ":\n" + _autodetectResult);

			if (_gapDetection == GapDetectionMethod.None)
			{
				gapsDetected = false;
				return false;
			}

			if (gapsDetected)
				return true;

			_toc2 = (CDImageLayout)_toc.Clone();

			if (_gapDetection == GapDetectionMethod.ReadSubchannel)
			{
				Device.CommandStatus st = m_device.ReadSubChannel42(2, 0, ref _subchannelBuffer, 0, _timeout);
				if (st == Device.CommandStatus.Success)
					if (_subchannelBuffer[0] == 0 && _subchannelBuffer[2] == 0 && _subchannelBuffer[3] == 20
						&& _subchannelBuffer[4] == 2 && _subchannelBuffer[8] == 0x80)
					{
						string catalog = Encoding.ASCII.GetString(_subchannelBuffer, 9, 13);
						if (catalog.ToString() != "0000000000000")
							_toc2.Barcode = catalog.ToString();
					}
			}

			int sec0 = (int)_toc2[_toc2.FirstAudio][0].Start, disc1 = (int)(_toc2[_toc2.FirstAudio][0].Start + _toc2.AudioLength) - 1;
			for (int iTrack = _toc2.FirstAudio; iTrack < _toc2.FirstAudio + _toc2.AudioTracks; iTrack++)
			{
				if (ReadProgress != null)
				{
					progressArgs.Action = Resource1.StatusDetectingGaps;
					progressArgs.Pass = -1;
					progressArgs.Position = (iTrack - _toc2.FirstAudio) * 3;
					progressArgs.PassStart = 0;
					progressArgs.PassEnd = _toc2.TrackCount * 3 - 1;
					progressArgs.ErrorsCount = 0;
					progressArgs.PassTime = DateTime.Now;
					ReadProgress(this, progressArgs);
				}
				int sec1, idx1 = 1;
				LocateLastSector(sec0, Math.Min(disc1, (int)_toc[iTrack].End + 16), iTrack, -1, ref idx1, out sec1);
				int isec0 = sec0;
				for (int idx = 0; idx <= idx1; idx++)
				{
					int isec1 = sec1, iidx1 = 1;
					if (idx < idx1)
					{
						if (ReadProgress != null)
						{
							progressArgs.Position = (iTrack - _toc2.FirstAudio) * 3 + 1;
							progressArgs.PassTime = DateTime.Now;
							ReadProgress(this, progressArgs);
						}
						LocateLastSector(isec0, sec1, iTrack, idx, ref iidx1, out isec1);
					}
					if (isec1 > isec0)
					{
						if (idx == 0 && iTrack > 1)
							_toc2[iTrack][0].Start = _toc2[iTrack].Start - (uint)(isec1 - isec0 + 1);
						if (idx > 1)
							_toc2[iTrack].AddIndex(new CDTrackIndex((uint)idx, (uint)(_toc2[iTrack][0].Start + isec0 - sec0)));
					}
					isec0 = isec1 + 1;
				}

				if (ReadProgress != null)
				{
					progressArgs.Position = (iTrack - _toc2.FirstAudio) * 3 + 2;
					progressArgs.PassTime = DateTime.Now;
					ReadProgress(this, progressArgs);
				}

				if (_gapDetection == GapDetectionMethod.ReadSubchannel)
				{
					Device.CommandStatus st = m_device.ReadSubChannel42(3, iTrack, ref _subchannelBuffer, 0, _timeout);
					if (st == Device.CommandStatus.Success)
						if (_subchannelBuffer[0] == 0 && _subchannelBuffer[2] == 0 && _subchannelBuffer[3] == 20
							&& _subchannelBuffer[4] == 3 && _subchannelBuffer[8] == 0x80) //&& _subchannelBuffer[6] == iTrack)
						{
							string isrc = Encoding.ASCII.GetString(_subchannelBuffer, 9, 12);
							if (!isrc.ToString().Contains("#") && isrc.ToString() != "000000000000")
								_toc2[iTrack].ISRC = isrc.ToString();
						}
				}
				if (_gapDetection == GapDetectionMethod.ReadCD)
				{
					Device.CommandStatus st = m_device.ReadSubChannel(2, _toc2[iTrack].Start + 16, 100, ref _subchannelBuffer, _timeout);
					if (st == Device.CommandStatus.Success)
					{
						for (int offs = 0; offs < 100 * 16; offs += 16)
						{
							int ctl = _subchannelBuffer[offs + 0] >> 4;
							int adr = _subchannelBuffer[offs + 0] & 7;
							if (adr != 2 && adr != 3)
								continue;
							ushort crc = _crc.ComputeChecksum(_subchannelBuffer, offs, 10);
							crc ^= 0xffff;
							ushort scrc = (ushort)((_subchannelBuffer[offs + 10] << 8) | _subchannelBuffer[offs + 11]);
							if (scrc != 0 && scrc != crc)
								continue;
							if (adr == 3 && _toc2[iTrack].ISRC == null)
							{
								StringBuilder isrc = new StringBuilder();
								isrc.Append(from6bit(_subchannelBuffer[offs + 1] >> 2));
								isrc.Append(from6bit(((_subchannelBuffer[offs + 1] & 0x3) << 4) + (0x0f & (_subchannelBuffer[offs + 2] >> 4))));
								isrc.Append(from6bit(((_subchannelBuffer[offs + 2] & 0xf) << 2) + (0x03 & (_subchannelBuffer[offs + 3] >> 6))));
								isrc.Append(from6bit((_subchannelBuffer[offs + 3] & 0x3f)));
								isrc.Append(from6bit(_subchannelBuffer[offs + 4] >> 2));
								isrc.Append(from6bit(((_subchannelBuffer[offs + 4] & 0x3) << 4) + (0x0f & (_subchannelBuffer[offs + 5] >> 4))));
								isrc.AppendFormat("{0:x}", _subchannelBuffer[offs + 5] & 0xf);
								isrc.AppendFormat("{0:x2}", _subchannelBuffer[offs + 6]);
								isrc.AppendFormat("{0:x2}", _subchannelBuffer[offs + 7]);
								isrc.AppendFormat("{0:x}", _subchannelBuffer[offs + 8] >> 4);
								if (!isrc.ToString().Contains("#") && isrc.ToString() != "000000000000")
									_toc2[iTrack].ISRC = isrc.ToString();
							}
							if (adr == 2 && _toc2.Barcode == null)
							{
								StringBuilder barcode = new StringBuilder();
								for (int i = 1; i < 8; i++)
									barcode.AppendFormat("{0:x2}", _subchannelBuffer[offs + i]);
								if (barcode.ToString(0, 13) != "0000000000000")
									_toc2.Barcode = barcode.ToString(0, 13);
							}
						}
					}
				}
				sec0 = sec1 + 1;
			}

			gapsDetected = true;
			return true;
		}

		bool readCommandFound = false;

		public unsafe bool TestReadCommand()
		{
			if (readCommandFound)
				return true;

			ReadCDCommand[] readmode = { ReadCDCommand.ReadCdBEh, ReadCDCommand.ReadCdD8h };
			Device.C2ErrorMode[] c2mode = { Device.C2ErrorMode.Mode294 };
			if (_driveC2ErrorMode == DriveC2ErrorModeSetting.Auto)
			{
				// Drives can contain one or multiple spaces in the name, e.g. "ASUS DRW-24F1ST   d". Remove any spaces from Path.
				string pathNoSpace = Path.Replace(" ", String.Empty);

				// Mode294 does not work for these drives. Try Mode296 first, which has been reported to work:
				// ASUS DRW-24D5MT, ASUS DRW-24F1ST d,
				// HL-DT-ST BD-RE BU40N, HL-DT-ST BD-RE WH10LS30, HL-DT-ST DVDRAM GH22LS51,
				// HP DH16ACSH,
				// LG GH24NSD1, GH24NSD5,
				// LITEON DH-20A4P,
				// MATSHITA DVD-R UJ-868,
				// PIONEER BD-RW BDR-UD04, BDR-XD05, BDR-XD07U, DVD-RW DVR-S21,
				// PLDS DU-8A5LH, DS-8A9SH
				// Slimtype - DVD A DU8AESH.
				if (pathNoSpace.Contains("DRW-24D5MT") || pathNoSpace.Contains("DRW-24F1STd") ||
					pathNoSpace.Contains("BU40N") || pathNoSpace.Contains("WH10LS30") || pathNoSpace.Contains("GH22LS51") ||
					pathNoSpace.Contains("DH16ACSH") ||
					pathNoSpace.Contains("GH24NSD1") || pathNoSpace.Contains("GH24NSD5") ||
					pathNoSpace.Contains("DH20A4P") ||
					pathNoSpace.Contains("UJ-868") ||
					pathNoSpace.Contains("BDR-UD04") || pathNoSpace.Contains("BDR-XD05") || pathNoSpace.Contains("BDR-XD07U") || pathNoSpace.Contains("DVR-S21") ||
					pathNoSpace.Contains("DU-8A5LH") || pathNoSpace.Contains("DS8A9SH") ||
					pathNoSpace.Contains("DU8AESH"))
				{
					Array.Resize(ref c2mode, 2);
					c2mode.SetValue(Device.C2ErrorMode.Mode296, 0);
					c2mode.SetValue(Device.C2ErrorMode.None, 1);
				}

				// Mode294 does not work for this drive, C2ErrorMode.None has been reported to work:
				// iHAS324 F.
				else if (pathNoSpace.Contains("iHAS324F"))
				{
					c2mode.SetValue(Device.C2ErrorMode.None, 0);
				}

				// Default. Auto detection of C2ErrorMode for most drives.
				// Device.C2ErrorMode[] c2mode = { Device.C2ErrorMode.Mode294, Device.C2ErrorMode.Mode296, Device.C2ErrorMode.None };
				else
				{
					Array.Resize(ref c2mode, 3);
					c2mode.SetValue(Device.C2ErrorMode.Mode296, 1);
					c2mode.SetValue(Device.C2ErrorMode.None, 2);
				}
			}
			else
			{
				// Disable auto-detection of c2mode and override with setting
				c2mode.SetValue((Device.C2ErrorMode)_driveC2ErrorMode, 0);
			}
			Device.MainChannelSelection[] mainmode = { Device.MainChannelSelection.UserData, Device.MainChannelSelection.F8h };
			bool found = false;
			_autodetectResult = "";
			_currentStart = 0;
			m_max_sectors = Math.Min(NSECTORS, m_device.MaximumTransferLength / CB_AUDIO - 1);
			int sector = 3;
			int pass = 0;

			for (int c = 0; c <= c2mode.Length - 1 && !found; c++)
				for (int r = 0; r <= 1 && !found; r++)
					for (int m = 0; m <= 1 && !found; m++)
					{
						_readCDCommand = readmode[r];
						_c2ErrorMode = c2mode[c];
						_mainChannelMode = mainmode[m];
						if (_forceReadCommand != ReadCDCommand.Unknown && _readCDCommand != _forceReadCommand)
							continue;
                        if (_readCDCommand == ReadCDCommand.ReadCdD8h && (_c2ErrorMode != Device.C2ErrorMode.None || _mainChannelMode != Device.MainChannelSelection.UserData))
							continue;
						Array.Clear(_readBuffer, 0, _readBuffer.Length); // fill with something nasty instead?
						DateTime tm = DateTime.Now;
						if (ReadProgress != null)
						{
							progressArgs.Action = Resource1.StatusDetectingDriveFeatures;
							progressArgs.Pass = -1;
							progressArgs.Position = pass++;
							progressArgs.PassStart = 0;
							progressArgs.PassEnd = 2 * 3 * 2 - 1;
							progressArgs.ErrorsCount = 0;
							progressArgs.PassTime = tm;
							ReadProgress(this, progressArgs);
						}
						System.Diagnostics.Trace.WriteLine("Trying " + CurrentReadCommand);
						Device.CommandStatus st = FetchSectors(sector, m_max_sectors, false);
						TimeSpan delay = DateTime.Now - tm;
						_autodetectResult += string.Format("{0}: {1} ({2}ms)\n", CurrentReadCommand, (st == Device.CommandStatus.DeviceFailed ? Device.LookupSenseError(m_device.GetSenseAsc(), m_device.GetSenseAscq()) : st.ToString()), delay.TotalMilliseconds);
						found = st == Device.CommandStatus.Success;
						
						//sector += m_max_sectors;
					}
			//if (found)
			//    for (int n = 1; n <= m_max_sectors; n++)
			//    {
			//        Device.CommandStatus st = FetchSectors(0, n, false, false);
			//        if (st != Device.CommandStatus.Success)
			//        {
			//            _autodetectResult += string.Format("Maximum sectors: {0}, else {1}; max length {2}\n", n - 1, (st == Device.CommandStatus.DeviceFailed ? Device.LookupSenseError(m_device.GetSenseAsc(), m_device.GetSenseAscq()) : st.ToString()), m_device.MaximumTransferLength);
			//            m_max_sectors = n - 1;
			//            break;
			//        }
			//    }
            if (found)
            {
                TestGaps();
                _autodetectResult += "Chosen " + CurrentReadCommand + "\n";
            }
            else
            {
                _gapDetection = GapDetectionMethod.None;
                _readCDCommand = ReadCDCommand.Unknown;
            }

			_currentStart = -1;
			_currentEnd = -1;
			readCommandFound = found;
			return found;
		}

		//int dbg_pass;
		//FileStream fs_d, fs_c;

		// Folds one freshly read pass into the per-sample accumulators used by the secure
		// vote in CorrectSectors. For each byte, byte2long spreads its 8 bits into 8 lanes of
		// a long so bit-level tallies can be summed with one add. Samples the drive's C2
		// pointer flagged as erroneous go into the c2==1 plane (UserData[.,1,.]); clean
		// samples into the c2==0 plane. C2Count tracks how many passes flagged each sample.
		// The Mode296 branch works around drives (e.g. PIONEER 215D) that place the block C2
		// byte after the per-sample C2 bytes instead of before as MMC-6 specifies; offs is
		// derived by testing which layout makes the block byte equal the OR of the rest.
		private unsafe void ReorganiseSectors(int sector, int Sectors2Read)
		{
			int c2Size = _c2ErrorMode == Device.C2ErrorMode.None ? 0 : _c2ErrorMode == Device.C2ErrorMode.Mode294 ? 294 : 296;
			int oldSize = 4 * 588 + c2Size;
			fixed (byte* readBuf = _readBuffer, c2Count = C2Count)
			fixed (long* userData = UserData)
			{
				for (int iSector = 0; iSector < Sectors2Read; iSector++)
				{
					byte* sectorPtr = readBuf + iSector * oldSize;
					long* userDataPtr = userData + (sector - _currentStart + iSector) * 8 * 588;
					byte* c2CountPtr = c2Count + (sector - _currentStart + iSector) * 294;

					//if (_currentStart > 0)
					//{
					//    string nm_d = string.Format("Y:\\Temp\\dbg\\{0:x}-{1:00}.bin", _currentStart, dbg_pass);
					//    string nm_c = string.Format("Y:\\Temp\\dbg\\{0:x}-{1:00}.c2", _currentStart, dbg_pass);
					//    if (fs_d != null && fs_d.Name != nm_d) { fs_d.Close(); fs_d = null; }
					//    if (fs_c != null && fs_c.Name != nm_c) { fs_c.Close(); fs_c = null; }
					//    if (fs_d == null) fs_d = new FileStream(nm_d, FileMode.Create);
					//    if (fs_c == null) fs_c = new FileStream(nm_c, FileMode.Create);
					//    fs_d.Seek((sector - _currentStart + iSector) * 4 * 588, SeekOrigin.Begin);
					//    fs_d.Write(_readBuffer, iSector * oldSize, 4 * 588);
					//    fs_c.Seek((sector - _currentStart + iSector) * 296, SeekOrigin.Begin);
					//    fs_c.Write(_readBuffer, iSector * oldSize + 4 * 588, 296);
					//}

					if (_c2ErrorMode != Device.C2ErrorMode.None)
					{
						int offs = 0;
						if (c2Size == 296)
						{
							// TODO: sometimes (e.g on PIONEER 215D) sector C2 byte is placed after C2 info,
							// not before, like it says in mmc6r02g.pdf !!
							int c2 = 0;
							for (int pos = 2; pos < 294; pos++)
								c2 |= sectorPtr[4 * 588 + pos];
							if (sectorPtr[4 * 588 + 294] == (c2 | sectorPtr[4 * 588 + 0] | sectorPtr[4 * 588 + 1]))
								offs = 0;
							else
								offs = 2;
							// TSSTcorp CDDVDW SH-S203B SB03
							// TSSTcorpCD/DVDW SH-W162C TS12
							//	Both produced Exception("invalid C2 pointers");
							//else if (sectorPtr[4 * 588] == (c2 | sectorPtr[4 * 588 + 294] | sectorPtr[4 * 588 + 295]))
							//    offs = 2;
							//else 
							//    throw new Exception("invalid C2 pointers");
						}
						for (int pos = 0; pos < 294; pos++)
						{
							int c2d = sectorPtr[4 * 588 + pos + offs]; 
							int c2 = ((-c2d) >> 31) & 1;
							c2CountPtr[pos] += (byte)c2;
							int sample = pos << 3;
							userDataPtr[sample + c2 * 4 * 588] += byte2long[sectorPtr[sample]]; sample++;
							userDataPtr[sample + c2 * 4 * 588] += byte2long[sectorPtr[sample]]; sample++;
							userDataPtr[sample + c2 * 4 * 588] += byte2long[sectorPtr[sample]]; sample++;
							userDataPtr[sample + c2 * 4 * 588] += byte2long[sectorPtr[sample]]; sample++;
							userDataPtr[sample + c2 * 4 * 588] += byte2long[sectorPtr[sample]]; sample++;
							userDataPtr[sample + c2 * 4 * 588] += byte2long[sectorPtr[sample]]; sample++;
							userDataPtr[sample + c2 * 4 * 588] += byte2long[sectorPtr[sample]]; sample++;
							userDataPtr[sample + c2 * 4 * 588] += byte2long[sectorPtr[sample]];
						}
					}
					else
					{
						for (int sample = 0; sample < 4 * 588; sample++)
							userDataPtr[sample] += byte2long[sectorPtr[sample]];
					}
				}
			}
		}

		private unsafe void ClearSectors(int sector, int Sectors2Read)
		{
			fixed (long* userData = &UserData[sector - _currentStart, 0, 0])
			fixed (byte* c2Count = &C2Count[sector - _currentStart, 0])
			{
				AudioSamples.MemSet((byte*)userData, 0, 8 * 2 * 4 * 588 * Sectors2Read);
				AudioSamples.MemSet(c2Count, 0, 294 * Sectors2Read);
			}
		}

		private unsafe Device.CommandStatus FetchSectors(int sector, int Sectors2Read, bool abort)
		{
			Device.CommandStatus st;
			// R118: a re-read of known trouble at low speed can exceed the fixed timeout while
			// the drive's servo hunts a slipping zone; the port driver then resets the device
			// and the read dies as IoctlFailed / ERROR_SEM_TIMEOUT, killing the whole job.
			// Extend the timeout for exactly that observed shape; healthy reads keep the tight
			// baseline so a dead drive still fails fast.
			int timeoutSeconds = ReadTimeoutPolicy.SecondsFor(
				_timeout, _appliedSpeedKbps, _currentErrorsCount > 0);
			if (timeoutSeconds != _timeout)
				_extendedTimeoutReadCount++;
			fixed (byte* data = _readBuffer)
			{
				if (_debugMessages)
				{
					int size = (4 * 588 + (_c2ErrorMode == Device.C2ErrorMode.Mode294 ? 294 : _c2ErrorMode == Device.C2ErrorMode.Mode296 ? 296 : 0))
						* (int)Sectors2Read;
					AudioSamples.MemSet(data, 0xff, size);
				}
				if (_readCDCommand == ReadCDCommand.ReadCdBEh)
					st = m_device.ReadCDAndSubChannel(_mainChannelMode, Device.SubChannelMode.None, _c2ErrorMode, 1, false, (uint)sector + _toc[_toc.FirstAudio][0].Start, (uint)Sectors2Read, (IntPtr)((void*)data), timeoutSeconds);
				else
					st = m_device.ReadCDDA(Device.SubChannelMode.None, (uint)sector + _toc[_toc.FirstAudio][0].Start, (uint)Sectors2Read, (IntPtr)((void*)data), timeoutSeconds);
			}

			if (st == Device.CommandStatus.Success)
			{
				// Transport underrun guard (R112): GOOD status with fewer bytes DMA'd than the
				// exact per-sector layout ReorganiseSectors consumes would fold the reused
				// buffer's stale tail into the vote as fresh audio. A short transfer is
				// transport evidence and stays fatal; it is never damaged-media evidence.
				uint expectedBytes = (uint)((4 * 588 +
					(_c2ErrorMode == Device.C2ErrorMode.Mode294 ? 294 :
					 _c2ErrorMode == Device.C2ErrorMode.Mode296 ? 296 : 0)) * Sectors2Read);
				if (m_device.LastDataTransferLength != expectedBytes)
				{
					_shortPayloadTransferCount++;
					throw new ReadCDException(string.Format(
						"READ CD returned GOOD status but transferred {0} of {1} bytes " +
						"[relative-sector={2}, sectors={3}, command={4}]",
						m_device.LastDataTransferLength,
						expectedBytes,
						sector,
						Sectors2Read,
						_readCDCommand));
				}
				ReorganiseSectors(sector, Sectors2Read);
				return st;
			}

			if (!abort)
				return st;
			string readContext = string.Format(
				"{0} [relative-sector={1}, sectors={2}, command={3}, speed={4}kB/s, speed-transition={5}, cache-transition={6}]",
				Resource1.ReadCDError,
				sector,
				Sectors2Read,
				_readCDCommand,
				_appliedSpeedKbps,
				_speedChangeJustApplied,
				_cacheDefeatJustFlushed);
			SCSIException ex = new SCSIException(readContext, m_device, st);
			Device.SenseKeyType senseKey = ex.SenseKey;
			byte asc = ex.Asc;
			byte ascq = ex.Ascq;
			bool mediumError =
				PayloadReadFailurePolicy.IsMediumError(st, senseKey);
			bool legacyTrackMode =
				sector != 0 && st == Device.CommandStatus.DeviceFailed &&
				asc == 0x64 && ascq == 0x00;

			if (Sectors2Read == 1 && mediumError)
			{
				MarkSectorUnreadable(sector);
				return Device.CommandStatus.Success;
			}

			if (PayloadReadFailurePolicy.ShouldDecomposeRejectedPayloadBatch(
				_speedChangeJustApplied || _cacheDefeatJustFlushed,
				Sectors2Read,
				st,
				senseKey,
				asc,
				ascq))
			{
				if (_debugMessages)
					System.Console.WriteLine(
						"\n{0}: retrying the rejected batch one sector at a time",
						ex.Message);
				for (int iSector = 0; iSector < Sectors2Read; iSector++)
				{
					int singleSector = sector + iSector;
					Device.CommandStatus singleStatus =
						FetchSectors(singleSector, 1, false);
					if (singleStatus != Device.CommandStatus.Success)
					{
						// A rejected transfer shape says nothing about the trustworthiness
						// of any sector. Snapshot the exact child sense before another command
						// overwrites the device buffer, then accept only a successful retry or
						// explicit untrusted-sector evidence.
						string singleContext = string.Format(
							"{0} [relative-sector={1}, sectors=1, command={2}, speed={3}kB/s, speed-transition={4}, cache-transition={5}, batch-fallback=True, parent-batch-sector={6}, parent-batch-sectors={7}, parent-batch-sense={8}]",
							Resource1.ReadCDError,
							singleSector,
							_readCDCommand,
							_appliedSpeedKbps,
							_speedChangeJustApplied,
							_cacheDefeatJustFlushed,
							sector,
							Sectors2Read,
							senseKey);
						SCSIException singleFailure = new SCSIException(
							singleContext,
							m_device,
							singleStatus);

						if (PayloadReadFailurePolicy.IsMediumError(
							singleFailure.Status,
							singleFailure.SenseKey))
						{
							MarkSectorUnreadable(singleSector);
							continue;
						}

						if (PayloadReadFailurePolicy
							.ShouldRetryPinpointAfterRejectedPayloadBatch(
								Sectors2Read,
								st,
								senseKey,
								asc,
								ascq,
								singleFailure.Status,
								singleFailure.SenseKey,
								singleFailure.Asc,
								singleFailure.Ascq))
						{
							Thread.Sleep(80);
							_pinpointRetryCount++;
							Device.CommandStatus repeatedStatus =
								FetchSectors(singleSector, 1, false);
							if (repeatedStatus == Device.CommandStatus.Success)
								continue;

							string repeatedContext = string.Format(
								"{0} [relative-sector={1}, sectors=1, command={2}, speed={3}kB/s, speed-transition={4}, cache-transition={5}, batch-fallback=True, parent-batch-sector={6}, parent-batch-sectors={7}, parent-batch-sense={8}, pinpoint-retry=True]",
								Resource1.ReadCDError,
								singleSector,
								_readCDCommand,
								_appliedSpeedKbps,
								_speedChangeJustApplied,
								_cacheDefeatJustFlushed,
								sector,
								Sectors2Read,
								senseKey);
							SCSIException repeatedFailure = new SCSIException(
								repeatedContext,
								m_device,
								repeatedStatus);
							if (PayloadReadFailurePolicy.IsMediumError(
									repeatedFailure.Status,
									repeatedFailure.SenseKey) ||
								PayloadReadFailurePolicy
									.IsCorroboratedRejectedBatchPinpoint(
										Sectors2Read,
										st,
										senseKey,
										asc,
										ascq,
										singleFailure.Status,
										singleFailure.SenseKey,
										singleFailure.Asc,
										singleFailure.Ascq,
										repeatedFailure.Status,
										repeatedFailure.SenseKey,
										repeatedFailure.Asc,
										repeatedFailure.Ascq))
							{
								_corroboratedUnreadablePinpointCount++;
								MarkSectorUnreadable(singleSector);
								continue;
							}
							throw repeatedFailure;
						}
						throw singleFailure;
					}
				}
				_payloadBatchFallbackCount++;
				return Device.CommandStatus.Success;
			}

			if (Sectors2Read > 1 &&
				PayloadReadFailurePolicy.ShouldSplitBatch(
					st,
					senseKey,
					asc,
					ascq) &&
				(mediumError || legacyTrackMode))
			{
				if (_debugMessages)
					System.Console.WriteLine("\n{0}: retrying one sector at a time", ex.Message);
				int iErrors = 0;
				for (int iSector = 0; iSector < Sectors2Read; iSector++)
				{
					Device.CommandStatus singleStatus =
						FetchSectors(sector + iSector, 1, false);
					if (singleStatus != Device.CommandStatus.Success)
					{
						int singleSector = sector + iSector;
						string singleContext = string.Format(
							"{0} [relative-sector={1}, sectors=1, command={2}, speed={3}kB/s, speed-transition={4}, cache-transition={5}, parent-batch-sector={6}, parent-batch-sectors={7}, parent-batch-sense={8}]",
							Resource1.ReadCDError,
							singleSector,
							_readCDCommand,
							_appliedSpeedKbps,
							_speedChangeJustApplied,
							_cacheDefeatJustFlushed,
							sector,
							Sectors2Read,
							senseKey);
						// Snapshot sense immediately. The next SCSI command overwrites the
						// device's shared sense buffer, and the exact pinpoint failure decides
						// whether this is damaged media or a fatal device/command failure.
						SCSIException singleFailure =
							new SCSIException(singleContext, m_device, singleStatus);
						if (PayloadReadFailurePolicy.ShouldRetryPinpointAfterMediumBatch(
							mediumError,
							Sectors2Read,
							singleFailure.Status,
							singleFailure.SenseKey,
							singleFailure.Asc,
							singleFailure.Ascq))
						{
							// The batch established media trouble, while this one-sector
							// command succeeds elsewhere in the same valid TOC range. Retry
							// once after a bounded settle and use only successful payload.
							Thread.Sleep(80);
							_pinpointRetryCount++;
							Device.CommandStatus repeatedStatus =
								FetchSectors(singleSector, 1, false);
							if (repeatedStatus == Device.CommandStatus.Success)
								continue;

							string repeatedContext = string.Format(
								"{0} [relative-sector={1}, sectors=1, command={2}, speed={3}kB/s, speed-transition={4}, cache-transition={5}, parent-batch-sector={6}, parent-batch-sectors={7}, parent-batch-sense={8}, pinpoint-retry=True]",
								Resource1.ReadCDError,
								singleSector,
								_readCDCommand,
								_appliedSpeedKbps,
								_speedChangeJustApplied,
								_cacheDefeatJustFlushed,
								sector,
								Sectors2Read,
								senseKey);
							SCSIException repeatedFailure =
								new SCSIException(
									repeatedContext,
									m_device,
									repeatedStatus);
							if (PayloadReadFailurePolicy.IsMediumError(
									repeatedFailure.Status,
									repeatedFailure.SenseKey) ||
								PayloadReadFailurePolicy.IsCorroboratedUnreadablePinpoint(
									mediumError,
									Sectors2Read,
									singleFailure.Status,
									singleFailure.SenseKey,
									singleFailure.Asc,
									singleFailure.Ascq,
									repeatedFailure.Status,
									repeatedFailure.SenseKey,
									repeatedFailure.Asc,
									repeatedFailure.Ascq))
							{
								// Neither failed command supplied trusted bytes. Feed this
								// exact sector into the normal vote/retry/CTDB path.
								_corroboratedUnreadablePinpointCount++;
								iErrors++;
								MarkSectorUnreadable(singleSector);
								continue;
							}
							throw repeatedFailure;
						}
						if (PayloadReadFailurePolicy.IsDriveReportedTimeoutPinpoint(
								mediumError,
								Sectors2Read,
								1,
								_speedChangeJustApplied,
								_cacheDefeatJustFlushed,
								singleFailure.Status,
								singleFailure.SenseKey,
								singleFailure.Asc,
								singleFailure.Ascq))
						{
							// The drive itself surrendered on this exact address (3E/02) after
							// the extended timeout, and the parent batch independently proved
							// media trouble. That is per-sector media evidence, not a device
							// death: mark it untrusted and keep the job alive (R118).
							_driveReportedTimeoutPinpointCount++;
							iErrors++;
							MarkSectorUnreadable(singleSector);
							continue;
						}
						if (!PayloadReadFailurePolicy.MayMarkSplitChildUnreadable(
								mediumError,
								legacyTrackMode,
								singleStatus,
								singleFailure.SenseKey,
								singleFailure.Asc,
								singleFailure.Ascq))
						{
							// A batch-level fault may expose a different failure on the pinpoint
							// read. Do not mislabel device removal, transport, readiness, command,
							// or hardware failures as damage that CTDB can repair - under the
							// medium-error parent AND the legacy 64/00 parent, whose children may
							// only repeat the exact 64/00 track fault (mixed-mode discs) (R114).
							throw singleFailure;
						}
						iErrors ++;
						MarkSectorUnreadable(singleSector);
						if (_debugMessages)
							System.Console.WriteLine("\nSector lost");
					}
				}
				// A medium error is media evidence even when every sector in this small
				// transfer failed. Keep those sectors in the existing vote/retry pipeline;
				// StopOnUnrecoverable decides whether the job stops after retries exhaust.
				// Preserve the legacy 64/00 behavior, which required at least one readable
				// sector to prove that the command still worked in this region.
				if (mediumError || iErrors < Sectors2Read)
					return Device.CommandStatus.Success;
			}
			throw ex;
		}

		private void MarkSectorUnreadable(int sector)
		{
			for (int i = 0; i < 294; i++)
				C2Count[sector - _currentStart, i]++;
		}

		// The secure-ripping decision. For each sample byte it runs a weighted majority vote
		// over all passes read so far: a C2-clean read of a bit counts c2div (128) times, a
		// C2-flagged read counts once, so clean reads dominate but flagged reads still break
		// ties. sig = sum >> 31 extracts the winning bit from the sign of the vote; bestValue
		// reassembles the byte. A sample is only trusted when its winning margin clears
		// er_limit = 128 * (1 + correctionQuality) - 1, i.e. it needs at least
		// (1 + correctionQuality) clean agreeing passes. Samples that fall short set fError,
		// which bumps m_retryCount so the sector is read again; sectors that never converge
		// within (16 << correctionQuality)+1 passes end up in m_failedSectors. This is the
		// algorithm TestRipper exercises offline. Changing c2div, er_limit, or the sign trick
		// changes which rips are declared secure.
		private unsafe void CorrectSectors(int pass, int sector, int Sectors2Read)
		{
			for (int iSector = 0; iSector < Sectors2Read; iSector++)
			{
				int pos = sector - _currentStart + iSector;
				int retryIndex = sector + iSector - _retrySectorBase;
				// avg - pass + 1
				// p  a  l  o
				// 0  1  1  2
				// 2  4  3  3
				// 4  7  2  4
				// 6 10     5
				//16 25    10
				// need at least 1 + _correctionQuality good passes. The pure helper is
				// deliberately the shipping implementation exercised by TestRipper; the old test
				// carried a copied, stale variant that ignored the C2 vote plane.
				bool fError = SecureSectorVote.CorrectSector(
					UserData,
					C2Count,
					currentData.Bytes,
					pos,
					pass + 1,
					_correctionQuality);
                
                if (fError) _thisPassErrors++;   // diagnostic (read-only): sector flagged on THIS pass

                if (pass > _correctionQuality || fError)
                {
                    int olderr = pass > _correctionQuality && m_retryCount[retryIndex] == pass + 1 ? 1 : 0;
                    int newerr = fError ? 1 : 0;
                    _currentErrorsCount += newerr - olderr;
                    if (fError) m_retryCount[retryIndex] = (byte)(pass + 2);
                }
			}
		}

		private void ReadFailureIdentity(
			Device.CommandStatus status,
			out Device.SenseKeyType senseKey,
			out byte asc,
			out byte ascq)
		{
			senseKey = Device.SenseKeyType.NoSense;
			asc = 0;
			ascq = 0;
			if (status != Device.CommandStatus.DeviceFailed)
				return;
			ReadCurrentSense(out senseKey, out asc, out ascq);
		}

		private void ReadCurrentSense(
			out Device.SenseKeyType senseKey,
			out byte asc,
			out byte ascq)
		{
			senseKey = Device.SenseKeyType.NoSense;
			asc = 0;
			ascq = 0;
			try
			{
				senseKey = m_device.GetSenseKey();
				asc = m_device.GetSenseAsc();
				ascq = m_device.GetSenseAscq();
			}
			catch
			{
				// Missing or malformed sense data never authorizes a retry. The caller
				// preserves the status and tries only other already-bounded regions.
			}
		}

		private bool TryWakeCacheDefeatDrive(
			ref int aggregateReadinessRetries,
			ref int aggregateIndeterminateReadiness,
			out string failure)
		{
			Device.CommandStatus status = m_device.StartStopUnit(
				false,
				Device.PowerControl.NoChange,
				Device.StartState.StartDisk);
			if (status != Device.CommandStatus.Success)
			{
				ReadFailureIdentity(
					status,
					out Device.SenseKeyType senseKey,
					out byte asc,
					out byte ascq);
				failure =
					$"wake-command=StartDisk, status={status}, " +
					$"sense={senseKey}, ASC={asc:X2}, ASCQ={ascq:X2}";
				return false;
			}

			// Although START UNIT is non-immediate, the ASUS firmware can return
			// command success before its readiness CDB path leaves the same 24/00
			// transition state. Settle before the first query.
			Thread.Sleep(250);
			int retriesForTransition = 0;
			while (true)
			{
				status = m_device.TestUnitReady(out bool ready);
				if (status == Device.CommandStatus.Success && ready)
				{
					failure = null;
					return true;
				}

				Device.SenseKeyType senseKey;
				byte asc;
				byte ascq;
				if (status == Device.CommandStatus.DeviceFailed)
					ReadFailureIdentity(
						status,
						out senseKey,
						out asc,
						out ascq);
				else
					ReadCurrentSense(
						out senseKey,
						out asc,
						out ascq);
				failure =
					$"wake-command=TestUnitReady, status={status}, " +
					$"ready={ready}, readiness-attempt={retriesForTransition + 1}, " +
					$"sense={senseKey}, " +
					$"ASC={asc:X2}, ASCQ={ascq:X2}";

				if (PayloadReadFailurePolicy
					.ShouldRetryCacheDefeatWakeReadiness(
						retriesForTransition,
						status,
						senseKey,
						asc,
						ascq))
				{
					Thread.Sleep(250);
					retriesForTransition++;
					aggregateReadinessRetries++;
					_cacheDefeatWakeReadinessRetryCount++;
					continue;
				}

				if (PayloadReadFailurePolicy
					.ShouldAttemptCacheDefeatProofAfterIndeterminateReadiness(
						retriesForTransition,
						status,
						senseKey,
						asc,
						ascq))
				{
					// This does not treat the unit as ready. The caller repeats the
					// complete eviction, which is the only operation that can prove
					// both media access and cache independence.
					aggregateIndeterminateReadiness++;
					_cacheDefeatWakeReadinessIndeterminateCount++;
					failure = null;
					return true;
				}

				return false;
			}
		}

		private Device.CommandStatus IssueCacheDefeatRead(
			uint lba,
			int sectorCount,
			IntPtr scratch)
		{
			return _readCDCommand == ReadCDCommand.ReadCdBEh
				? m_device.ReadCDAndSubChannel(
					_mainChannelMode,
					Device.SubChannelMode.None,
					_c2ErrorMode,
					1,
					false,
					lba,
					(uint)sectorCount,
					scratch,
					_timeout)
				: m_device.ReadCDDA(
					Device.SubChannelMode.None,
					lba,
					(uint)sectorCount,
					scratch,
					_timeout);
		}

		private Device.CommandStatus ReadCacheDefeatChunk(
			int relativeSector,
			uint lba,
			int sectorCount,
			int chunkShape,
			IntPtr scratch,
			ref int aggregateRetries,
			out Device.SenseKeyType senseKey,
			out byte asc,
			out byte ascq,
			out string failure)
		{
			int retriesForCommand = 0;
			Device.CommandStatus status =
				IssueCacheDefeatRead(lba, sectorCount, scratch);
			failure = null;
			senseKey = Device.SenseKeyType.NoSense;
			asc = 0;
			ascq = 0;

			while (status != Device.CommandStatus.Success)
			{
				ReadFailureIdentity(status, out senseKey, out asc, out ascq);
				if (retriesForCommand == 0)
				{
					failure =
						$"relative-sector={relativeSector}, sectors={sectorCount}, " +
						$"chunk-shape={chunkShape}, command={_readCDCommand}, " +
						$"main={_mainChannelMode}, c2={_c2ErrorMode}, " +
						$"speed={_appliedSpeedKbps}kB/s, " +
						$"current-window={_currentStart}-{_currentEnd}, " +
						$"status={status}, sense={senseKey}, " +
						$"ASC={asc:X2}, ASCQ={ascq:X2}";
				}
				else
				{
					failure =
						$"relative-sector={relativeSector}, sectors={sectorCount}, " +
						$"chunk-shape={chunkShape}, command={_readCDCommand}, " +
						$"main={_mainChannelMode}, c2={_c2ErrorMode}, " +
						$"speed={_appliedSpeedKbps}kB/s, " +
						$"current-window={_currentStart}-{_currentEnd}, " +
						$"retry-status={status}, retry-sense={senseKey}, " +
						$"retry-ASC={asc:X2}, retry-ASCQ={ascq:X2}";
				}

				if (!PayloadReadFailurePolicy.ShouldRetryCacheDefeatRead(
					retriesForCommand,
					status,
					senseKey,
					asc,
					ascq))
					return status;

				// This settle belongs to this exact LBA and transfer shape. A different
				// address or smaller chunk creates a fresh one-retry scope.
				Thread.Sleep(80);
				retriesForCommand++;
				aggregateRetries++;
				_cacheDefeatRetryCount++;
				status = IssueCacheDefeatRead(lba, sectorCount, scratch);
			}

			return status;
		}

		// Cache defeat: read _cacheDefeatBytes of an unrelated in-program region into scratch to evict
		// the current window from the drive cache before a secure re-read, so the re-read hits media (a
		// genuine second read) instead of the cached first read. Bounded strictly inside the audio
		// program (a past-end read is what throws INVALID FIELD IN CDB). The operation is complete or
		// explicit: silently accepting a partial flush would let Test & Copy claim two independent
		// reads without proving that the second reached the disc. Scratch-only; never touches output.
		private unsafe bool FlushCache()
		{
			if (_cacheDefeatBytes <= 0 || _toc == null) return true;
			_cacheDefeatJustFlushed = false;
			string failure = "no usable in-program cache-defeat region";
			int attemptedRegions = 0;
			int transientRetries = 0;
			int chunkFallbacks = 0;
			int wakeAttempts = 0;
			int wakeReadinessRetries = 0;
			int wakeReadinessIndeterminate = 0;
			try
			{
				int preferredChunk = Math.Max(1, m_max_sectors);
				int c2Size = _c2ErrorMode == Device.C2ErrorMode.None ? 0 : _c2ErrorMode == Device.C2ErrorMode.Mode294 ? 294 : 296;
				int perSector = 4 * 588 + c2Size;
				if (_flushScratch == null || _flushScratch.Length < preferredChunk * perSector) _flushScratch = new byte[preferredChunk * perSector];
				uint firstStart = (uint)_toc[_toc.FirstAudio][0].Start;
				uint len = (uint)_toc.AudioLength;
				int sectorsNeeded =
					PayloadReadFailurePolicy.RequiredCacheDefeatSectors(
						_cacheDefeatBytes);
				if (len > int.MaxValue || sectorsNeeded > (long)len)
				{
					failure =
						$"required-sectors={sectorsNeeded}, audio-program-sectors={len}";
				}
				else
				{
					int programSectors = (int)len;
					int currentMiddle =
						_currentStart +
						Math.Max(0, _currentEnd - _currentStart) / 2;
					// Try several well-separated regions, farthest first. A scratch/read
					// defect in one part of a damaged disc must not make cache defeat
					// unusable when another clean region can evict the same cache. Every
					// candidate remains wholly inside the audio program.
					int* candidates = stackalloc int[3];
					candidates[0] = programSectors / 10;
					candidates[1] = programSectors / 2;
					candidates[2] = (int)((long)programSectors * 8L / 10L);
					for (int i = 0; i < 2; i++)
						for (int j = i + 1; j < 3; j++)
							if (Math.Abs(candidates[j] - currentMiddle) >
								Math.Abs(candidates[i] - currentMiddle))
							{
								int swap = candidates[i];
								candidates[i] = candidates[j];
								candidates[j] = swap;
							}
					fixed (byte* p = _flushScratch)
					{
						bool retryAfterWake;
						do
						{
							retryAfterWake = false;
							bool exhaustedInvalidFieldShapes = false;
							int cacheChunk = preferredChunk;
							while (cacheChunk > 0)
							{
								bool sawFailure = false;
								bool everyFailureWasInvalidShape = true;
								for (int candidateIndex = 0; candidateIndex < 3; candidateIndex++)
								{
									int candidate = candidates[candidateIndex];
									int relBase = Math.Max(
										0,
										Math.Min(programSectors - sectorsNeeded, candidate));
									int relEnd = relBase + sectorsNeeded;
									bool overlapsCurrent =
										relBase < _currentEnd && relEnd > _currentStart;
									if (overlapsCurrent) continue;
									attemptedRegions++;
									bool complete = true;
									for (int offset = 0; offset < sectorsNeeded; offset += cacheChunk)
									{
										int readCount = Math.Min(
											cacheChunk,
											sectorsNeeded - offset);
										uint lba =
											firstStart + (uint)(relBase + offset);
										if (lba + (uint)readCount > firstStart + len)
										{
											failure =
												$"relative-sector={relBase + offset}, " +
												$"sectors={readCount}, outside audio program";
											complete = false;
											break;
										}
										Device.CommandStatus st = ReadCacheDefeatChunk(
											relBase + offset,
											lba,
											readCount,
											cacheChunk,
											(IntPtr)p,
											ref transientRetries,
											out Device.SenseKeyType senseKey,
											out byte asc,
											out byte ascq,
											out string readFailure);
										if (st != Device.CommandStatus.Success)
										{
											failure = readFailure;
											sawFailure = true;
											everyFailureWasInvalidShape &=
												PayloadReadFailurePolicy
													.IsCacheDefeatInvalidField(
														st,
														senseKey,
														asc,
														ascq);
											complete = false;
											break;
										}
									}
									if (complete)
									{
										_cacheDefeatJustFlushed = true;
										return true;
									}
								}
								if (!sawFailure || !everyFailureWasInvalidShape)
									break;
								int nextChunk =
									PayloadReadFailurePolicy.NextCacheDefeatChunkSize(
										cacheChunk);
								if (nextChunk == 0)
								{
									exhaustedInvalidFieldShapes = true;
									break;
								}
								cacheChunk = nextChunk;
								chunkFallbacks++;
								_cacheDefeatChunkFallbackCount++;
							}

							if (PayloadReadFailurePolicy
								.ShouldAttemptCacheDefeatWake(
									wakeAttempts,
									exhaustedInvalidFieldShapes))
							{
								wakeAttempts++;
								_cacheDefeatWakeCount++;
								if (TryWakeCacheDefeatDrive(
									ref wakeReadinessRetries,
									ref wakeReadinessIndeterminate,
									out string wakeFailure))
									retryAfterWake = true;
								else
									failure = wakeFailure;
							}
						}
						while (retryAfterWake);
					}
				}
			}
			catch (Exception ex)
			{
				failure = "exception=" + ex.GetType().Name;
			}
			_cacheDefeatFailure =
				$"{failure}; attempted-regions={attemptedRegions}; " +
				$"transient-retries={transientRetries}; " +
				$"retry-scope=per-command; max-retries-per-command=1; " +
				$"chunk-fallbacks={chunkFallbacks}; " +
				$"wake-attempts={wakeAttempts}; max-wake-attempts=1; " +
				$"wake-readiness-retries={wakeReadinessRetries}; " +
				$"max-wake-readiness-retries=1; " +
				$"wake-readiness-indeterminate={wakeReadinessIndeterminate}; " +
				$"max-wake-readiness-indeterminate=1";
			return false;
		}

		public unsafe void PrefetchSector(int iSector)
		{
			if (iSector >= _currentStart && iSector < _currentEnd)
				return;

			if (!TestReadCommand())
				throw new ReadCDException(Resource1.AutodetectReadCommandFailed + "\n" + _autodetectResult);

			int leadInSectors = _overreadLeadIn && _driveOffset < 0
				? (-_driveOffset + 587) / 588
				: 0;
			int leadOutSectors = _overreadLeadOut && _driveOffset > 0
				? (_driveOffset + 587) / 588
				: 0;
			int minSector = -leadInSectors;
			int maxSector = (int)_toc.AudioLength + leadOutSectors;
			if (iSector < minSector || iSector >= maxSector)
				throw new ReadCDException("Read requested outside the calibrated overread range.");

			_currentStart = iSector;
			_currentEnd = _currentStart + MSECTORS;
			if (_currentEnd > maxSector)
			{
				_currentEnd = maxSector;
				_currentStart = Math.Max(minSector, _currentEnd - MSECTORS);
			}

			int neededSize = (_currentEnd - _currentStart) * 588;
			if (currentData.Size < neededSize)
				currentData.Prepare(new byte[neededSize * 4], neededSize);
			currentData.Length = neededSize;

			//FileStream correctFile = new FileStream("correct.wav", FileMode.Open);
			//byte[] realData = new byte[MSECTORS * 4 * 588];
			//correctFile.Seek(0x2C + _currentStart * 588 * 4, SeekOrigin.Begin);
			//if (correctFile.Read(realData, _driveOffset * 4, MSECTORS * 4 * 588 - _driveOffset * 4) != MSECTORS * 4 * 588 - _driveOffset * 4)
			//    throw new Exception("read");
			//correctFile.Close();

			_currentErrorsCount = 0;
			_slipClassified = false; _slipStreak = 0; _slipVerdictPending = false;   // deep recovery: fresh slip state per window

			// Adaptive read speed: apply a pending request HERE and only here - a fresh-window
			// boundary, on the read thread, after TestReadCommand and before any FetchSectors of
			// this window. Issuing SET CD SPEED from the ReadProgress callback (which fires BETWEEN
			// FetchSectors passes of an in-flight window) made the next READ CD fail with
			// "illegal request: INVALID FIELD IN CDB" on an ASUS BW-16D1HT and crashed the rip.
			// This is also exactly where the upstream author left speed control (the commented
			// SetCdSpeed that used to sit here). An accepted command still needs a short settle:
			// this ASUS firmware has intermittently returned 24/00 on the immediately following
			// READ CD even though the identical payload succeeds on other runs. One exact,
			// transition-bound retry below handles that state; a repeated rejection stays fatal.
			int wantSpeed = _pendingSpeedKbps;
			if (wantSpeed > 0 && wantSpeed != _appliedSpeedKbps)
			{
				try
				{
					ushort v = (ushort)Math.Min(0xFFFE, wantSpeed);
					if (m_device.SetCdSpeed(Device.RotationalControl.CLVandNonPureCav, v, v) == Device.CommandStatus.Success)
					{
						_appliedSpeedKbps = wantSpeed;
						_speedChangeJustApplied = true;
						Thread.Sleep(40);
					}
					else
					{
						_pendingSpeedKbps = 0;   // drive refused: keep its current speed, stop asking
						_speedChangeJustApplied = false;
					}
				}
				catch
				{
					_pendingSpeedKbps = 0;
					_speedChangeJustApplied = false;
				}
			}

			// TODO:
			//int max_scans = 1 << _correctionQuality;
			int max_scans = 16 << _correctionQuality;
			// Deep recovery: the blind cap becomes progress-aware. RecoveryPolicy keeps us going while
			// the error count is still improving and stops on a plateau or a time ceiling. When deep
			// recovery is off, hard_cap stays at max_scans and the policy is never consulted, so this
			// loop is identical to before. MaxPasses bounds the extension because every per-sector
			// vote accumulator is 8-bit (R107): an unbounded window would carry bit lanes into
			// their neighbors and wrap C2Count, flipping votes with confident margins.
			// Decision D9: deep recovery is a Secure/Paranoid capability. Burst (quality 0)
			// keeps only the historical 16-pass cap so it stays "fast until trouble"; the
			// progress-aware extension, the slip probe, and the floor-speed grind never run
			// at quality 0.
			bool deepRecoveryActive = DeepRecovery && _correctionQuality > 0;
			int hard_cap = deepRecoveryActive ? RecoveryPolicy.MaxPasses : max_scans;
			DateTime recoveryStart = DateTime.Now;
			if (deepRecoveryActive) _recovery.StartWindow();
			int lastExecutedPass = -1;
			for (int pass = 0; pass < hard_cap; pass++)
			{
				lastExecutedPass = pass;
//				dbg_pass = pass;
				_thisPassErrors = 0;   // diagnostic (read-only): reset the fresh per-pass error count
				// cache defeat: on pass >= 1 (a re-read), evict the window first so this read hits media,
				// not the cached copy of pass 0 (which would make the secure comparison meaningless)
				if (_cacheDefeatBytes > 0 && pass >= 1 && !FlushCache())
					throw new ReadCDException(
						"Drive-cache flush failed; the secure re-read could not be proven independent. " +
						_cacheDefeatFailure);
				DateTime PassTime = DateTime.Now, LastFetch = DateTime.Now;

				for (int sector = _currentStart; sector < _currentEnd; sector += m_max_sectors)
				{
					int Sectors2Read = Math.Min(m_max_sectors, _currentEnd - sector);
					int speed = pass == 5 ? 300 : pass == 6 ? 150 : pass == 7 ? 75 : 32500; // sectors per second
					int msToSleep = 1000 * Sectors2Read / speed - (int)((DateTime.Now - LastFetch).TotalMilliseconds);

					//if (msToSleep > 0) Thread.Sleep(msToSleep);

					LastFetch = DateTime.Now;
					if (pass == 0) 
						ClearSectors(sector, Sectors2Read);
					try
					{
						FetchSectors(sector, Sectors2Read, true);
					}
					catch (SCSIException readFailure) when (
						PayloadReadFailurePolicy.ShouldRetryAfterControlTransition(
							_speedChangeJustApplied,
							readFailure.Status,
							readFailure.SenseKey,
							readFailure.Asc,
							readFailure.Ascq))
					{
						// The SET CD SPEED command completed, but the firmware had not made
						// the payload path ready. Retry the exact same READ CD once after a
						// second bounded settle. The policy excludes every other failure.
						Thread.Sleep(80);
						_controlTransitionRetryCount++;
						FetchSectors(sector, Sectors2Read, true);
						_speedChangeJustApplied = false;
					}
					catch (SCSIException readFailure) when (
						PayloadReadFailurePolicy
							.ShouldRetryAfterCacheDefeatTransition(
								_cacheDefeatJustFlushed,
								readFailure.Status,
								readFailure.SenseKey,
								readFailure.Asc,
								readFailure.Ascq))
					{
						// Some optical firmware briefly rejects the first payload CDB after the long
						// seek used to evict its cache. One bounded retry after a short settle preserves
						// independence (the cache is already evicted) without masking persistent CDB
						// errors or looping forever.
						Thread.Sleep(40);
						FetchSectors(sector, Sectors2Read, true);
						_cacheDefeatJustFlushed = false;
					}
					catch (SCSIException readFailure) when (
						PayloadReadFailurePolicy
							.ShouldRetryObservedReadCommunicationFailure(
								0,
								IsObservedReadCommunicationDrive,
								_readCDCommand == ReadCDCommand.ReadCdBEh,
								Sectors2Read,
								_speedChangeJustApplied,
								_cacheDefeatJustFlushed,
								readFailure.Status,
								readFailure.SenseKey,
								readFailure.Asc,
								readFailure.Ascq))
					{
						// This command-local retry is intentionally outside FetchSectors:
						// pinpoint, decomposition, and cache-eviction reads must not inherit it.
						// Fetch without abort processing so a failed retry cannot enter the
						// damaged-sector classifier under a different or overwritten identity.
						Thread.Sleep(80);
						_readCommunicationRetryCount++;
						Device.CommandStatus repeatedStatus =
							FetchSectors(sector, Sectors2Read, false);
						if (repeatedStatus != Device.CommandStatus.Success)
						{
							string repeatedContext = string.Format(
								"{0} [relative-sector={1}, sectors={2}, command={3}, speed={4}kB/s, speed-transition={5}, cache-transition={6}, communication-retry=True, initial-sense={7}/ASC={8:X2}/ASCQ={9:X2}]",
								Resource1.ReadCDError,
								sector,
								Sectors2Read,
								_readCDCommand,
								_appliedSpeedKbps,
								_speedChangeJustApplied,
								_cacheDefeatJustFlushed,
								readFailure.SenseKey,
								readFailure.Asc,
								readFailure.Ascq);
							throw new SCSIException(
								repeatedContext,
								m_device,
								repeatedStatus);
						}
					}
					_cacheDefeatJustFlushed = false;
					_speedChangeJustApplied = false;
					//TimeSpan delay1 = DateTime.Now - LastFetch;
					//DateTime LastFetched = DateTime.Now;
					if (pass >= _correctionQuality)
                        CorrectSectors(pass, sector, Sectors2Read);
					//TimeSpan delay2 = DateTime.Now - LastFetched;
					//if (sector == _currentStart)
					//System.Console.WriteLine("\n{0},{1}", delay1.TotalMilliseconds, delay2.TotalMilliseconds);
					if (ReadProgress != null)
					{
						progressArgs.Action = Resource1.StatusRipping;
						progressArgs.Position = sector + Sectors2Read;
						progressArgs.Pass = pass;
						progressArgs.PassStart = _currentStart;
						progressArgs.PassEnd = _currentEnd;
						progressArgs.ErrorsCount = _currentErrorsCount;
						progressArgs.ThisPassErrors = _thisPassErrors;
						progressArgs.SlipStrengthPct = _slipVerdictPending ? _slipVerdictPct : -1;
						progressArgs.SlipOffset = _slipVerdictOff;
						progressArgs.PassTime = PassTime;
						ReadProgress(this, progressArgs);
						_slipVerdictPending = false;   // surfaced once
					}
				}
				// Deep recovery: classify a persistent slip ONCE per window (read-only probe - repeated
				// raw reads of the first chunk, correlated; never touches the vote or the audio bytes).
				if (deepRecoveryActive && !_slipClassified && pass > _correctionQuality)
				{
					int windowSize = _currentEnd - _currentStart;
					if (_currentErrorsCount >= (windowSize * 85) / 100)
					{
						if (++_slipStreak >= 2)
						{
							ClassifySlip(
								_currentStart,
								out _slipVerdictOff,
								out _slipVerdictPct);
							_slipClassified = true; _slipVerdictPending = true;
						}
					}
					else _slipStreak = 0;
				}
				if (pass >= _correctionQuality && _currentErrorsCount == 0)
					break;
				// Deep recovery: feed the policy every pass so it tracks the best-so-far, but only ACT
				// on its stop decision in the EXTENSION zone (pass+1 >= max_scans). This makes deep
				// recovery a strict SUPERSET of baseline: it runs at least the old max_scans passes just
				// like today (a window that only resolves at pass 15 still does - the plateau rule must
				// never cut it off early), and only EXTENDS beyond the old cap while the error count is
				// still improving. It can never stop earlier than today.
				if (deepRecoveryActive && pass >= _correctionQuality)
				{
					bool keepGoing = _recovery.ShouldContinue(_currentErrorsCount, (DateTime.Now - recoveryStart).TotalSeconds);
					if (pass + 1 >= max_scans && !keepGoing)
						break;
				}
			}
			// Window give-up classification (R106): the pass loop stopped with sectors still
			// unresolved. Mark exactly the sectors flagged on the final executed pass and surface
			// one engine verdict event; consumers that stop on unrecoverable damage key on this,
			// never on running mid-pass counts (a mid-pass count includes sectors a later chunk of
			// the same pass could still converge).
			if (_currentErrorsCount > 0 && lastExecutedPass >= _correctionQuality
				&& m_retryCount.Length > 0 && m_failedSectors != null)
			{
				int givenUp = FailedSectorAccounting.FinalizeWindow(
					m_retryCount, _retrySectorBase, _currentStart, _currentEnd,
					lastExecutedPass, m_failedSectors);
				_givenUpWindowCount++;
				if (ReadProgress != null)
				{
					progressArgs.Action = Resource1.StatusRipping;
					progressArgs.Position = _currentEnd;
					progressArgs.Pass = lastExecutedPass;
					progressArgs.PassStart = _currentStart;
					progressArgs.PassEnd = _currentEnd;
					progressArgs.ErrorsCount = _currentErrorsCount;
					progressArgs.ThisPassErrors = _thisPassErrors;
					progressArgs.SlipStrengthPct = -1;
					progressArgs.WindowGivenUpSectors = givenUp;
					progressArgs.PassTime = DateTime.Now;
					ReadProgress(this, progressArgs);
					progressArgs.WindowGivenUpSectors = -1;   // surfaced once
				}
			}
		}

		public unsafe int Read(AudioBuffer buff, int maxLength)
		{
			if (_toc == null)
				throw new ReadCDException("Read: invalid TOC");
			buff.Prepare(this, maxLength);
			if (Position >= Length)
				return 0;
			if (_sampleOffset >= Length && !_overreadLeadOut)
			{
				for (int i = 0; i < buff.ByteLength; i++)
					buff.Bytes[i] = 0;
				_sampleOffset += buff.Length;
				return buff.Length; // == Remaining
			}
			if (_sampleOffset < 0 && !_overreadLeadIn)
			{
				buff.Length = Math.Min(buff.Length, -_sampleOffset);
				for (int i = 0; i < buff.ByteLength; i++)
					buff.Bytes[i] = 0;
				_sampleOffset += buff.Length;
				return buff.Length;
			}
			// C# integer division truncates toward zero; overread sample -1 belongs to sector -1,
			// not sector 0.
			int sampleSector = _sampleOffset >= 0
				? _sampleOffset / 588
				: (_sampleOffset - 587) / 588;
			PrefetchSector(sampleSector);
			if (!_overreadLeadOut)
				buff.Length = Math.Min(buff.Length, (int)Length - _sampleOffset);
			buff.Length = Math.Min(buff.Length, _currentEnd * 588 - _sampleOffset);
			if ((_sampleOffset - _currentStart * 588) == 0 && (maxLength < 0 || (_currentEnd - _currentStart) * 588 <= buff.Length))
			{
				buff.Swap(currentData);
				_currentStart = -1;
				_currentEnd = -1;
			} 
			else
				fixed (byte* dest = buff.Bytes, src = &currentData.Bytes[(_sampleOffset - _currentStart * 588) * 4])
					AudioSamples.MemCpy(dest, src, buff.ByteLength);
			_sampleOffset += buff.Length;
			return buff.Length;
		}

        public TimeSpan Duration => TimeSpan.FromSeconds((double)Length / PCM.SampleRate);

        public long Length
		{
			get
			{
				if (_toc == null)
					throw new ReadCDException("invalid TOC");
				return 588 * (int)_toc.AudioLength;
			}
		}

		public AudioPCMConfig PCM
		{
			get
			{
				return AudioPCMConfig.RedBook;
			}
		}

		public string Path
		{
			get
			{
				string result = m_device_letter + ": ";
				if (m_inqury_result != null && m_inqury_result.Valid)
					result += "[" + m_inqury_result.VendorIdentification + " " +
						m_inqury_result.ProductIdentification + " " +
						m_inqury_result.FirmwareVersion + "]";
				return result;
			}
		}

		public string ARName
		{
			get
			{
				return m_inqury_result == null || !m_inqury_result.Valid ? null :
					m_inqury_result.VendorIdentification.TrimEnd(' ', '\0').PadRight(8, ' ') + " - " + 
					m_inqury_result.ProductIdentification.TrimEnd(' ', '\0');
			}
		}

		public string EACName
		{
			get
			{
				return m_inqury_result.VendorIdentification.TrimEnd(' ', '\0') + " " + m_inqury_result.ProductIdentification.TrimEnd(' ', '\0');
			}
		}

		public long Position
		{
			get
			{
				return _sampleOffset - _driveOffset;
			}
			set
			{
				if (_toc == null || _toc.AudioLength <= 0)
					throw new ReadCDException(Resource1.NoAudio);
				_currentStart = -1;
				_currentEnd = -1;
				ResetRetryState();
				_sampleOffset = (int)value + _driveOffset;
			}
		}

		public long Remaining
		{
			get
			{
				return Length - Position;
			}
		}

		public int DriveOffset
		{
			get
			{
				return _driveOffset;
			}
			set
			{
				_driveOffset = value;
				_sampleOffset = value;
				ResetRetryState();
			}
		}

		public int DriveC2ErrorMode
		{
			get
			{
				return (int)_driveC2ErrorMode;
			}
			set
			{
				_driveC2ErrorMode = (DriveC2ErrorModeSetting)value;
			}
		}

		public int CorrectionQuality
		{
			get
			{
				return _correctionQuality;
			}
			set
			{
				if (value < 0 || value > 3)
					throw new Exception("invalid CorrectionQuality");
				_correctionQuality = value;
				ResetRetryState();
            }
		}

		private void ResetRetryState()
		{
			if (_toc == null || _toc.AudioLength <= 0)
			{
				m_retryCount = new byte[0];
				_retrySectorBase = 0;
				m_failedSectors = null;
				return;
			}
			int leadInSectors = _overreadLeadIn && _driveOffset < 0
				? (-_driveOffset + 587) / 588
				: 0;
			int leadOutSectors = _overreadLeadOut && _driveOffset > 0
				? (_driveOffset + 587) / 588
				: 0;
			_retrySectorBase = -leadInSectors;
			m_retryCount = new byte[(int)_toc.AudioLength + leadInSectors + leadOutSectors];
			for (int i = 0; i < m_retryCount.Length; i++)
				m_retryCount[i] = (byte)(_correctionQuality + 1);
			// A fresh read session starts with no failed sectors; window finalization repopulates.
			m_failedSectors = new BitArray((int)_toc.AudioLength);
		}

		public string RipperVersion
		{
			get
			{
				return "CUERipper v2.2.6 Copyright (C) 2008-2025 Grigory Chudov";
				// ripper.GetName().Name + " " + ripper.GetName().Version;
			}
		}

		private int fromBCD(byte hex)
		{
			return (hex >> 4) * 10 + (hex & 15);
		}

		private byte toBCD(int val)
		{
			return (byte)(((val / 10) << 4) + (val % 10));
		}

		private char from6bit(int bcd)
		{
			char[] ISRC6 = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '#', '#', '#', '#', '#', '#', '#', 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z' };
			bcd &= 0x3f;
			return bcd >= ISRC6.Length ? '#' : ISRC6[bcd];
		}
	}

	enum ReadCDCommand
	{
		ReadCdBEh,
		ReadCdD8h,
		Unknown
	};

	public sealed class SCSIException : Exception
	{
		public Device.CommandStatus Status { get; private set; }
		public Device.SenseKeyType SenseKey { get; private set; }
		public byte Asc { get; private set; }
		public byte Ascq { get; private set; }

		// IoctlFailed carries its Win32 error: without it, a device timeout, a removal, and a
		// handle failure all collapse into one opaque word and the scrubbed failure context
		// cannot distinguish them (observed live on H: during a damaged-region pinpoint read).
		public SCSIException(string args, Device device, Device.CommandStatus st)
			: base(args + ": " + (st == Device.CommandStatus.DeviceFailed ? device.GetErrorString()
				: st == Device.CommandStatus.IoctlFailed ? "IoctlFailed (Win32 error " + device.LastError + ")"
				: st.ToString()))
		{
			Status = st;
			SenseKey = st == Device.CommandStatus.DeviceFailed
				? device.GetSenseKey()
				: Device.SenseKeyType.NoSense;
			Asc = st == Device.CommandStatus.DeviceFailed
				? device.GetSenseAsc()
				: (byte)0;
			Ascq = st == Device.CommandStatus.DeviceFailed
				? device.GetSenseAscq()
				: (byte)0;
		}
	}

	public sealed class ReadCDException : Exception
	{
		public ReadCDException(string args, Exception inner)
			: base(args + ": " + inner.Message, inner) { }
		public ReadCDException(string args)
			: base(args) { }
	}
}
