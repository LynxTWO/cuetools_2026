using System;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace CUEPlayer
{
	internal static class LegacyPlaylistImporter
	{
		private const string ConnectionTypeName =
			"System.Data.SqlServerCe.SqlCeConnection, System.Data.SqlServerCe";

		public static bool TryImport(
			string databasePath,
			out PlaylistModel playlist,
			out Exception failure)
		{
			return TryImport(
				databasePath,
				delegate
				{
					return Type.GetType(ConnectionTypeName, false);
				},
				out playlist,
				out failure);
		}

		internal static bool TryImport(
			string databasePath,
			Func<Type> connectionTypeResolver,
			out PlaylistModel playlist,
			out Exception failure)
		{
			playlist = null;
			failure = null;
			if (connectionTypeResolver == null)
				throw new ArgumentNullException("connectionTypeResolver");

			try
			{
				Type connectionType = connectionTypeResolver();
				if (connectionType == null)
					return false;

				IDbConnection connection =
					Activator.CreateInstance(connectionType) as IDbConnection;
				if (connection == null)
					return false;

				using (connection)
				{
					DbConnectionStringBuilder connectionString =
						new DbConnectionStringBuilder();
					connectionString["Data Source"] = databasePath;
					connectionString["File Mode"] = "Read Only";
					connection.ConnectionString =
						connectionString.ConnectionString;
					connection.Open();

					using (IDbCommand command = connection.CreateCommand())
					{
						command.CommandText =
							"SELECT [path], [artist], [title], [album], " +
							"[length], [track] FROM [Playlist] ORDER BY [id]";
						using (IDataReader reader = command.ExecuteReader())
						{
							PlaylistModel imported = new PlaylistModel();
							while (reader.Read())
							{
								if (imported.Count >=
									PlaylistModel.MaximumEntryCount)
								{
									throw new InvalidOperationException(
										"The legacy playlist contains too many entries.");
								}
								imported.Add(ReadEntry(reader));
							}
							playlist = imported;
						}
					}
				}
				return true;
			}
			catch (Exception ex)
			{
				failure = ex;
				playlist = null;
				return false;
			}
		}

		internal static PlaylistEntry ReadEntry(IDataRecord record)
		{
			if (record == null)
				throw new ArgumentNullException("record");
			return new PlaylistEntry(
				ReadRequiredString(record, 0),
				ReadOptionalString(record, 1),
				ReadOptionalString(record, 2),
				ReadOptionalString(record, 3),
				ReadNonNegativeInt(record, 4),
				ReadNonNegativeInt(record, 5));
		}

		private static string ReadRequiredString(
			IDataRecord record,
			int ordinal)
		{
			string value = ReadOptionalString(record, ordinal);
			if (String.IsNullOrEmpty(value))
				throw new InvalidOperationException(
					"The legacy playlist contains an empty path.");
			return value;
		}

		private static string ReadOptionalString(
			IDataRecord record,
			int ordinal)
		{
			if (record.IsDBNull(ordinal))
				return String.Empty;
			return Convert.ToString(
				record.GetValue(ordinal),
				CultureInfo.InvariantCulture) ?? String.Empty;
		}

		private static int ReadNonNegativeInt(
			IDataRecord record,
			int ordinal)
		{
			if (record.IsDBNull(ordinal))
				return 0;
			int value = Convert.ToInt32(
				record.GetValue(ordinal),
				CultureInfo.InvariantCulture);
			if (value < 0)
				throw new InvalidOperationException(
					"The legacy playlist contains a negative number.");
			return value;
		}
	}
}
