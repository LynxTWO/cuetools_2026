using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using CUEControls;
using CUETools.Codecs;
using CUETools.Processor;

namespace CUEPlayer
{
	public partial class Playlist : Form
	{
		internal const string EntryDragFormat =
			"CUEPlayer.PlaylistEntry";

		private CUEConfig _config;
		private ShellIconMgr _icon_mgr;
		private PlaylistModel playlistModel;

		public Playlist()
		{
			InitializeComponent();
		}

		public void Init(frmCUEPlayer parent)
		{
			_config = parent.Config;
			playlistModel = parent.PlaylistModel;
			MdiParent = parent;
			Show();
			_icon_mgr = parent.IconMgr;
			listViewTracks.SmallImageList = _icon_mgr.ImageList;
			foreach (PlaylistEntry entry in playlistModel)
				listViewTracks.Items.Add(ToItem(entry));
		}

		public ListView List
		{
			get
			{
				return listViewTracks;
			}
		}

		public ListViewItem ToItem(PlaylistEntry entry)
		{
			ListViewGroup in_group = null;
			string group_name = entry.Artist + " - " + entry.Album;
			try
			{
				foreach (ListViewGroup group in listViewTracks.Groups)
				{
					if (group.Name == group_name)
					{
						in_group = group;
						break;
					}
				}
				if (in_group == null)
				{
					in_group = new ListViewGroup(group_name, group_name);
					listViewTracks.Groups.Add(in_group);
				}
			}
			catch (ArgumentException ex)
			{
				Trace.WriteLine(
					"Playlist grouping unavailable (" +
					ex.GetType().Name + ").");
				in_group = null;
			}
			catch (InvalidOperationException ex)
			{
				Trace.WriteLine(
					"Playlist grouping unavailable (" +
					ex.GetType().Name + ").");
				in_group = null;
			}

			Exception iconFailure;
			ListViewItem item = PlaylistItemFactory.Create(
				entry,
				delegate(PlaylistEntry candidate)
				{
					return _icon_mgr.GetIconIndex(
						new FileInfo(candidate.Path),
						true);
				},
				in_group,
				out iconFailure);
			if (iconFailure != null)
			{
				Trace.WriteLine(
					"Playlist icon unavailable (" +
					iconFailure.GetType().Name + ").");
			}
			return item;
		}

		private PlaylistEntry AddAndDisplay(
			string path,
			string artist,
			string title,
			string album,
			int lengthSeconds,
			int trackNumber)
		{
			PlaylistEntry entry = playlistModel.Add(
				path,
				artist,
				title,
				album,
				lengthSeconds,
				trackNumber);
			try
			{
				listViewTracks.Items.Add(ToItem(entry));
			}
			catch
			{
				playlistModel.Remove(entry);
				throw;
			}
			(MdiParent as frmCUEPlayer).PlaylistChanged();
			return entry;
		}

		private void exploreToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (listViewTracks.SelectedIndices.Count == 1)
			{
				int index = listViewTracks.SelectedIndices[0];
				PlaylistEntry entry =
					listViewTracks.Items[index].Tag as PlaylistEntry;
				if (entry != null)
				{
					(MdiParent as frmCUEPlayer).browser.TreeView.SelectedPath =
						entry.Path;
				}
			}
		}

		private void removeToolStripMenuItem_Click(object sender, EventArgs e)
		{
			while (listViewTracks.SelectedIndices.Count > 0)
			{
				int index = listViewTracks.SelectedIndices[0];
				PlaylistEntry entry =
					listViewTracks.Items[index].Tag as PlaylistEntry;
				if (entry == null || !playlistModel.Remove(entry))
				{
					Trace.WriteLine(
						"Playlist row identity was unavailable; removal stopped.");
					return;
				}
				listViewTracks.Items.RemoveAt(index);
				(MdiParent as frmCUEPlayer).PlaylistChanged();
			}
		}

		private void listViewTracks_DragDrop(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
				if (files.Length == 1)
				{
					string path = files[0];
					CUESheet cue = null;
					try
					{
						cue = new CUESheet(_config);
						cue.Open(path);
					}
					catch (Exception ex)
					{
						if (cue != null)
						{
							try
							{
								cue.Close();
							}
							catch (Exception closeFailure)
							{
								Trace.WriteLine(
									"Playlist CUE cleanup failed (" +
									closeFailure.GetType().Name + ").");
							}
						}
						cue = null;
						Trace.WriteLine("Playlist CUE probe failed (" +
							ex.GetType().Name + ").");
					}

					if (cue != null)
					{
						try
						{
							for (int iTrack = 0;
								iTrack < cue.TrackCount;
								iTrack++)
							{
								AddAndDisplay(
									path,
									cue.Metadata.Artist,
									cue.Metadata.Tracks[iTrack].Title,
									cue.Metadata.Title,
									(int)cue.TOC[
										cue.TOC.FirstAudio + iTrack].Length / 75,
									iTrack + 1);
							}
						}
						finally
						{
							cue.Close();
						}
						return;
					}

					FileInfo fi = new FileInfo(path);
					if (!String.Equals(
						fi.Extension,
						".cue",
						StringComparison.OrdinalIgnoreCase))
					{
						AddAndDisplay(
							path,
							null, // cue.Artist,
							null, // cue.Tracks[iTrack].Title,
							null, // cue.Title,
							0, // (int)cue.TOC[cue.TOC.FirstAudio + iTrack].Length / 75,
							0);
					}
				}
			}
		}

		private void listViewTracks_DragOver(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				e.Effect = DragDropEffects.Copy;
			}
		}

		private void listViewTracks_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Delete)
				removeToolStripMenuItem_Click(sender, EventArgs.Empty);
		}

		private void listViewTracks_ItemDrag(object sender, ItemDragEventArgs e)
		{
			if (e.Item != null && e.Item is ListViewItem)
			{
				PlaylistEntry entry =
					(e.Item as ListViewItem).Tag as PlaylistEntry;
				if (entry == null)
					return;
				DataObject dobj = new DataObject();
				dobj.SetData(EntryDragFormat, false, entry);
				DragDropEffects effects = DoDragDrop(dobj, DragDropEffects.All);
				return;
			}
		}

		private void Playlist_Load(object sender, EventArgs e)
		{

		}
	}
}
