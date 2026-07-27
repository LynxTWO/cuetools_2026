using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using CUETools.Codecs;
using CUETools.Processor;
using CUETools.DSP.Mixer;

namespace CUEPlayer
{
	public partial class Deck : Form
	{
		IAudioSource playingSource = null;
		CUESheet playingCue = null;
		PlaylistEntry playingEntry = null;
		long playingStart = 0;
		long playingFinish = 0;
		Thread playThread;
		int iSource;
		AudioBuffer buff;
		MixingSource mixer;
		MixingWriter writer;
		Deck nextDeck;
		bool needUpdate = false;

		public Deck(int iSource, string suffix)
		{
			InitializeComponent();
			this.iSource = iSource;
			if (suffix != null)
				Text += " " + suffix;
			//mediaSliderA.FlyOutInfo += new MediaSlider.MediaSlider.FlyOutInfoDelegate(mediaSliderA_FlyOutInfo);
		}

		public void Init(frmCUEPlayer parent, Deck nextDeck)
		{
			MdiParent = parent;
			mixer = (parent as frmCUEPlayer).Mixer;
			writer = new MixingWriter(mixer, iSource);
			this.nextDeck = nextDeck;
			Show();
		}

		//void mediaSliderA_FlyOutInfo(ref string data)
		//{
		//    TimeSpan ts = TimeSpan.FromSeconds(mediaSliderA.Value / 44100.0);
		//    data = ts.ToString();
		//}

		internal int PlayingOffset
		{
			set
			{
				if (!mediaSlider.Capture) mediaSlider.Value = value;
			}
		}

		internal void UpdateDeck()
		{
			if (needUpdate)
			{
				needUpdate = false;

				PlaylistModel playlist =
					(MdiParent as frmCUEPlayer).PlaylistModel;

				mediaSlider.Maximum = (int)(playingFinish - playingStart);
				mediaSlider.Value = 0;
				textBoxArtist.Text =
					playingEntry == null ? "" : playingEntry.Artist;
				textBoxAlbum.Text =
					playingEntry == null ? "" : playingEntry.Album;
				textBoxTitle.Text =
					playingEntry == null ? "" : playingEntry.Title;
				textBoxDuration.Text = "";
				pictureBox.Image = playingCue != null && playingCue.Cover != null ? playingCue.Cover : pictureBox.InitialImage;

				PlaylistEntry nextEntry = playlist.GetNext(playingEntry);
				if (nextDeck != null &&
					nextDeck.playingSource == null &&
					nextEntry != null)
				{
					nextDeck.LoadDeck(nextEntry);
				}

				if (playThread != null)
					(MdiParent as frmCUEPlayer).UpdateMetadata(textBoxArtist.Text, textBoxTitle.Text);
			}
			mediaSlider.Enabled = playingSource != null;
			if (playingSource != null)
				mediaSlider.Value = (int)(playingSource.Position - playingStart);
		}

		private void mediaSliderA_ValueChanged(object sender, EventArgs e)
		{
			if (mediaSlider.Maximum == 1) return;
			TimeSpan len1 = TimeSpan.FromSeconds(mediaSlider.Maximum / 44100.0);
			TimeSpan len2 = TimeSpan.FromSeconds(mediaSlider.Value / 44100.0);
			string lenStr1 = string.Format("{0:d}.{1:d2}:{2:d2}:{3:d2}", len1.Days, len1.Hours, len1.Minutes, len1.Seconds).TrimStart('0', ':', '.');
			string lenStr2 = string.Format("{0:d}.{1:d2}:{2:d2}:{3:d2}", len2.Days, len2.Hours, len2.Minutes, len2.Seconds).TrimStart('0', ':', '.');
			lenStr1 = "0:00".Substring(0, Math.Max(0, 4 - lenStr1.Length)) + lenStr1;
			lenStr2 = "0:00".Substring(0, Math.Max(0, 4 - lenStr2.Length)) + lenStr2;
			textBoxDuration.Text = lenStr2 + " / " + lenStr1;
		}

		private int seekTo = -1;
		private bool stopNow = false;

