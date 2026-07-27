using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.Net;
using CUETools.Codecs;
using CUETools.Codecs.Icecast;

namespace CUEPlayer
{
	public partial class Icecast : Form
	{
		private volatile IcecastWriter _icecastWriter;
		private IcecastSettingsData _icecastSettings;
		private CUETools.DSP.Mixer.MixingSource _mixer;
		private Thread flushThread;
		private AudioPipe buffer;
		private volatile bool close = false;
		private volatile bool abortClose = false;
		private bool updatingTransmitState = false;
		private int latency = 0;

		public Icecast()
		{
			InitializeComponent();
		}

		private void Icecast_Load(object sender, EventArgs e)
		{
			if (Properties.Settings.Default.IcecastSettings == null)
				Properties.Settings.Default.IcecastSettings = new IcecastSettingsData();
			IcecastCredentialStore.Load();
			_icecastSettings = Properties.Settings.Default.IcecastSettings;
		}

		public void Init(frmCUEPlayer parent)
		{
			MdiParent = parent;
			Show();
			_mixer = parent.Mixer;
			buffer = new AudioPipe(_mixer.PCM, _mixer.PCM.SampleRate * 10); // 10 secs
			_mixer.AudioRead += new EventHandler<CUETools.DSP.Mixer.AudioReadEventArgs>(Mixer_AudioRead);
			parent.updateMetadata += new EventHandler<UpdateMetadataEvent>(parent_updateMetadata);

			flushThread = new Thread(FlushThread);
			flushThread.Priority = ThreadPriority.AboveNormal;
			flushThread.IsBackground = true;
			flushThread.Name = "Icecast";
			flushThread.Start();
		}

		void parent_updateMetadata(object sender, UpdateMetadataEvent e)
		{
			if (_icecastWriter != null)
			{
				try
				{
					_icecastWriter.UpdateMetadata(e.artist, e.title);
				}
				catch (Exception metadataException)
				{
					// Metadata is ancillary to the audio stream. Keep playback and
					// broadcasting alive, and never log the credential-bearing
					// request or an exception message.
					Trace.WriteLine(
						"Icecast metadata update failed: " +
						metadataException.GetType().Name);
				}
			}
		}

		private void FlushThread()
		{
			AudioBuffer result = new AudioBuffer(_mixer.PCM, _mixer.BufferSize);
			while (true)
			{
				buffer.Read(result, -1);
				IcecastWriter writer = _icecastWriter;
				IcecastWriter failedWriter = null;
				if (writer != null && !close)
				{
					try
					{
						writer.Write(result);
					}
					catch (Exception writeException)
					{
						Trace.WriteLine(
							"Icecast streaming write failed: " +
							writeException.GetType().Name);
						abortClose = true;
						close = true;
						failedWriter = writer;
					}
				}
				if (_icecastWriter != null && (close || failedWriter != null))
				{
					writer = _icecastWriter;
					bool abort = abortClose;
					// Publish the stopped state before network finalization. A metadata event or
					// timer must not race the encoder while its final MP3 bytes are being flushed.
					_icecastWriter = null;
					if (failedWriter != null)
						SetTransmitStoppedState(
							failedWriter,
							"Streaming stopped",
							"The Icecast stream stopped after a network or encoder write failure.");
					try
					{
						if (abort)
							writer.Delete();
						else
							writer.Close();
					}
					catch (Exception cleanupException)
					{
						Trace.WriteLine(
							"Icecast streaming cleanup failed: " +
							cleanupException.GetType().Name);
					}
					finally
					{
						abortClose = false;
					}
				}
			}
		}

		void Mixer_AudioRead(object sender, CUETools.DSP.Mixer.AudioReadEventArgs e)
		{
			latency = buffer.Write(e.buffer);
			//int bs = buffer.Write(e.buffer);
			//if (bs > 0)
			//{
			//    Trace.WriteLine(string.Format("buffer size {0}", bs));
			//}
		}

		private void timer1_Tick(object sender, EventArgs e)
		{
			textBoxBytes.Text = _icecastWriter == null ? "" : string.Format("{0}K", _icecastWriter.BytesWritten/1024);
			textBoxLatency.Text = (_icecastWriter == null || latency == 0 ) ? "" : string.Format("{0}", 1.0 * latency / buffer.PCM.SampleRate);
		}

