using System;
using System.IO;
using System.Collections.Generic;
using CUETools.Compression;
using ICSharpCode.SharpZipLib.Zip;

namespace CUETools.Compression.Zip
{
	[CompressionProviderClass("zip")]
	public class ZipCompressionProvider : ICompressionProvider
	{
		private ZipFile _zipFile;
		private bool _passwordSet;

		public ZipCompressionProvider(string path)
		{
			_zipFile = new ZipFile(path);
		}

		public void Close()
		{
			if (_zipFile != null) _zipFile.Close();
			_zipFile = null;
		}

		~ZipCompressionProvider()
		{
			Close();
		}

		public Stream Decompress(string file)
		{
			ZipEntry zipEntry = _zipFile.GetEntry(file);
			if (zipEntry == null)
				throw new Exception("Archive entry not found.");
			// The password must be on the ZipFile before the entry stream is opened. Modern
			// SharpZipLib needs it up front for AES entries (GetInputStream fails otherwise), so
			// request it here rather than lazily on first read. Set once per archive.
			if (zipEntry.IsCrypted && !_passwordSet && PasswordRequired != null)
			{
				CompressionPasswordRequiredEventArgs e = new CompressionPasswordRequiredEventArgs();
				PasswordRequired(this, e);
				if (e.ContinueOperation && e.Password != null && e.Password.Length > 0)
				{
					_zipFile.Password = e.Password;
					_passwordSet = true;
				}
			}
			SeekableZipStream stream = new SeekableZipStream(_zipFile, zipEntry);
			stream.ExtractionProgress += ExtractionProgress;
			return stream;
		}

		public IEnumerable<string> Contents
		{
			get
			{
				foreach (ZipEntry e in _zipFile)
					if (e.IsFile)
						yield return (e.Name);
			}
		}

		public event EventHandler<CompressionPasswordRequiredEventArgs> PasswordRequired;
		public event EventHandler<CompressionExtractionProgressEventArgs> ExtractionProgress;
	}
}
