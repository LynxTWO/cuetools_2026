using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;

namespace CUEPlayer
{
	public sealed class PlaylistEntry
	{
		public const int MaximumTextLength = 32768;

		public PlaylistEntry(
			string path,
			string artist,
			string title,
			string album,
			int lengthSeconds,
			int trackNumber)
		{
			if (String.IsNullOrEmpty(path))
				throw new ArgumentException("A playlist entry must have a path.", "path");
			ValidateText(path, "path");
			ValidateText(artist ?? String.Empty, "artist");
			ValidateText(title ?? String.Empty, "title");
			ValidateText(album ?? String.Empty, "album");
			if (lengthSeconds < 0)
				throw new ArgumentOutOfRangeException("lengthSeconds");
			if (trackNumber < 0)
				throw new ArgumentOutOfRangeException("trackNumber");

			Path = path;
			Artist = artist ?? String.Empty;
			Title = title ?? String.Empty;
			Album = album ?? String.Empty;
			LengthSeconds = lengthSeconds;
			TrackNumber = trackNumber;
		}

		public string Path { get; private set; }
		public string Artist { get; private set; }
		public string Title { get; private set; }
		public string Album { get; private set; }
		public int LengthSeconds { get; private set; }
		public int TrackNumber { get; private set; }

		private static void ValidateText(string value, string name)
		{
			if (value.Length > MaximumTextLength)
			{
				throw new ArgumentException(
					"The playlist " + name + " exceeds the length limit.",
					name);
			}
			try
			{
				XmlConvert.VerifyXmlChars(value);
			}
			catch (XmlException ex)
			{
				throw new ArgumentException(
					"The playlist " + name +
					" contains characters that XML cannot store.",
					name,
					ex);
			}
		}
	}

	public sealed class PlaylistModel : IEnumerable<PlaylistEntry>
	{
		public const int MaximumEntryCount = 10000;

		private readonly List<PlaylistEntry> entries =
			new List<PlaylistEntry>();

		public int Count
		{
			get { return entries.Count; }
		}

		public PlaylistEntry this[int index]
		{
			get { return entries[index]; }
		}

		public PlaylistEntry Add(
			string path,
			string artist,
			string title,
			string album,
			int lengthSeconds,
			int trackNumber)
		{
			PlaylistEntry entry = new PlaylistEntry(
				path,
				artist,
				title,
				album,
				lengthSeconds,
				trackNumber);
			Add(entry);
			return entry;
		}

		public void Add(PlaylistEntry entry)
		{
			if (entry == null)
				throw new ArgumentNullException("entry");
			if (entries.Count >= MaximumEntryCount)
				throw new InvalidOperationException(
					"The playlist contains too many entries.");
			entries.Add(entry);
		}

		public bool Remove(PlaylistEntry entry)
		{
			return entries.Remove(entry);
		}

		public int IndexOf(PlaylistEntry entry)
		{
			if (entry == null)
				return -1;
			for (int i = 0; i < entries.Count; i++)
			{
				if (Object.ReferenceEquals(entries[i], entry))
					return i;
			}
			return -1;
		}

		public PlaylistEntry GetNext(PlaylistEntry entry)
		{
			int index = IndexOf(entry);
			if (index < 0 || index >= entries.Count - 1)
				return null;
			return entries[index + 1];
		}

		public IEnumerator<PlaylistEntry> GetEnumerator()
		{
			return entries.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}
