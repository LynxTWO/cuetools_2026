using System;
using System.Threading;
using System.Windows.Forms;

namespace CUEPlayer
{
	internal static class PlaylistItemFactory
	{
		public static ListViewItem Create(
			PlaylistEntry entry,
			Func<PlaylistEntry, int> iconResolver,
			ListViewGroup group,
			out Exception iconFailure)
		{
			if (entry == null)
				throw new ArgumentNullException("entry");

			iconFailure = null;
			int iconIndex = -1;
			if (iconResolver != null)
			{
				try
				{
					iconIndex = iconResolver(entry);
				}
				catch (Exception ex)
				{
					if (IsFatal(ex))
						throw;
					iconFailure = ex;
				}
			}

			ListViewItem item = new ListViewItem(entry.Title);
			if (iconIndex >= 0)
				item.ImageIndex = iconIndex;
			TimeSpan length = TimeSpan.FromSeconds(entry.LengthSeconds);
			string lengthText = String.Format(
				"{0:d}.{1:d2}:{2:d2}:{3:d2}",
				length.Days,
				length.Hours,
				length.Minutes,
				length.Seconds).TrimStart('0', ':', '.');
			item.SubItems.Add(
				new ListViewItem.ListViewSubItem(item, lengthText));
			item.Group = group;
			item.Tag = entry;
			return item;
		}

		private static bool IsFatal(Exception failure)
		{
			return failure is OutOfMemoryException ||
				failure is StackOverflowException ||
				failure is AccessViolationException ||
				failure is AppDomainUnloadedException ||
				failure is ThreadAbortException;
		}
	}
}
