using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;
using CUEControls;
using CUETools.Codecs;
using CUETools.DSP.Mixer;
using CUETools.Processor;

namespace CUEPlayer
{
	public partial class frmCUEPlayer : Form
	{
		private ShellIconMgr _icon_mgr;
		private CUEConfig _config;
		private readonly PlaylistStore playlistStore;
		private PlaylistModel playlistModel = new PlaylistModel();
		private bool playlistPersistenceEnabled = true;
		private bool playlistDirty;
		private bool playlistSessionOnlyWarningShown;
		private Thread mixThread;
		private MixingSource _mixer;

		internal Playlist wndPlaylist
		{
			get
			{
				return playlist;
			}
		}

		internal PlaylistModel PlaylistModel
		{
			get
			{
				return playlistModel;
			}
		}

		internal void PlaylistChanged()
		{
			playlistDirty = true;
			if (!playlistPersistenceEnabled &&
				!playlistSessionOnlyWarningShown)
			{
				playlistSessionOnlyWarningShown = true;
				MessageBox.Show(
					this,
					"Playlist saving is unavailable for this session. " +
					"These playlist changes are session-only and will be " +
					"discarded when CUEPlayer exits.",
					"Playlist changes are session-only",
					MessageBoxButtons.OK,
					MessageBoxIcon.Warning);
			}
		}

		internal CUEConfig Config
		{
			get
			{
				return _config;
			}
		}

		internal ShellIconMgr IconMgr
		{
			get
			{
				return _icon_mgr;
			}
		}

		public MixingSource Mixer
		{
			get
			{
				return _mixer;
			}
		}

		public frmCUEPlayer()
		{
			InitializeComponent();
			_icon_mgr = new ShellIconMgr();
			_config = new CUEConfig();
			_config.separateDecodingThread = false;
			playlistStore = new PlaylistStore(
				PlaylistStore.GetDefaultFilePath());
		}

		internal Deck deckA = new Deck(0, "A");
		internal Deck deckB = new Deck(1, "B");
		internal Output outputA = new Output();
		internal Browser browser = new Browser();
		internal Playlist playlist = new Playlist();

		private void frmCUEPlayer_Load(object sender, EventArgs e)
		{
			if (Properties.Settings.Default.AppSettings == null)
			{
				Properties.Settings.Default.AppSettings = new CUEPlayerSettings();
				Properties.Settings.Default.AppSettings.IcecastServers.Add(new CUETools.Codecs.Icecast.IcecastSettingsData());
			}

			LoadPlaylist();

			_mixer = new MixingSource(new AudioPCMConfig(32, 2, 44100), 100, 2);

			outputA.Init(this);
			browser.Init(this);
			playlist.Init(this);
			deckB.Init(this, null);
			deckA.Init(this, deckB);
			Icecast icecast = new Icecast();
			icecast.Init(this);
			//LayoutMdi(MdiLayout.TileHorizontal);

			browser.Location = new Point(0, 0);
			browser.Height = ClientRectangle.Height - 5 - menuStrip1.Height;
			playlist.Location = new Point(browser.Location.X + browser.Width, 0);
			playlist.Height = ClientRectangle.Height - 5 - menuStrip1.Height;
			deckA.Location = new Point(playlist.Location.X + playlist.Width, 0);
			deckB.Location = new Point(playlist.Location.X + playlist.Width, deckA.Height);
			outputA.Location = new Point(deckA.Location.X + deckA.Width, 0);
			icecast.Location = new Point(deckA.Location.X + deckA.Width, outputA.Height);

			mixThread = new Thread(MixThread);
			mixThread.Priority = ThreadPriority.AboveNormal;
			mixThread.IsBackground = true;
			mixThread.Name = "Mixer";
			mixThread.Start();
		}

		private void frmCUEPlayer_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (!playlistDirty)
				return;

			if (!playlistPersistenceEnabled)
			{
				DialogResult discardChanges = MessageBox.Show(
					this,
					"Playlist saving is unavailable for this session. Exit " +
					"and discard the session-only playlist changes?",
					"Discard session-only playlist changes?",
					MessageBoxButtons.YesNo,
					MessageBoxIcon.Warning,
					MessageBoxDefaultButton.Button2);
				if (discardChanges == DialogResult.No)
					e.Cancel = true;
				return;
			}

