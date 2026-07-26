using System;
using System.Runtime.CompilerServices;
using CUETools.Processor;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>
/// Advanced engine settings page: the CUEConfigAdvanced options the original WinForms app
/// exposed through a PropertyGrid. All of these already persist - CUEConfig serializes the
/// whole `advanced` object as JSON under the "Advanced" key - and the engine already honors
/// every one of them. This page only makes them visible and editable again.
/// </summary>
public sealed class AdvancedViewModel : PageViewModel
{
    private readonly CUEConfig _c;
    private readonly IDiagnosticLog _log;

    public AdvancedViewModel(CUEConfig config, IDiagnosticLog log)
    {
        Title = "Advanced";
        Group = "Setup";
        Subtitle = "Engine options carried over from the classic property grid. Most people never need these.";
        _c = config;
        _log = log;
    }

    private void Raise([CallerMemberName] string? n = null) => OnPropertyChanged(n);

    // ---- Output extras ----
    public bool CreateTOC { get => _c.advanced.CreateTOC; set { _c.advanced.CreateTOC = value; Raise(); } }
    public bool DetailedCTDBLog { get => _c.advanced.DetailedCTDBLog; set { _c.advanced.DetailedCTDBLog = value; Raise(); } }
    public bool WriteCTDBTagsOnEncode { get => _c.advanced.WriteCTDBTagsOnEncode; set { _c.advanced.WriteCTDBTagsOnEncode = value; Raise(); } }
    public bool WriteCTDBTagsOnVerify { get => _c.advanced.WriteCTDBTagsOnVerify; set { _c.advanced.WriteCTDBTagsOnVerify = value; Raise(); } }
    public bool UseId3v24 { get => _c.advanced.UseId3v24; set { _c.advanced.UseId3v24 = value; Raise(); } }
    public bool WriteCDTOCTag { get => _c.advanced.WriteCDTOCTag; set { _c.advanced.WriteCDTOCTag = value; Raise(); } }

    // ---- Cover art ----
    public string CoverArtFiles { get => _c.advanced.CoverArtFiles; set { _c.advanced.CoverArtFiles = value; Raise(); } }
    public bool CoverArtSearchSubdirs { get => _c.advanced.CoverArtSearchSubdirs; set { _c.advanced.CoverArtSearchSubdirs = value; Raise(); } }
    public CUEConfigAdvanced.CTDBCoversSearch CoversSearch { get => _c.advanced.coversSearch; set { _c.advanced.coversSearch = value; Raise(); } }
    // "Album art size" (CUEConfigAdvanced.coversSize) is deliberately NOT surfaced: only the legacy
    // WinForms CUERipper reads it. This app's art pipeline is AlbumArtService plus maxAlbumArtSize, so a
    // row here would be another switch that does nothing. Detected by DeadSwitchTests.
    public Array CoversSearches => Enum.GetValues(typeof(CUEConfigAdvanced.CTDBCoversSearch));

    // ---- Metadata ----
    public CUETools.CTDB.CTDBMetadataSearch MetadataSearch { get => _c.advanced.metadataSearch; set { _c.advanced.metadataSearch = value; Raise(); } }
    public Array MetadataSearches => Enum.GetValues(typeof(CUETools.CTDB.CTDBMetadataSearch));
    public bool CacheMetadata { get => _c.advanced.CacheMetadata; set { _c.advanced.CacheMetadata = value; Raise(); } }
    public string CTDBServer { get => _c.advanced.CTDBServer; set { _c.advanced.CTDBServer = value; Raise(); } }

    // ---- Network ----
    public CUEConfigAdvanced.ProxyMode ProxyMode { get => _c.advanced.UseProxyMode; set { _c.advanced.UseProxyMode = value; Raise(); } }
    public Array ProxyModes => Enum.GetValues(typeof(CUEConfigAdvanced.ProxyMode));
    public string ProxyServer { get => _c.advanced.ProxyServer; set { _c.advanced.ProxyServer = value; Raise(); } }
    public int ProxyPort { get => _c.advanced.ProxyPort; set { _c.advanced.ProxyPort = value; Raise(); } }
    public string ProxyUser { get => _c.advanced.ProxyUser; set { _c.advanced.ProxyUser = value; Raise(); } }
    public bool HasProxyPassword => !string.IsNullOrEmpty(_c.advanced.ProxyPassword);
    public string ProxyPasswordStatus => HasProxyPassword ? "Credential set" : "No credential";

    public bool SetProxyPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return false;

        _c.advanced.ProxyPassword = password;
        _log.Redact(password);
        Raise(nameof(HasProxyPassword));
        Raise(nameof(ProxyPasswordStatus));
        return true;
    }

    public void ClearProxyPassword()
    {
        _c.advanced.ProxyPassword = "";
        Raise(nameof(HasProxyPassword));
        Raise(nameof(ProxyPasswordStatus));
    }

    public string FreedbUser { get => _c.advanced.FreedbUser; set { _c.advanced.FreedbUser = value; Raise(); } }
    public string FreedbDomain { get => _c.advanced.FreedbDomain; set { _c.advanced.FreedbDomain = value; Raise(); } }
    public string FreedbSiteAddress { get => _c.advanced.FreedbSiteAddress; set { _c.advanced.FreedbSiteAddress = value; Raise(); } }
}
