using System;
using System.Security.Cryptography;
using System.Text;

namespace CUETools.Wpf.Services;

internal interface ISecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedValue);
}

/// <summary>Protects credentials for the current Windows user. The protected value is safe to
/// keep beside ordinary settings, but it cannot be decrypted by another Windows account.</summary>
internal sealed class WindowsDpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CUETools2026.ProxyPassword.v1");
    private const string Prefix = "dpapi-v1:";
    private const int MaximumSecretCharacters = 16384;
    private const int MaximumSecretBytes = 65536;
    private const int MaximumProtectedCharacters = 131072;
    private const int MaximumCiphertextBytes = 98304;

    public string Protect(string secret)
    {
        if (secret == null)
            throw new ArgumentNullException(nameof(secret));
        if (secret.Length > MaximumSecretCharacters)
            throw new CryptographicException("The credential is too large to protect.");

        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        try
        {
            if (plaintext.Length > MaximumSecretBytes)
                throw new CryptographicException("The credential is too large to protect.");
            byte[] ciphertext = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                return Prefix + Convert.ToBase64String(ciphertext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(ciphertext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public string Unprotect(string protectedValue)
    {
        if (protectedValue == null || !protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
            throw new CryptographicException("Unsupported protected credential format.");
        if (protectedValue.Length > MaximumProtectedCharacters)
            throw new CryptographicException("Protected credential is too large.");

        byte[] ciphertext;
        try
        {
            ciphertext = Convert.FromBase64String(protectedValue.Substring(Prefix.Length));
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Protected credential is corrupt.", ex);
        }

        try
        {
            if (ciphertext.Length > MaximumCiphertextBytes)
                throw new CryptographicException("Protected credential is too large.");
            byte[] plaintext = ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
            try
            {
                if (plaintext.Length > MaximumSecretBytes)
                    throw new CryptographicException("Protected credential payload is too large.");
                return Encoding.UTF8.GetString(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }
}
