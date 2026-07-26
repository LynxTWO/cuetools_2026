using System;
using System.Security.Cryptography;
using System.Text;

namespace CUETools.Processor.Settings
{
    /// <summary>
    /// Protects the classic CUETools/CUERipper proxy credential for the current Windows user.
    /// Unsupported platforms never fall back to writing the password in plaintext.
    /// </summary>
    internal static class ProxyCredentialStore
    {
        internal const string SettingsKey = "ProxyPasswordProtected";

        private const string Prefix = "dpapi-v1:";
        private const int MaximumSecretCharacters = 16384;
        private const int MaximumSecretBytes = 65536;
        private const int MaximumProtectedCharacters = 131072;
        private const int MaximumCiphertextBytes = 98304;
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("CUETools2026.ProxyPassword.v1");

        internal static bool CanProtectCurrentUser
        {
            get { return Environment.OSVersion.Platform == PlatformID.Win32NT; }
        }

        internal static string ProtectOrNull(string secret)
        {
            return ProtectOrNull(secret, CanProtectCurrentUser);
        }

        internal static string ProtectOrNull(string secret, bool canProtectCurrentUser)
        {
            if (string.IsNullOrEmpty(secret))
                return null;
            if (!canProtectCurrentUser)
                throw new PlatformNotSupportedException(
                    "Saving a proxy credential requires Windows current-user protection.");
            if (secret.Length > MaximumSecretCharacters)
                throw new CryptographicException("The proxy credential is too large to protect.");

            byte[] plaintext = Encoding.UTF8.GetBytes(secret);
            try
            {
                if (plaintext.Length > MaximumSecretBytes)
                    throw new CryptographicException(
                        "The proxy credential is too large to protect.");

                byte[] ciphertext = ProtectedData.Protect(
                    plaintext, Entropy, DataProtectionScope.CurrentUser);
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

        internal static string Unprotect(string protectedValue)
        {
            if (!CanProtectCurrentUser)
                throw new PlatformNotSupportedException(
                    "Current-user proxy credential protection requires Windows.");
            if (protectedValue == null ||
                !protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
                throw new CryptographicException(
                    "Unsupported protected proxy credential format.");
            if (protectedValue.Length > MaximumProtectedCharacters)
                throw new CryptographicException(
                    "The protected proxy credential is too large.");

            byte[] ciphertext;
            try
            {
                ciphertext = Convert.FromBase64String(
                    protectedValue.Substring(Prefix.Length));
            }
            catch (FormatException ex)
            {
                throw new CryptographicException(
                    "The protected proxy credential is corrupt.", ex);
            }

            try
            {
                if (ciphertext.Length > MaximumCiphertextBytes)
                    throw new CryptographicException(
                        "The protected proxy credential is too large.");

                byte[] plaintext = ProtectedData.Unprotect(
                    ciphertext, Entropy, DataProtectionScope.CurrentUser);
                try
                {
                    if (plaintext.Length > MaximumSecretBytes)
                        throw new CryptographicException(
                            "The protected proxy credential payload is too large.");
                    return Encoding.UTF8.GetString(plaintext);
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
    }
}
