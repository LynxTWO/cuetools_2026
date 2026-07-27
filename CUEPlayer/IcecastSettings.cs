using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using CUETools.Codecs.Icecast;

namespace CUEPlayer
{
	public partial class IcecastSettings : Form
	{
		private readonly IcecastSettingsData _target;
		private readonly IcecastSettingsData _data;
		private bool _clearPassword;

		public IcecastSettings(IcecastSettingsData data)
		{
			InitializeComponent();
			if (data == null)
				throw new ArgumentNullException("data");
			_target = data;
			// Bind controls to a draft. Binding directly to the persisted object made Cancel
			// commit every field that had already lost focus.
			_data = Copy(data);
			icecastSettingsDataBindingSource.DataSource = _data;
			textBoxPassword.Text = "";
			labelPassword.Text = String.IsNullOrEmpty(data.Password) ? "Password" : "Password (set)";
			checkBoxAllowInsecureHttp.Checked = data.AllowInsecureHttp;
			UpdateInsecureWarning();
		}

		private void IcecastSettings_Load(object sender, EventArgs e)
		{

		}

		private void buttonClearPassword_Click(object sender, EventArgs e)
		{
			_clearPassword = true;
			textBoxPassword.Text = "";
			labelPassword.Text = "Password";
		}

		private void checkBoxAllowInsecureHttp_CheckedChanged(object sender, EventArgs e)
		{
			UpdateInsecureWarning();
		}

		private void UpdateInsecureWarning()
		{
			labelInsecureWarning.Visible = checkBoxAllowInsecureHttp.Checked;
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (DialogResult == DialogResult.OK)
			{
				int bitrate;
				if (!Int32.TryParse(textBoxMP3Options.Text, out bitrate) ||
					!IcecastSettingsData.IsSupportedBitrate(bitrate))
				{
					MessageBox.Show(this,
						"MP3 bitrate must be 96, 128, 192, 256, or 320 kbps.",
						"Icecast settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					e.Cancel = true;
					return;
				}
				Validate();
				icecastSettingsDataBindingSource.EndEdit();
				_data.Bitrate = bitrate;
				_data.JointStereo = checkBoxJointStereo.Checked;
				if (_clearPassword)
					_data.Password = "";
				if (!String.IsNullOrEmpty(textBoxPassword.Text))
					_data.Password = textBoxPassword.Text;
				_data.AllowInsecureHttp = checkBoxAllowInsecureHttp.Checked;
				Apply(_data, _target);
			}
			base.OnFormClosing(e);
		}

		internal static IcecastSettingsData Copy(IcecastSettingsData source)
		{
			var copy = new IcecastSettingsData();
			Apply(source, copy);
			return copy;
		}

		internal static void Apply(
			IcecastSettingsData source,
			IcecastSettingsData target)
		{
			target.Server = source.Server;
			target.Port = source.Port;
			target.Password = source.Password;
			target.Mount = source.Mount;
			target.Name = source.Name;
			target.Description = source.Description;
			target.Url = source.Url;
			target.Genre = source.Genre;
			target.Bitrate = source.Bitrate;
			target.JointStereo = source.JointStereo;
			target.AllowInsecureHttp = source.AllowInsecureHttp;
		}
	}
}
