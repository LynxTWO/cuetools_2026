using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CUETools.Codecs.Icecast;

namespace CUEPlayer
{
    /// <summary>Keeps Icecast passwords out of ApplicationSettingsBase XML. Non-secret server
    /// settings retain their existing serialized shape; only the password values move into one
    /// CurrentUser DPAPI blob.</summary>
    internal static class IcecastCredentialStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CUEPlayer.IcecastPasswords.v1");
        private const string Prefix = "dpapi-v1:";
        private const int MaximumProtectedCharacters = 262144;
        private const int MaximumPayloadBytes = 131072;
        private const int MaximumPasswordCharacters = 16384;

        public static void Load()
        {
            string protectedValue = Properties.Settings.Default.IcecastCredentialsProtected;
            if (String.IsNullOrEmpty(protectedValue))
            {
                if (HasInMemoryPassword())
                {
                    try
                    {
                        Save();
                        System.Diagnostics.Trace.WriteLine(
                            "Legacy Icecast credential migrated to current-user protection.");
                    }
                    catch (Exception ex)
                    {
                        // Keep the legacy value in memory and leave the old user.config in place.
                        // Unsupported platforms and failed profile writes must not erase a working
                        // credential merely because migration was unavailable.
                        System.Diagnostics.Trace.WriteLine(
                            "Legacy Icecast credential migration failed: " +
                            ex.GetType().Name);
                    }
                }
                return;
            }

            try
            {
                if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
                    throw new CryptographicException("Unsupported Icecast credential format.");
                if (protectedValue.Length > MaximumProtectedCharacters)
                    throw new CryptographicException("Protected Icecast credential is too large.");
                byte[] ciphertext =
                    Convert.FromBase64String(protectedValue.Substring(Prefix.Length));
                try
                {
                    if (ciphertext.Length > MaximumPayloadBytes * 2)
                        throw new CryptographicException(
                            "Protected Icecast credential is too large.");
                    byte[] plaintext = ProtectedData.Unprotect(
                        ciphertext, Entropy, DataProtectionScope.CurrentUser);
                    try
                    {
                        if (plaintext.Length > MaximumPayloadBytes)
                            throw new CryptographicException(
                                "Icecast credential payload is too large.");
                        ApplyPayload(plaintext);
                    }
                    finally
                    {
                        Array.Clear(plaintext, 0, plaintext.Length);
                    }
                }
                finally
                {
                    Array.Clear(ciphertext, 0, ciphertext.Length);
                }
            }
            catch (Exception)
            {
                ClearInMemoryPasswords();
                System.Diagnostics.Trace.WriteLine(
                    "Icecast credential unavailable; clear it and set it again for this Windows user.");
            }
        }

        public static void Save()
        {
            List<IcecastSettingsData> settings = GetSettingsObjects();
            var originalPasswords = new Dictionary<IcecastSettingsData, string>();
            foreach (IcecastSettingsData item in settings)
                originalPasswords[item] = item.Password ?? "";
            string originalProtectedValue =
                Properties.Settings.Default.IcecastCredentialsProtected;

            string protectedValue = ProtectPayload(settings);
            try
            {
                foreach (IcecastSettingsData item in settings)
                    item.Password = "";
                // These are mutable objects. Reassign them so ApplicationSettingsBase marks the
                // user-scoped values dirty and cannot leave legacy plaintext in an old user.config.
                Properties.Settings.Default.IcecastSettings =
                    Properties.Settings.Default.IcecastSettings;
                Properties.Settings.Default.AppSettings =
                    Properties.Settings.Default.AppSettings;
                Properties.Settings.Default.IcecastCredentialsProtected = protectedValue;
                Properties.Settings.Default.Save();
            }
            catch
            {
                // Keep the in-memory settings graph internally consistent when the provider
                // rejects the write. The caller separately restores the edited server fields.
                Properties.Settings.Default.IcecastCredentialsProtected =
                    originalProtectedValue;
                throw;
            }
            finally
            {
                foreach (KeyValuePair<IcecastSettingsData, string> item in originalPasswords)
                    item.Key.Password = item.Value;
            }
        }

        private static string ProtectPayload(List<IcecastSettingsData> settings)
        {
            bool hasSecret = false;
            using (var payload = new MemoryStream())
            {
                using (var writer = new BinaryWriter(payload, Encoding.UTF8, true))
                {
                    writer.Write(1);
                    writer.Write(settings.Count);
                    foreach (IcecastSettingsData item in settings)
                    {
                        string password = item.Password ?? "";
                        if (password.Length > MaximumPasswordCharacters)
                            throw new CryptographicException(
                                "An Icecast credential is too large to protect.");
                        hasSecret |= password.Length != 0;
                        writer.Write(password);
                    }
                }

                if (!hasSecret)
                    return "";

                byte[] plaintext = payload.ToArray();
                try
                {
                    if (plaintext.Length > MaximumPayloadBytes)
                        throw new CryptographicException(
                            "Icecast credential payload is too large.");
                    byte[] ciphertext = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
                    try
                    {
                        return Prefix + Convert.ToBase64String(ciphertext);
                    }
                    finally
                    {
                        Array.Clear(ciphertext, 0, ciphertext.Length);
                    }
                }
                finally
                {
                    Array.Clear(plaintext, 0, plaintext.Length);
                }
            }
        }

        private static void ApplyPayload(byte[] payload)
        {
            List<IcecastSettingsData> settings = GetSettingsObjects();
            ClearInMemoryPasswords();
            using (var stream = new MemoryStream(payload, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadInt32() != 1)
                    throw new InvalidDataException("Unsupported Icecast credential version.");
                int count = reader.ReadInt32();
                if (count < 0 || count > settings.Count || count > 1024)
                    throw new InvalidDataException("Invalid Icecast credential count.");
                for (int i = 0; i < count; i++)
                {
                    string password = reader.ReadString();
                    if (password.Length > MaximumPasswordCharacters)
                        throw new InvalidDataException("Icecast credential is too large.");
                    settings[i].Password = password;
                }
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("Trailing Icecast credential data.");
            }
        }

        private static List<IcecastSettingsData> GetSettingsObjects()
        {
            var result = new List<IcecastSettingsData>();
            AddOnce(result, Properties.Settings.Default.IcecastSettings);
            CUEPlayerSettings app = Properties.Settings.Default.AppSettings;
            if (app != null && app.IcecastServers != null)
                foreach (IcecastSettingsData item in app.IcecastServers)
                    AddOnce(result, item);
            return result;
        }

        private static void AddOnce(List<IcecastSettingsData> result, IcecastSettingsData item)
        {
            if (item != null && !result.Contains(item))
                result.Add(item);
        }

        private static void ClearInMemoryPasswords()
        {
            foreach (IcecastSettingsData item in GetSettingsObjects())
                item.Password = "";
        }

        private static bool HasInMemoryPassword()
        {
            foreach (IcecastSettingsData item in GetSettingsObjects())
                if (!String.IsNullOrEmpty(item.Password))
                    return true;
            return false;
        }
    }
}