		private void PlayThread()
		{
			try
			{
				do
				{
					if (playingSource == null)
						writer.Pause();
					else
					{
						if (seekTo >= 0 && playingStart + seekTo < playingFinish)
						{
							playingSource.Position = playingStart + seekTo;
							seekTo = -1;
						}
						if (playingSource.Position == playingFinish || stopNow || seekTo == (int)(playingFinish - playingStart))
						{
							seekTo = -1;
							playingSource.Close();
							playingSource = null;
							if (playingCue != null)
							{
								playingCue.Close();
								playingCue = null;
							}
							playingFinish = 0;
							playingStart = 0;
							playingEntry = null;
							if (stopNow || nextDeck == null || nextDeck.playingSource == null)
							{
								writer.Flush();
								stopNow = false;
								mixer.BufferPlaying(iSource, false);
								needUpdate = true;
								playThread = null;
								return;
							}
							playingSource = nextDeck.playingSource;
							playingCue = nextDeck.playingCue;
							playingStart = nextDeck.playingStart;
							playingFinish = nextDeck.playingFinish;
							playingEntry = nextDeck.playingEntry;
							needUpdate = true;
							nextDeck.playingSource = null;
							nextDeck.playingCue = null;
							nextDeck.playingStart = 0;
							nextDeck.playingFinish = 0;
							nextDeck.playingEntry = null;
							nextDeck.needUpdate = true;
						}
						if (buff == null || buff.PCM.SampleRate != playingSource.PCM.SampleRate || buff.PCM.ChannelCount != playingSource.PCM.ChannelCount || buff.PCM.BitsPerSample != playingSource.PCM.BitsPerSample)
							buff = new AudioBuffer(playingSource.PCM, 0x2000);
						playingSource.Read(buff, Math.Min(buff.Size, (int)(playingFinish - playingSource.Position)));
						writer.Write(buff);
					}
				} while (true);
			}
			catch (Exception)
			{
			}
			if (playingCue != null)
			{
				playingCue.Close();
				playingCue = null;
			}
			if (playingSource != null)
			{
				playingSource.Close();
				playingSource = null;
			}
			playThread = null;
		}

		internal void LoadDeck(PlaylistEntry entry)
		{
			if (entry == null)
				throw new ArgumentNullException("entry");
			CUEConfig _config = (MdiParent as frmCUEPlayer).Config;
			string path = entry.Path;
			int track = entry.TrackNumber;

			try
			{
				playingCue = new CUESheet(_config);
				playingCue.Open(path);
				playingSource = new CUESheetAudio(playingCue);
				playingSource.Position = (long)playingCue.TOC[track].Start * 588;
				playingSource = new AudioPipe(playingSource, 0x2000);
				playingStart = playingSource.Position;
				playingFinish = playingStart + (long)playingCue.TOC[track].Length * 588;
				playingEntry = entry;
				needUpdate = true;
				UpdateDeck();
			}
			catch (Exception)
			{
				playingStart = playingFinish = 0;
				playingCue = null;
				playingSource = null;
				playingEntry = null;
				return;
			}
		}

		private void buttonPlay_Click(object sender, EventArgs e)
		{
			if (playingSource == null)
			{
				Playlist playlist = (MdiParent as frmCUEPlayer).wndPlaylist;
				if (playlist.List.SelectedItems.Count == 0)
					return;
				PlaylistEntry selectedEntry =
					playlist.List.SelectedItems[0].Tag as PlaylistEntry;
				if (selectedEntry == null)
					return;
				LoadDeck(selectedEntry);
				if (playingSource == null)
					return;
			}
			if (playThread == null)
			{
				playThread = new Thread(PlayThread);
				playThread.Priority = ThreadPriority.AboveNormal;
				playThread.IsBackground = true;
				playThread.Name = Text;
				playThread.Start();
			}
			mixer.BufferPlaying(iSource, true);
		}

		private void buttonStop_Click(object sender, EventArgs e)
		{
			if (playThread != null)
			{
				stopNow = true;
				playThread.Join();
			}
			else
			{
				if (playingSource != null)
				{
					playingSource.Close();
					playingSource = null;
				}
				if (playingCue != null)
				{
					playingCue.Close();
					playingCue = null;
				}
				playingFinish = 0;
				playingStart = 0;
				playingEntry = null;
				needUpdate = true;
				UpdateDeck();
			}
		}

		private void buttonPause_Click(object sender, EventArgs e)
		{
			mixer.BufferPlaying(iSource, false);
		}

		private void timer1_Tick(object sender, EventArgs e)
		{
			UpdateDeck();
		}

		private void mediaSlider_Scrolled(object sender, EventArgs e)
		{
			if (playThread != null)
			{
				seekTo = mediaSlider.Value;
			}
			else
			{
				if (playingSource != null)
					playingSource.Position = playingStart + mediaSlider.Value;
			}
		}

		private void mediaSliderVolume_Scrolled(object sender, EventArgs e)
		{
			writer.Volume = mediaSliderVolume.Value / 100.0f;
		}

		private void Deck_DragOver(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(Playlist.EntryDragFormat))
			{
				e.Effect = DragDropEffects.Copy;
			}
		}

		private void Deck_DragDrop(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(Playlist.EntryDragFormat))
			{
				PlaylistEntry entry =
					e.Data.GetData(Playlist.EntryDragFormat) as PlaylistEntry;
				if (playThread == null && entry != null)
				{
					LoadDeck(entry);
				}
			}
		}

		private void buttonNext_Click(object sender, EventArgs e)
		{
			seekTo = (int)(playingFinish - playingStart);
		}

		private void buttonRewind_Click(object sender, EventArgs e)
		{
			if (playThread != null)
			{
				seekTo = 0;
			}
			else if (playingSource != null)
			{
				playingSource.Position = playingStart;
			}
		}

		private void Deck_Load(object sender, EventArgs e)
		{

		}
	}
}
