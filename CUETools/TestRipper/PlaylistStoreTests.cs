using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CUEPlayer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestRipper
{
	[TestClass]
	public sealed class PlaylistStoreTests
	{
		private string testDirectory;

		[TestInitialize]
		public void CreateTestDirectory()
		{
			testDirectory = Path.Combine(
				Path.GetTempPath(),
				"CUETools-PlaylistStoreTests-" +
				Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(testDirectory);
		}

		[TestCleanup]
		public void RemoveTestDirectory()
		{
			if (testDirectory != null &&
				Directory.Exists(testDirectory))
				Directory.Delete(testDirectory, true);
		}

		[TestMethod]
		public void RoundTripPreservesOrderAndValues()
		{
			PlaylistStore store = CreateStore();
			PlaylistModel expected = new PlaylistModel();
			expected.Add(
				@"D:\Music & More\one.cue",
				"Sigur Rós",
				"One <Two>",
				"Album \"Name\"",
				3723,
				7);
			expected.Add(
				@"\\server\share\two.flac",
				null,
				null,
				null,
				0,
				0);

			store.Save(expected);
			PlaylistLoadResult loaded = store.Load();

			Assert.AreEqual(PlaylistLoadSource.Primary, loaded.Source);
			Assert.IsNull(loaded.PrimaryFailure);
			Assert.AreEqual(2, loaded.Playlist.Count);
			AssertEntryEqual(expected[0], loaded.Playlist[0]);
			AssertEntryEqual(expected[1], loaded.Playlist[1]);
			Assert.IsFalse(File.Exists(store.BackupPath));
		}

		[TestMethod]
		public void CorruptPrimaryRecoversPreviousGenerationFromBackup()
		{
			PlaylistStore store = CreateStore();
			PlaylistModel first = ModelWithTitle("first");
			PlaylistModel second = ModelWithTitle("second");
			PlaylistModel third = ModelWithTitle("third");
			store.Save(first);
			store.Save(second);
			store.Save(third);
			Assert.IsTrue(File.Exists(store.BackupPath));

			File.WriteAllText(
				store.FilePath,
				"<playlist version=\"1\"><broken>",
				new UTF8Encoding(false));
			PlaylistLoadResult recovered = store.Load();

			Assert.AreEqual(PlaylistLoadSource.Backup, recovered.Source);
			Assert.IsNotNull(recovered.PrimaryFailure);
			Assert.AreEqual(1, recovered.Playlist.Count);
			Assert.AreEqual("second", recovered.Playlist[0].Title);
		}

		[TestMethod]
		public void BackupPromotionPreservesFallbackAndQuarantinesBadPrimary()
		{
			PlaylistStore store = CreateStore();
			store.Save(ModelWithTitle("known-good"));
			store.Save(ModelWithTitle("newer"));
			byte[] backupBefore = File.ReadAllBytes(store.BackupPath);
			byte[] corruptPrimary = Encoding.UTF8.GetBytes(
				"<playlist version=\"1\"><damaged>");
			File.WriteAllBytes(store.FilePath, corruptPrimary);

			PlaylistLoadResult recovered = store.Load();
			Assert.AreEqual(PlaylistLoadSource.Backup, recovered.Source);
			Assert.AreEqual("known-good", recovered.Playlist[0].Title);

			PlaylistPromotionResult promotion =
				store.PromoteRecoveredBackup();

			Assert.IsTrue(promotion.Promoted);
			Assert.IsFalse(
				String.IsNullOrEmpty(promotion.QuarantinedPrimaryPath));
			CollectionAssert.AreEqual(
				corruptPrimary,
				File.ReadAllBytes(promotion.QuarantinedPrimaryPath));
			CollectionAssert.AreEqual(
				backupBefore,
				File.ReadAllBytes(store.BackupPath),
				"Recovery overwrote the last-known-good backup.");

			PlaylistLoadResult afterRestart =
				new PlaylistStore(store.FilePath).Load();
			Assert.AreEqual(
				PlaylistLoadSource.Primary,
				afterRestart.Source);
			Assert.AreEqual(
				"known-good",
				afterRestart.Playlist[0].Title);

			File.WriteAllBytes(store.FilePath, corruptPrimary);
			PlaylistLoadResult afterSecondCorruption =
				new PlaylistStore(store.FilePath).Load();
			Assert.AreEqual(
				PlaylistLoadSource.Backup,
				afterSecondCorruption.Source);
			Assert.AreEqual(
				"known-good",
				afterSecondCorruption.Playlist[0].Title);
		}

		[TestMethod]
		public void CorruptPrimaryAndBackupFailWithoutChangingEitherFile()
		{
			PlaylistStore store = CreateStore();
			byte[] badPrimary = Encoding.UTF8.GetBytes(
				"<playlist version=\"1\"><entry");
			byte[] badBackup = Encoding.UTF8.GetBytes(
				"<not-a-playlist />");
			File.WriteAllBytes(store.FilePath, badPrimary);
			File.WriteAllBytes(store.BackupPath, badBackup);

			Assert.ThrowsException<InvalidDataException>(
				delegate { store.Load(); });

			CollectionAssert.AreEqual(
				badPrimary,
				File.ReadAllBytes(store.FilePath));
			CollectionAssert.AreEqual(
				badBackup,
				File.ReadAllBytes(store.BackupPath));
		}

		[TestMethod]
		public void MissingGenerationsReturnExplicitEmptyResult()
		{
			PlaylistLoadResult loaded = CreateStore().Load();

			Assert.AreEqual(PlaylistLoadSource.Empty, loaded.Source);
			Assert.AreEqual(0, loaded.Playlist.Count);
			Assert.IsNull(loaded.PrimaryFailure);
		}

		[TestMethod]
		public void DtdInputIsRejected()
		{
			PlaylistStore store = CreateStore();
			File.WriteAllText(
				store.FilePath,
				"<!DOCTYPE playlist [<!ENTITY xxe SYSTEM " +
				"\"file:///C:/Windows/win.ini\">]>" +
				"<playlist version=\"1\">" +
				"<entry path=\"&xxe;\" artist=\"\" title=\"\" " +
				"album=\"\" length=\"0\" track=\"0\" />" +
				"</playlist>",
				new UTF8Encoding(false));

			Assert.ThrowsException<InvalidDataException>(
				delegate { store.Load(); });
		}

		[TestMethod]
		public void InvalidXmlCharactersAreRejectedBeforeOutputCreation()
		{
			PlaylistStore store = CreateStore();
			PlaylistModel model = new PlaylistModel();

			Assert.ThrowsException<ArgumentException>(
				delegate
				{
					model.Add(
						@"C:\Music\album.cue",
						"artist",
						"title\u0001",
						"album",
						60,
						1);
				});

			Assert.IsFalse(File.Exists(store.FilePath));
			Assert.AreEqual(
				0,
				Directory.GetFiles(
					testDirectory,
					"playlist.xml.*.tmp").Length);
		}

		[TestMethod]
		public void SerializedOutputIsBoundedAndLeavesNoPartialFile()
		{
			PlaylistStore store = CreateStore();
			PlaylistModel model = new PlaylistModel();
			string expansionHeavyText =
				new string('&', PlaylistEntry.MaximumTextLength);
			for (int i = 0; i < 40; i++)
			{
				model.Add(
					@"C:\Music\album.cue",
					expansionHeavyText,
					expansionHeavyText,
					expansionHeavyText,
					60,
					1);
			}

			Assert.ThrowsException<InvalidDataException>(
				delegate { store.Save(model); });

			Assert.IsFalse(File.Exists(store.FilePath));
			Assert.AreEqual(
				0,
				Directory.GetFiles(
					testDirectory,
					"playlist.xml.*.tmp").Length);
		}

		[TestMethod]
		public void ConcurrentStoresPublishOnlyCompleteGenerations()
		{
			PlaylistStore firstStore = CreateStore();
			PlaylistStore secondStore = CreateStore();
			Exception firstFailure = null;
			Exception secondFailure = null;
			using (ManualResetEvent start = new ManualResetEvent(false))
			{
				Thread firstThread = new Thread(
					new ThreadStart(delegate
					{
						try
						{
							start.WaitOne();
							firstStore.Save(ModelWithTitle("first"));
						}
						catch (Exception ex)
						{
							firstFailure = ex;
						}
					}));
				Thread secondThread = new Thread(
					new ThreadStart(delegate
					{
						try
						{
							start.WaitOne();
							secondStore.Save(ModelWithTitle("second"));
						}
						catch (Exception ex)
						{
							secondFailure = ex;
						}
					}));
				firstThread.IsBackground = true;
				secondThread.IsBackground = true;
				firstThread.Start();
				secondThread.Start();
				start.Set();

				Assert.IsTrue(
					firstThread.Join(10000),
					"The first playlist writer did not finish.");
				Assert.IsTrue(
					secondThread.Join(10000),
					"The second playlist writer did not finish.");
			}

			Assert.IsNull(firstFailure);
			Assert.IsNull(secondFailure);
			PlaylistLoadResult primary = firstStore.Load();
			PlaylistLoadResult backup =
				new PlaylistStore(firstStore.BackupPath).Load();
			List<string> titles = new List<string>();
			titles.Add(primary.Playlist[0].Title);
			titles.Add(backup.Playlist[0].Title);
			CollectionAssert.AreEquivalent(
				new string[] { "first", "second" },
				titles);
			Assert.AreEqual(
				0,
				Directory.GetFiles(
					testDirectory,
					"playlist.xml.*.tmp").Length);
		}

		[TestMethod]
		public void FailedCommitDoesNotLeakTemporaryFile()
		{
			string destination = Path.Combine(
				testDirectory,
				"playlist.xml");
			Directory.CreateDirectory(destination);
			PlaylistStore store = new PlaylistStore(destination);

			Assert.ThrowsException<IOException>(
				delegate { store.Save(ModelWithTitle("blocked")); });

			string[] leaked = Directory.GetFiles(
				testDirectory,
				"playlist.xml.*.tmp");
			Assert.AreEqual(
				0,
				leaked.Length,
				"A failed atomic commit left a temporary file.");
		}

		[TestMethod]
		public void SameDirectoryLockSerializesAnIndependentProcess()
		{
			PlaylistStore store = CreateStore();
			string readyPath = Path.Combine(testDirectory, "lock-ready");
			string releasePath = Path.Combine(testDirectory, "lock-release");
			string powerShellPath = Path.Combine(
				Environment.GetFolderPath(
					Environment.SpecialFolder.System),
				"WindowsPowerShell",
				"v1.0",
				"powershell.exe");
			Assert.IsTrue(
				File.Exists(powerShellPath),
				"Windows PowerShell is required for this process-boundary test.");

			string script =
				"$lockPath = '" + EscapePowerShellLiteral(store.LockPath) + "'\r\n" +
				"$readyPath = '" + EscapePowerShellLiteral(readyPath) + "'\r\n" +
				"$releasePath = '" + EscapePowerShellLiteral(releasePath) + "'\r\n" +
				"$lockStream = New-Object System.IO.FileStream -ArgumentList " +
				"@($lockPath, [System.IO.FileMode]::OpenOrCreate, " +
				"[System.IO.FileAccess]::ReadWrite, " +
				"[System.IO.FileShare]::None, 1, " +
				"[System.IO.FileOptions]::DeleteOnClose)\r\n" +
				"try {\r\n" +
				"  [System.IO.File]::WriteAllText($readyPath, 'ready')\r\n" +
				"  while (-not [System.IO.File]::Exists($releasePath)) " +
				"{ Start-Sleep -Milliseconds 25 }\r\n" +
				"} finally { $lockStream.Dispose() }\r\n";
			string encodedCommand = Convert.ToBase64String(
				Encoding.Unicode.GetBytes(script));
			ProcessStartInfo startInfo = new ProcessStartInfo();
			startInfo.FileName = powerShellPath;
			startInfo.Arguments =
				"-NoLogo -NoProfile -NonInteractive -EncodedCommand " +
				encodedCommand;
			startInfo.UseShellExecute = false;
			startInfo.CreateNoWindow = true;

			using (Process child = new Process())
			{
				child.StartInfo = startInfo;
				Thread releaseThread = null;
				try
				{
					Assert.IsTrue(child.Start());
					Assert.IsTrue(
						WaitForFile(readyPath, 5000),
						"The child process did not acquire the playlist lock.");

					releaseThread = new Thread(
						new ThreadStart(delegate
						{
							Thread.Sleep(500);
							File.WriteAllText(releasePath, "release");
						}));
					releaseThread.IsBackground = true;
					Stopwatch waiting = Stopwatch.StartNew();
					releaseThread.Start();
					store.Save(ModelWithTitle("after-lock"));
					waiting.Stop();

					Assert.IsTrue(
						waiting.ElapsedMilliseconds >= 300,
						"The playlist writer did not wait for the other process.");
					Assert.IsTrue(
						child.WaitForExit(5000),
						"The lock-holder process did not exit.");
					Assert.AreEqual(0, child.ExitCode);
				}
				finally
				{
					if (!File.Exists(releasePath))
						File.WriteAllText(releasePath, "release");
					if (releaseThread != null)
						releaseThread.Join(2000);
					if (!child.HasExited)
					{
						child.Kill();
						child.WaitForExit(5000);
					}
				}
			}

			Assert.AreEqual(
				"after-lock",
				store.Load().Playlist[0].Title);
			Assert.IsFalse(
				File.Exists(store.LockPath),
				"The process lock file was not released.");
		}

		[TestMethod]
		public void PlaylistItemsKeepStableModelIdentityWhenIconsFail()
		{
			PlaylistModel model = new PlaylistModel();
			PlaylistEntry first = model.Add(
				@"C:\Music\first.flac",
				"artist",
				"first",
				"album",
				60,
				1);
			PlaylistEntry selected = model.Add(
				@"C:\Music\selected.flac",
				"artist",
				"selected",
				"album",
				61,
				2);
			PlaylistEntry next = model.Add(
				@"C:\Music\next.flac",
				"artist",
				"next",
				"album",
				62,
				3);
			Exception iconFailure;
			ListViewItem item = PlaylistItemFactory.Create(
				selected,
				delegate
				{
					throw new InvalidOperationException("icon failure");
				},
				new ListViewGroup("album"),
				out iconFailure);

			Assert.IsInstanceOfType(
				iconFailure,
				typeof(InvalidOperationException));
			Assert.AreEqual(-1, item.ImageIndex);
			Assert.AreSame(selected, item.Tag);
			Assert.IsTrue(model.Remove(first));
			Assert.AreEqual(0, model.IndexOf(selected));
			Assert.AreSame(next, model.GetNext(selected));
			Assert.AreSame(
				next,
				model.GetNext(item.Tag as PlaylistEntry));
		}

		[TestMethod]
		public void MissingLegacyProviderReturnsWithoutTouchingDatabase()
		{
			string legacyPath = Path.Combine(
				testDirectory,
				"CUEPlayer.sdf");
			byte[] original = new byte[] { 1, 2, 3, 4, 5 };
			File.WriteAllBytes(legacyPath, original);
			PlaylistModel imported;
			Exception failure;
			Stopwatch elapsed = Stopwatch.StartNew();

			bool result = LegacyPlaylistImporter.TryImport(
				legacyPath,
				delegate { return null; },
				out imported,
				out failure);
			elapsed.Stop();

			Assert.IsFalse(result);
			Assert.IsNull(imported);
			Assert.IsNull(failure);
			Assert.IsTrue(
				elapsed.ElapsedMilliseconds < 1000,
				"An absent legacy provider should not block on database I/O.");
			CollectionAssert.AreEqual(
				original,
				File.ReadAllBytes(legacyPath));
		}

		[TestMethod]
		public void LegacyRowConversionHandlesNullsAndRejectsNegatives()
		{
			DataTable table = CreateLegacyTable();
			table.Rows.Add(
				@"C:\Music\legacy.cue",
				DBNull.Value,
				"Legacy title",
				"Legacy album",
				123L,
				"4");
			using (DataTableReader reader = table.CreateDataReader())
			{
				Assert.IsTrue(reader.Read());
				PlaylistEntry entry =
					LegacyPlaylistImporter.ReadEntry(reader);
				Assert.AreEqual(@"C:\Music\legacy.cue", entry.Path);
				Assert.AreEqual(String.Empty, entry.Artist);
				Assert.AreEqual("Legacy title", entry.Title);
				Assert.AreEqual("Legacy album", entry.Album);
				Assert.AreEqual(123, entry.LengthSeconds);
				Assert.AreEqual(4, entry.TrackNumber);
			}

			DataTable negativeTable = CreateLegacyTable();
			negativeTable.Rows.Add(
				@"C:\Music\legacy.cue",
				"artist",
				"title",
				"album",
				-1,
				1);
			using (DataTableReader reader =
				negativeTable.CreateDataReader())
			{
				Assert.IsTrue(reader.Read());
				Assert.ThrowsException<InvalidOperationException>(
					delegate
					{
						LegacyPlaylistImporter.ReadEntry(reader);
					});
			}
		}

		private PlaylistStore CreateStore()
		{
			return new PlaylistStore(
				Path.Combine(testDirectory, "playlist.xml"));
		}

		private static PlaylistModel ModelWithTitle(string title)
		{
			PlaylistModel model = new PlaylistModel();
			model.Add(
				@"C:\Music\album.cue",
				"artist",
				title,
				"album",
				60,
				1);
			return model;
		}

		private static DataTable CreateLegacyTable()
		{
			DataTable table = new DataTable();
			table.Columns.Add("path", typeof(object));
			table.Columns.Add("artist", typeof(object));
			table.Columns.Add("title", typeof(object));
			table.Columns.Add("album", typeof(object));
			table.Columns.Add("length", typeof(object));
			table.Columns.Add("track", typeof(object));
			return table;
		}

		private static string EscapePowerShellLiteral(string value)
		{
			return value.Replace("'", "''");
		}

		private static bool WaitForFile(
			string path,
			int timeoutMilliseconds)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
			{
				if (File.Exists(path))
					return true;
				Thread.Sleep(25);
			}
			return File.Exists(path);
		}

		private static void AssertEntryEqual(
			PlaylistEntry expected,
			PlaylistEntry actual)
		{
			Assert.AreEqual(expected.Path, actual.Path);
			Assert.AreEqual(expected.Artist, actual.Artist);
			Assert.AreEqual(expected.Title, actual.Title);
			Assert.AreEqual(expected.Album, actual.Album);
			Assert.AreEqual(
				expected.LengthSeconds,
				actual.LengthSeconds);
			Assert.AreEqual(
				expected.TrackNumber,
				actual.TrackNumber);
		}
	}
}
