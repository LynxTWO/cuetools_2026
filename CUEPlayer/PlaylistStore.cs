using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;

namespace CUEPlayer
{
	public enum PlaylistLoadSource
	{
		Empty,
		Primary,
		Backup
	}

	public sealed class PlaylistLoadResult
	{
		internal PlaylistLoadResult(
			PlaylistModel playlist,
			PlaylistLoadSource source,
			Exception primaryFailure)
		{
			Playlist = playlist;
			Source = source;
			PrimaryFailure = primaryFailure;
		}

		public PlaylistModel Playlist { get; private set; }
		public PlaylistLoadSource Source { get; private set; }

		// Non-null when a damaged/unreadable primary caused backup recovery.
		public Exception PrimaryFailure { get; private set; }
	}

	public sealed class PlaylistPromotionResult
	{
		internal PlaylistPromotionResult(
			bool promoted,
			string quarantinedPrimaryPath)
		{
			Promoted = promoted;
			QuarantinedPrimaryPath = quarantinedPrimaryPath;
		}

		// False means another writer had already published a valid primary.
		public bool Promoted { get; private set; }
		public string QuarantinedPrimaryPath { get; private set; }
	}

	public sealed class PlaylistStore
	{
		public const int MaximumEntryCount =
			PlaylistModel.MaximumEntryCount;
		public const int MaximumTextLength =
			PlaylistEntry.MaximumTextLength;
		public const long MaximumFileLength = 16L * 1024L * 1024L;

		private const string RootElementName = "playlist";
		private const string EntryElementName = "entry";
		private const string FormatVersion = "1";
		private const int PathLockTimeoutMilliseconds = 5000;
		private readonly object sync = new object();

		public PlaylistStore(string filePath)
		{
			if (String.IsNullOrEmpty(filePath))
				throw new ArgumentException(
					"A playlist file path is required.",
					"filePath");

			FilePath = Path.GetFullPath(filePath);
			BackupPath = FilePath + ".bak";
			LockPath = FilePath + ".lock";
		}

		public string FilePath { get; private set; }
		public string BackupPath { get; private set; }
		public string LockPath { get; private set; }

		public static string GetDefaultFilePath()
		{
			string localData = Environment.GetFolderPath(
				Environment.SpecialFolder.LocalApplicationData);
			if (String.IsNullOrEmpty(localData))
				throw new InvalidOperationException(
					"The LocalApplicationData folder is unavailable.");

			return Path.Combine(
				localData,
				"CUETools",
				"CUEPlayer",
				"playlist.xml");
		}

		public PlaylistLoadResult Load()
		{
			lock (sync)
			{
				EnsureParentDirectory();
				using (LockFileLease pathLock =
					LockFileLease.Acquire(
						LockPath,
						PathLockTimeoutMilliseconds))
				{
					Exception primaryFailure = null;
					if (File.Exists(FilePath))
					{
						try
						{
							return new PlaylistLoadResult(
								Read(FilePath),
								PlaylistLoadSource.Primary,
								null);
						}
						catch (Exception ex)
						{
							if (!IsRecoverableReadFailure(ex))
								throw;
							primaryFailure = ex;
						}
					}

					Exception backupFailure = null;
					if (File.Exists(BackupPath))
					{
						try
						{
							return new PlaylistLoadResult(
								Read(BackupPath),
								PlaylistLoadSource.Backup,
								primaryFailure);
						}
						catch (Exception ex)
						{
							if (!IsRecoverableReadFailure(ex))
								throw;
							backupFailure = ex;
						}
					}

					if (primaryFailure != null || backupFailure != null)
					{
						Exception cause = primaryFailure ?? backupFailure;
						if (primaryFailure != null && backupFailure != null)
						{
							cause = new AggregateException(
								primaryFailure,
								backupFailure);
						}
						throw new InvalidDataException(
							"Neither playlist generation could be loaded.",
							cause);
					}

					return new PlaylistLoadResult(
						new PlaylistModel(),
						PlaylistLoadSource.Empty,
						null);
				}
			}
		}

		public void Save(PlaylistModel playlist)
		{
			if (playlist == null)
				throw new ArgumentNullException("playlist");
			Validate(playlist);

			lock (sync)
			{
				EnsureParentDirectory();
				using (LockFileLease pathLock =
					LockFileLease.Acquire(
						LockPath,
						PathLockTimeoutMilliseconds))
				{
					string temporaryPath = FilePath + "." +
						Guid.NewGuid().ToString("N") + ".tmp";
					try
					{
						WriteAndFlush(temporaryPath, playlist);

						// Writer disposal and Flush(true) complete before this
						// same-volume publication point. File.Replace retains
						// the prior complete generation as the backup.
						if (File.Exists(FilePath))
						{
							File.Replace(
								temporaryPath,
								FilePath,
								BackupPath,
								true);
						}
						else
						{
							File.Move(temporaryPath, FilePath);
						}
					}
					finally
					{
						if (File.Exists(temporaryPath))
							File.Delete(temporaryPath);
					}
				}
			}
		}