			try
			{
				playlistStore.Save(playlistModel);
				playlistDirty = false;
			}
			catch (Exception ex)
			{
				System.Diagnostics.Trace.WriteLine(
					"Playlist save failed (" + ex.GetType().Name + ").");
				DialogResult exitWithoutSaving = MessageBox.Show(
					this,
					"The playlist could not be saved. Exit without saving " +
					"the playlist changes?",
					"Playlist save failed",
					MessageBoxButtons.YesNo,
					MessageBoxIcon.Error,
					MessageBoxDefaultButton.Button2);
				if (exitWithoutSaving == DialogResult.No)
					e.Cancel = true;
			}
		}

		private void LoadPlaylist()
		{
			try
			{
				PlaylistLoadResult result = playlistStore.Load();
				playlistModel = result.Playlist;
				if (result.Source == PlaylistLoadSource.Backup)
				{
					Trace.WriteLine(
						"Playlist primary failed; recovered the backup (" +
						(result.PrimaryFailure == null
							? "primary missing"
							: result.PrimaryFailure.GetType().Name) +
						").");
					try
					{
						PlaylistPromotionResult promotion =
							playlistStore.PromoteRecoveredBackup();
						PlaylistLoadResult repaired = playlistStore.Load();
						if (repaired.Source != PlaylistLoadSource.Primary)
						{
							throw new InvalidDataException(
								"Playlist recovery did not produce a valid primary.");
						}
						playlistModel = repaired.Playlist;
						playlistDirty = false;
						if (promotion.Promoted)
						{
							Trace.WriteLine(
								"Playlist backup promoted; validated backup " +
								"was preserved.");
						}
						if (promotion.QuarantinedPrimaryPath != null)
						{
							MessageBox.Show(
								this,
								"The playlist was recovered from its backup. " +
								"The damaged primary file was preserved at:\r\n\r\n" +
								promotion.QuarantinedPrimaryPath,
								"Playlist recovered",
								MessageBoxButtons.OK,
								MessageBoxIcon.Warning);
						}
					}
					catch (Exception promotionFailure)
					{
						DisableRecoveredPlaylistPersistence(
							promotionFailure);
					}
				}
				else if (result.Source == PlaylistLoadSource.Empty)
				{
					TryImportLegacyPlaylist();
				}
			}
			catch (InvalidDataException ex)
			{
				DisablePlaylistPersistence(ex);
			}
			catch (IOException ex)
			{
				DisablePlaylistPersistence(ex);
			}
			catch (UnauthorizedAccessException ex)
			{
				DisablePlaylistPersistence(ex);
			}
			catch (TimeoutException ex)
			{
				DisablePlaylistPersistence(ex);
			}
		}

