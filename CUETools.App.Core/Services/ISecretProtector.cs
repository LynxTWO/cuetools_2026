using System;

namespace CUETools.Wpf.Services;

/// <summary>
/// Credential protection seam for persisted settings. Windows supplies the
/// DPAPI implementation; other heads supply their platform's answer (or a
/// declining protector that keeps secrets out of the profile entirely).
/// </summary>
public interface ISecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedValue);
}