		public PlaylistPromotionResult PromoteRecoveredBackup()
		{
			lock (sync)
			{
				EnsureParentDirectory();
				using (LockFileLease pathLock =
					LockFileLease.Acquire(
						LockPath,
						PathLockTimeoutMilliseconds))
				{
					if (File.Exists(FilePath))
					{
						try
						{
							Read(FilePath);
							return new PlaylistPromotionResult(false, null);
						}
						catch (Exception ex)
						{
							if (!IsRecoverableReadFailure(ex))
								throw;
						}
					}

					if (!File.Exists(BackupPath))
						throw new InvalidDataException(
							"The recovered playlist backup is missing.");

					PlaylistModel recovered = Read(BackupPath);
					string temporaryPath = FilePath + "." +
						Guid.NewGuid().ToString("N") + ".tmp";
					string quarantinedPath = null;
					try
					{
						WriteAndFlush(temporaryPath, recovered);
						PlaylistModel validated = Read(temporaryPath);
						if (!ModelsEqual(recovered, validated))
						{
							throw new InvalidDataException(
								"The staged playlist recovery did not validate.");
						}

						if (File.Exists(FilePath))
						{
							quarantinedPath = GetQuarantinePath();
							File.Move(FilePath, quarantinedPath);
						}

						// The validated backup remains untouched. If this move
						// fails, the next start can recover from it again.
						File.Move(temporaryPath, FilePath);
						return new PlaylistPromotionResult(
							true,
							quarantinedPath);
					}
					finally
					{
						if (File.Exists(temporaryPath))
							File.Delete(temporaryPath);
					}
				}
			}
		}

		private void EnsureParentDirectory()
		{
			string directory = Path.GetDirectoryName(FilePath);
			if (String.IsNullOrEmpty(directory))
				throw new InvalidOperationException(
					"The playlist path has no parent directory.");
			Directory.CreateDirectory(directory);
		}

		private string GetQuarantinePath()
		{
			return FilePath + ".corrupt." +
				DateTime.UtcNow.ToString(
					"yyyyMMddTHHmmssfffffffZ",
					CultureInfo.InvariantCulture) +
				"." + Guid.NewGuid().ToString("N");
		}

		private static bool ModelsEqual(
			PlaylistModel first,
			PlaylistModel second)
		{
			if (first.Count != second.Count)
				return false;
			for (int i = 0; i < first.Count; i++)
			{
				PlaylistEntry left = first[i];
				PlaylistEntry right = second[i];
				if (!String.Equals(left.Path, right.Path, StringComparison.Ordinal) ||
					!String.Equals(left.Artist, right.Artist, StringComparison.Ordinal) ||
					!String.Equals(left.Title, right.Title, StringComparison.Ordinal) ||
					!String.Equals(left.Album, right.Album, StringComparison.Ordinal) ||
					left.LengthSeconds != right.LengthSeconds ||
					left.TrackNumber != right.TrackNumber)
					return false;
			}
			return true;
		}

		private static bool IsRecoverableReadFailure(Exception ex)
		{
			return ex is InvalidDataException ||
				ex is IOException ||
				ex is UnauthorizedAccessException;
		}

		private static PlaylistModel Read(string path)
		{
			try
			{
				using (FileStream stream = new FileStream(
					path,
					FileMode.Open,
					FileAccess.Read,
					FileShare.Read))
				{
					if (stream.Length > MaximumFileLength)
						throw new InvalidDataException(
							"The playlist file exceeds the size limit.");

					XmlReaderSettings settings = new XmlReaderSettings();
					settings.DtdProcessing = DtdProcessing.Prohibit;
					settings.XmlResolver = null;
					settings.MaxCharactersInDocument = MaximumFileLength;
					settings.MaxCharactersFromEntities = 1024;
					settings.IgnoreComments = true;
					settings.IgnoreProcessingInstructions = true;

					using (XmlReader reader = XmlReader.Create(stream, settings))
						return ReadDocument(reader);
				}
			}
			catch (InvalidDataException)
			{
				throw;
			}
			catch (XmlException ex)
			{
				throw new InvalidDataException(
					"The playlist XML is invalid.",
					ex);
			}
			catch (FormatException ex)
			{
				throw new InvalidDataException(
					"The playlist contains an invalid value.",
					ex);
			}
			catch (OverflowException ex)
			{
				throw new InvalidDataException(
					"The playlist contains an out-of-range value.",
					ex);
			}
			catch (ArgumentException ex)
			{
				throw new InvalidDataException(
					"The playlist contains an invalid entry.",
					ex);
			}
		}