		private void DisableRecoveredPlaylistPersistence(Exception failure)
		{
			playlistPersistenceEnabled = false;
			playlistDirty = false;
			Trace.WriteLine(
				"Playlist backup loaded, but primary repair failed; saving " +
				"is disabled for this session (" +
				failure.GetType().Name + ").");
			MessageBox.Show(
				this,
				"The playlist was loaded from its backup, but the primary " +
				"file could not be repaired safely. The backup was left " +
				"unchanged. Playlist saving is disabled for this session.",
				"Playlist recovery incomplete",
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
		}

		private void DisablePlaylistPersistence(Exception failure)
		{
			playlistModel = new PlaylistModel();
			playlistPersistenceEnabled = false;
			playlistDirty = false;
			Trace.WriteLine(
				"Playlist load failed; corrupt files were preserved and " +
				"saving is disabled for this session (" +
				failure.GetType().Name + ").");
			MessageBox.Show(
				this,
				"The playlist could not be loaded. The XML files were left " +
				"unchanged, and playlist saving is disabled for this session.",
				"Playlist load failed",
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
		}

		private void TryImportLegacyPlaylist()
		{
			bool legacyPlaylistFound = false;
			Exception lastFailure = null;
			foreach (string legacyPath in GetLegacyPlaylistPaths())
			{
				if (!File.Exists(legacyPath))
					continue;
				legacyPlaylistFound = true;

				PlaylistModel imported;
				Exception failure;
				if (!LegacyPlaylistImporter.TryImport(
					legacyPath,
					out imported,
					out failure))
				{
					if (failure != null)
					{
						lastFailure = failure;
						Trace.WriteLine(
							"Legacy playlist import failed (" +
							failure.GetType().Name + ").");
					}
					continue;
				}

				playlistModel = imported;
				playlistDirty = true;
				try
				{
					playlistStore.Save(playlistModel);
					playlistDirty = false;
					Trace.WriteLine(
						"Legacy playlist imported to the per-user XML store.");
				}
				catch (Exception ex)
				{
					Trace.WriteLine(
						"Legacy playlist was loaded for this session but " +
						"could not be persisted (" +
						ex.GetType().Name + ").");
				}
				return;
			}

			if (legacyPlaylistFound)
			{
				Trace.WriteLine(
					"A legacy playlist database remains untouched, but it " +
					"could not be imported (" +
					(lastFailure == null
						? "SQL Server Compact unavailable"
						: lastFailure.GetType().Name) +
					"). New playlist changes can still be saved as XML.");
				MessageBox.Show(
					this,
					"A legacy CUEPlayer playlist was found but could not be " +
					"imported. " +
					(lastFailure == null
						? "SQL Server Compact 3.5 is unavailable. "
						: "The legacy database could not be read safely. ") +
					"The legacy .sdf file was not changed.\r\n\r\n" +
					"You can still create and save a new XML playlist. If " +
					"you make no playlist changes, migration will be retried " +
					"the next time CUEPlayer starts.",
					"Legacy playlist not imported",
					MessageBoxButtons.OK,
					MessageBoxIcon.Warning);
			}
		}

		private static IEnumerable<string> GetLegacyPlaylistPaths()
		{
			List<string> paths = new List<string>();
			object dataDirectory =
				AppDomain.CurrentDomain.GetData("DataDirectory");
			AddLegacyPlaylistPath(paths, dataDirectory as string);
			AddLegacyPlaylistPath(
				paths,
				AppDomain.CurrentDomain.BaseDirectory);
			return paths;
		}

		private static void AddLegacyPlaylistPath(
			List<string> paths,
			string directory)
		{
			if (String.IsNullOrEmpty(directory))
				return;

			string candidate;
			try
			{
				candidate = Path.GetFullPath(
					Path.Combine(directory, "CUEPlayer.sdf"));
			}
			catch (Exception ex)
			{
				Trace.WriteLine(
					"Legacy playlist path was ignored (" +
					ex.GetType().Name + ").");
				return;
			}

			foreach (string existing in paths)
			{
				if (String.Equals(
					existing,
					candidate,
					StringComparison.OrdinalIgnoreCase))
					return;
			}
			paths.Add(candidate);
		}

		private void MixThread()
		{
			AudioBuffer result = new AudioBuffer(_mixer.PCM, _mixer.BufferSize);
			while (true)
			{
				_mixer.Read(result, -1);
			}
		}

		public event EventHandler<UpdateMetadataEvent> updateMetadata;

		public void UpdateMetadata(string artist, string title)
		{
			UpdateMetadataEvent e = new UpdateMetadataEvent();
			e.artist = artist;
			e.title = title;
			if (updateMetadata != null)
				updateMetadata(this, e);
		}

		private void icecastToolStripMenuItem_Click(object sender, EventArgs e)
		{
			Icecast icecast = new Icecast();
			icecast.Init(this);
		}
	}

	public class UpdateMetadataEvent: EventArgs
	{
		public string artist;
		public string title;
	}

	public class CUEPlayerSettings
	{
		private BindingList<CUETools.Codecs.Icecast.IcecastSettingsData> icecastServers;

		public CUEPlayerSettings()
		{
			icecastServers = new BindingList<CUETools.Codecs.Icecast.IcecastSettingsData>();
		}

		public BindingList<CUETools.Codecs.Icecast.IcecastSettingsData> IcecastServers
		{
			get
			{
				return icecastServers;
			}
			set
			{
				icecastServers = value;
			}
		}
	}
}