		private void checkBoxTransmit_CheckedChanged(object sender, EventArgs e)
		{
			if (updatingTransmitState)
				return;

			close = !checkBoxTransmit.Checked;
			this.toolTip1.SetToolTip(this.checkBoxTransmit, "");
			if (!close && _icecastWriter == null)
			{
				IcecastWriter icecastWriter = null;
				try
				{
					icecastWriter = new IcecastWriter(
						_mixer.PCM, _icecastSettings);
					icecastWriter.Connect();
					int sourceStatus =
						(int)icecastWriter.ProtocolResponse.StatusCode;
					if (sourceStatus >= 200 && sourceStatus < 300)
					{
						abortClose = false;
						_icecastWriter = icecastWriter;
					}
					else
					{
						HttpStatusCode statusCode = icecastWriter.ProtocolResponse.StatusCode;
						string statusDescription = icecastWriter.ProtocolResponse.StatusDescription;
						try
						{
							icecastWriter.Delete();
						}
						catch (Exception cleanupException)
						{
							Trace.WriteLine(
								"Icecast rejected-connection cleanup failed: " +
								cleanupException.GetType().Name);
						}
						SetTransmitStoppedState(
							null,
							statusCode.ToString(),
							statusDescription);
					}
				}
				catch (Exception ex)
				{
					Trace.WriteLine("Icecast connection failed: " + ex.GetType().Name);
					if (icecastWriter != null)
					{
						try
						{
							icecastWriter.Close();
						}
						catch (Exception cleanupException)
						{
							Trace.WriteLine(
								"Icecast connection cleanup failed: " +
								cleanupException.GetType().Name);
						}
					}
					SetTransmitStoppedState(
						null,
						"Connection failed",
						"Connection failed. Check the server and credential settings.");
				}
			}
		}

		private void SetTransmitStoppedState(
			IcecastWriter expectedWriter,
			string title,
			string description)
		{
			if (IsDisposed || Disposing || !IsHandleCreated)
				return;

			if (InvokeRequired)
			{
				try
				{
					BeginInvoke((MethodInvoker)delegate
					{
						ApplyTransmitStoppedState(
							expectedWriter,
							title,
							description);
					});
				}
				catch (ObjectDisposedException)
				{
					// The form was closed between the handle check and BeginInvoke.
				}
				catch (InvalidOperationException)
				{
					// The form was closed between the handle check and BeginInvoke.
				}
				return;
			}

			ApplyTransmitStoppedState(expectedWriter, title, description);
		}

		private void ApplyTransmitStoppedState(
			IcecastWriter expectedWriter,
			string title,
			string description)
		{
			if (IsDisposed || Disposing)
				return;

			// Ignore a delayed worker callback if the user has already established a different
			// stream. Otherwise a stale failure could turn off the replacement connection.
			if (expectedWriter != null &&
				_icecastWriter != null &&
				!Object.ReferenceEquals(_icecastWriter, expectedWriter))
				return;

			close = true;
			updatingTransmitState = true;
			try
			{
				checkBoxTransmit.Checked = false;
			}
			finally
			{
				updatingTransmitState = false;
			}

			toolTip1.ToolTipIcon = ToolTipIcon.Error;
			toolTip1.ToolTipTitle = title;
			toolTip1.IsBalloon = true;
			toolTip1.SetToolTip(checkBoxTransmit, description);
		}

		private void buttonSettings_Click(object sender, EventArgs e)
		{
			IcecastSettingsData original =
				IcecastSettings.Copy(_icecastSettings);
			IcecastSettings frm = new IcecastSettings(_icecastSettings);
			if (frm.ShowDialog(this) == DialogResult.OK)
			{
				try
				{
					IcecastCredentialStore.Save();
				}
				catch (Exception ex)
				{
					// The dialog applies its draft before returning OK so the settings serializer
					// can see it. Restore the live object if durable credential/settings storage
					// fails; otherwise "not saved" would still change the active stream settings.
					IcecastSettings.Apply(original, _icecastSettings);
					Trace.WriteLine("Icecast settings save failed: " + ex.GetType().Name);
					MessageBox.Show(this,
						"The Icecast credential could not be protected for this Windows user. Settings were not saved.",
						"Icecast settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}
	}
}