		private static PlaylistModel ReadDocument(XmlReader reader)
		{
			if (reader.MoveToContent() != XmlNodeType.Element ||
				reader.LocalName != RootElementName ||
				reader.NamespaceURI.Length != 0)
			{
				throw new InvalidDataException(
					"The playlist root element is invalid.");
			}

			EnsureOnlyAttributes(reader, new string[] { "version" });
			if (reader.GetAttribute("version") != FormatVersion)
				throw new InvalidDataException(
					"The playlist format version is unsupported.");

			bool emptyRoot = reader.IsEmptyElement;
			reader.ReadStartElement(RootElementName);
			PlaylistModel result = new PlaylistModel();
			if (emptyRoot)
			{
				EnsureEndOfDocument(reader);
				return result;
			}

			while (reader.MoveToContent() == XmlNodeType.Element)
			{
				if (reader.LocalName != EntryElementName ||
					reader.NamespaceURI.Length != 0)
				{
					throw new InvalidDataException(
						"The playlist contains an unknown element.");
				}
				if (result.Count >= MaximumEntryCount)
					throw new InvalidDataException(
						"The playlist contains too many entries.");

				EnsureOnlyAttributes(
					reader,
					new string[] {
						"path",
						"artist",
						"title",
						"album",
						"length",
						"track"
					});

				string path = RequiredAttribute(reader, "path");
				string artist = OptionalAttribute(reader, "artist");
				string title = OptionalAttribute(reader, "title");
				string album = OptionalAttribute(reader, "album");
				int length = NonNegativeIntAttribute(reader, "length");
				int track = NonNegativeIntAttribute(reader, "track");
				ValidateText(path, "path");
				ValidateText(artist, "artist");
				ValidateText(title, "title");
				ValidateText(album, "album");

				bool emptyEntry = reader.IsEmptyElement;
				reader.ReadStartElement(EntryElementName);
				if (!emptyEntry)
				{
					if (reader.MoveToContent() != XmlNodeType.EndElement)
						throw new InvalidDataException(
							"Playlist entries cannot contain child content.");
					reader.ReadEndElement();
				}

				result.Add(
					path,
					artist,
					title,
					album,
					length,
					track);
			}

			if (reader.NodeType != XmlNodeType.EndElement ||
				reader.LocalName != RootElementName)
				throw new InvalidDataException(
					"The playlist root element was not closed.");
			reader.ReadEndElement();
			EnsureEndOfDocument(reader);
			return result;
		}

		private static void EnsureEndOfDocument(XmlReader reader)
		{
			if (reader.MoveToContent() != XmlNodeType.None)
				throw new InvalidDataException(
					"The playlist contains trailing content.");
		}

		private static void EnsureOnlyAttributes(
			XmlReader reader,
			string[] allowedNames)
		{
			if (!reader.HasAttributes)
				return;

			while (reader.MoveToNextAttribute())
			{
				bool allowed = false;
				for (int i = 0; i < allowedNames.Length; i++)
				{
					if (reader.LocalName == allowedNames[i] &&
						reader.NamespaceURI.Length == 0)
					{
						allowed = true;
						break;
					}
				}
				if (!allowed)
					throw new InvalidDataException(
						"The playlist contains an unknown attribute.");
			}
			reader.MoveToElement();
		}

		private static string RequiredAttribute(
			XmlReader reader,
			string name)
		{
			string value = reader.GetAttribute(name);
			if (String.IsNullOrEmpty(value))
				throw new InvalidDataException(
					"The playlist entry is missing " + name + ".");
			return value;
		}

		private static string OptionalAttribute(
			XmlReader reader,
			string name)
		{
			return reader.GetAttribute(name) ?? String.Empty;
		}

		private static int NonNegativeIntAttribute(
			XmlReader reader,
			string name)
		{
			string value = reader.GetAttribute(name);
			if (value == null)
				throw new InvalidDataException(
					"The playlist entry is missing " + name + ".");
			int result = XmlConvert.ToInt32(value);
			if (result < 0)
				throw new InvalidDataException(
					"The playlist entry has a negative " + name + ".");
			return result;
		}

		private static void Validate(PlaylistModel playlist)
		{
			if (playlist.Count > MaximumEntryCount)
				throw new InvalidDataException(
					"The playlist contains too many entries.");

			foreach (PlaylistEntry entry in playlist)
			{
				if (entry == null)
					throw new InvalidDataException(
						"The playlist contains a null entry.");
				ValidateText(entry.Path, "path");
				ValidateText(entry.Artist, "artist");
				ValidateText(entry.Title, "title");
				ValidateText(entry.Album, "album");
				if (entry.LengthSeconds < 0 || entry.TrackNumber < 0)
					throw new InvalidDataException(
						"The playlist contains a negative numeric value.");
			}
		}

