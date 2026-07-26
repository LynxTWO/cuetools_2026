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
		private readonly IcecastSettingsData _data;
		private bool _clearPassword;

		public IcecastSettings(IcecastSettingsData data)
		{
			InitializeComponent();
			_data = data;
			icecastSettingsDataBindingSource.DataSource = data;
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
				if (_clearPassword)
					_data.Password = "";
				if (!String.IsNullOrEmpty(textBoxPassword.Text))
					_data.Password = textBoxPassword.Text;
				_data.AllowInsecureHttp = checkBoxAllowInsecureHttp.Checked;
			}
			base.OnFormClosing(e);
		}
	}
}