		private static void ValidateText(string value, string name)
		{
			if (value == null)
				throw new InvalidDataException(
					"The playlist entry has a null " + name + ".");
			if (value.Length > MaximumTextLength)
				throw new InvalidDataException(
					"The playlist entry " + name + " exceeds the length limit.");
		}

		private static void WriteAndFlush(
			string path,
			PlaylistModel playlist)
		{
			using (FileStream stream = new FileStream(
				path,
				FileMode.CreateNew,
				FileAccess.Write,
				FileShare.None))
			{
				BoundedWriteStream boundedStream =
					new BoundedWriteStream(stream, MaximumFileLength);
				XmlWriterSettings settings = new XmlWriterSettings();
				settings.Encoding = new UTF8Encoding(false);
				settings.Indent = true;
				settings.NewLineChars = "\r\n";

				using (XmlWriter writer =
					XmlWriter.Create(boundedStream, settings))
				{
					writer.WriteStartDocument();
					writer.WriteStartElement(RootElementName);
					writer.WriteAttributeString("version", FormatVersion);
					foreach (PlaylistEntry entry in playlist)
					{
						writer.WriteStartElement(EntryElementName);
						writer.WriteAttributeString("path", entry.Path);
						writer.WriteAttributeString("artist", entry.Artist);
						writer.WriteAttributeString("title", entry.Title);
						writer.WriteAttributeString("album", entry.Album);
						writer.WriteAttributeString(
							"length",
							XmlConvert.ToString(entry.LengthSeconds));
						writer.WriteAttributeString(
							"track",
							XmlConvert.ToString(entry.TrackNumber));
						writer.WriteEndElement();
					}
					writer.WriteEndElement();
					writer.WriteEndDocument();
					writer.Flush();
				}
				boundedStream.Flush();
				stream.Flush(true);
			}
		}

		private sealed class BoundedWriteStream : Stream
		{
			private readonly Stream innerStream;
			private readonly long maximumLength;
			private long bytesWritten;

			public BoundedWriteStream(
				Stream innerStream,
				long maximumLength)
			{
				if (innerStream == null)
					throw new ArgumentNullException("innerStream");
				if (!innerStream.CanWrite)
					throw new ArgumentException(
						"The inner stream must be writable.",
						"innerStream");
				if (maximumLength < 0)
					throw new ArgumentOutOfRangeException("maximumLength");

				this.innerStream = innerStream;
				this.maximumLength = maximumLength;
			}

			public override bool CanRead
			{
				get { return false; }
			}

			public override bool CanSeek
			{
				get { return false; }
			}

			public override bool CanWrite
			{
				get { return true; }
			}

			public override long Length
			{
				get { return bytesWritten; }
			}

			public override long Position
			{
				get { return bytesWritten; }
				set { throw new NotSupportedException(); }
			}

			public override void Flush()
			{
				innerStream.Flush();
			}

			public override int Read(
				byte[] buffer,
				int offset,
				int count)
			{
				throw new NotSupportedException();
			}

			public override long Seek(
				long offset,
				SeekOrigin origin)
			{
				throw new NotSupportedException();
			}

			public override void SetLength(long value)
			{
				throw new NotSupportedException();
			}

			public override void Write(
				byte[] buffer,
				int offset,
				int count)
			{
				if (count > maximumLength - bytesWritten)
					throw new InvalidDataException(
						"The serialized playlist exceeds the size limit.");
				innerStream.Write(buffer, offset, count);
				bytesWritten += count;
			}

			public override void WriteByte(byte value)
			{
				if (bytesWritten >= maximumLength)
					throw new InvalidDataException(
						"The serialized playlist exceeds the size limit.");
				innerStream.WriteByte(value);
				bytesWritten++;
			}
		}

		private sealed class LockFileLease : IDisposable
		{
			private FileStream stream;

			private LockFileLease(FileStream stream)
			{
				this.stream = stream;
			}

			public static LockFileLease Acquire(
				string path,
				int timeoutMilliseconds)
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				Exception lastFailure = null;
				while (true)
				{
					try
					{
						FileStream stream = new FileStream(
							path,
							FileMode.OpenOrCreate,
							FileAccess.ReadWrite,
							FileShare.None,
							1,
							FileOptions.DeleteOnClose);
						return new LockFileLease(stream);
					}
					catch (IOException ex)
					{
						lastFailure = ex;
					}
					catch (UnauthorizedAccessException ex)
					{
						lastFailure = ex;
					}

					if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
					{
						throw new TimeoutException(
							"Timed out waiting for the playlist store.",
							lastFailure);
					}
					Thread.Sleep(25);
				}
			}

			public void Dispose()
			{
				if (stream == null)
					return;
				stream.Dispose();
				stream = null;
			}
		}
	}
}
