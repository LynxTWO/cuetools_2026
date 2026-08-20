using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CUETools.Processor;
using CUETools.Wpf.Mvvm;
using CUETools.Wpf.Services;

namespace CUETools.Wpf.ViewModels;

/// <summary>One preview album in the live-preview panel: a label and the rendered relative paths.</summary>
public sealed class NamingPreviewGroup
{
    public string Label { get; init; } = "";
    public ObservableCollection<string> Lines { get; } = new();
}

/// <summary>
/// The Naming editor page. Edits the filename/folder scheme (template + clean-up rules), picks a
/// preset, and shows a LIVE PREVIEW that updates from canned examples AND from the disc currently
/// loaded on the Rip page. On any change it persists the scheme and writes the template into the
/// scheme the rip and convert paths render with (via NamingEngine), so the preview equals the output.
/// </summary>
public sealed class NamingViewModel : PageViewModel
{
    private readonly CUEConfig _config;
    private readonly AppSettings _settings;
    private NamingScheme _scheme;
    // false during construction: the tray-disc lookup resolves IEnumerable<PageViewModel> from the
    // container, which is mid-build while THIS page is being constructed - re-entering it there
    // hangs. The examples always render; the tray disc joins once startup is done (the page's
    // Loaded handler and every later edit call Refresh with this set).
    private bool _ready;

    public ObservableCollection<string> PaletteFields { get; } = new(NamingEngine.PaletteFields);
    public ObservableCollection<string> PresetNames { get; } = new(NamingEngine.Presets.Select(p => p.Name));
    public ObservableCollection<NamingPreviewGroup> Preview { get; } = new();

    private readonly Func<RipViewModel?>? _ripSource;

    /// <summary>ripSource supplies the Rip page for the tray-disc preview group; null
    /// means examples only. A seam instead of a container static, so both heads and the
    /// tests can hand it their own.</summary>
    public NamingViewModel(CUEConfig config, AppSettings settings, Func<RipViewModel?>? ripSource = null)
    {
        _ripSource = ripSource;
        Title = "Naming";
        Group = "Setup";
        Subtitle = "Design how ripped files and folders are named, with a live preview.";
        _config = config;
        _settings = settings;
        _scheme = settings.LoadNamingScheme();
        // The template is NOT pushed into the engine's trackFilenameFormat any more: rip and convert
        // render names with NamingEngine and hand the engine explicit per-track names, and the engine's
        // token vocabulary differs from the WPF one (it has no "%albumartist%"), so writing a WPF
        // template there only corrupts a setting the user can also see and edit on the Settings page.
        Refresh();          // examples only (see _ready) - safe during container build
        _ready = true;
    }

    public string Template
    {
        get => _scheme.Template;
        set { _scheme.Template = value ?? ""; Apply(); OnPropertyChanged(); }
    }

    public bool ExtractFeatured { get => _scheme.ExtractFeatured; set { _scheme.ExtractFeatured = value; Apply(); OnPropertyChanged(); } }
    public bool UnifySeparators { get => _scheme.UnifySeparators; set { _scheme.UnifySeparators = value; Apply(); OnPropertyChanged(); } }
    public bool HandleArticles { get => _scheme.HandleArticles; set { _scheme.HandleArticles = value; Apply(); OnPropertyChanged(); } }
    public bool StripIllegal { get => _scheme.StripIllegal; set { _scheme.StripIllegal = value; Apply(); OnPropertyChanged(); } }
    public bool ReleaseDescriptor { get => _scheme.ReleaseDescriptor; set { _scheme.ReleaseDescriptor = value; Apply(); OnPropertyChanged(); } }

    /// <summary>Load a built-in preset by name (from the picker).</summary>
    public void ApplyPreset(string name)
    {
        var preset = NamingEngine.Presets.FirstOrDefault(p => p.Name == name);
        if (preset.Scheme == null) return;
        _scheme = preset.Scheme.Clone();
        Apply();
        // re-announce every bound property
        OnPropertyChanged(nameof(Template));
        OnPropertyChanged(nameof(ExtractFeatured));
        OnPropertyChanged(nameof(UnifySeparators));
        OnPropertyChanged(nameof(HandleArticles));
        OnPropertyChanged(nameof(StripIllegal));
        OnPropertyChanged(nameof(ReleaseDescriptor));
    }

    /// <summary>Insert a palette field into the template at the given caret position.</summary>
    public int InsertField(string field, int caret)
    {
        string t = _scheme.Template ?? "";
        caret = Math.Max(0, Math.Min(caret, t.Length));
        _scheme.Template = t.Substring(0, caret) + field + t.Substring(caret);
        Apply();
        OnPropertyChanged(nameof(Template));
        return caret + field.Length;
    }

    private void Apply()
    {
        // Real rip/convert output now renders through NamingEngine (RipService/ConvertService inject
        // explicit names), so the engine's trackFilenameFormat is no longer used for track naming and
        // must not be overwritten with WPF-token syntax the old engine cannot parse.
        _settings.SaveNamingScheme(_scheme);
        Refresh();
    }

    /// <summary>Rebuild the preview: the canned examples first, then the real tray disc if loaded.</summary>
    public void Refresh()
    {
        Preview.Clear();
        foreach (var (label, tracks) in NamingEngine.Examples())
        {
            var g = new NamingPreviewGroup { Label = label };
            foreach (var t in tracks) g.Lines.Add(NamingEngine.Render(t, _scheme));
            Preview.Add(g);
        }

        if (_ready)
        {
            var disc = BuildTrayDiscGroup();
            if (disc != null) Preview.Insert(0, disc);   // the real disc leads when present
        }
    }

    // Pull the currently loaded release off the Rip page (resolved from the container to avoid a
    // ctor cycle). Per-track artist is not surfaced there, so the album artist stands in - fine for
    // a preview; the point is to see YOUR disc land in the scheme.
    private NamingPreviewGroup? BuildTrayDiscGroup()
    {
        try
        {
            RipViewModel? vm = _ripSource?.Invoke();
            if (vm == null || vm.Tracks.Count == 0 || string.IsNullOrWhiteSpace(vm.AlbumTitle)) return null;

            string year = vm.SelectedRelease?.Year ?? "";
            // AlbumArtist is a DISPLAY string with "  (Year)" appended for the header; strip that
            // trailing year-parenthetical so the engine gets a clean artist (the year is a field).
            string cleanArtist = System.Text.RegularExpressions.Regex.Replace(
                vm.AlbumArtist ?? "", @"\s*\(\d{4}\)\s*$", "").Trim();
            // Go through NamingContextMapper, the SAME mapper the rip uses, rather than hand-building a
            // context here. Two mappers drifted: this one set seven fields, the real one sets eighteen,
            // and the two it omitted were DiscNumber/TotalDiscs - so every box set previewed as a
            // single disc while the rip wrote "Artist - Album [3-CD Set]/Disc 2/...". A preview that
            // does not match the output is worse than no preview.
            var meta = vm.SelectedRelease?.Metadata ?? SyntheticMeta(vm, cleanArtist, year);
            var g = new NamingPreviewGroup { Label = "Disc in tray: " + vm.AlbumTitle };
            for (int i = 0; i < Math.Min(4, vm.Tracks.Count); i++)
                g.Lines.Add(NamingEngine.Render(
                    NamingContextMapper.FromMetadata(meta, i, vm.Tracks.Count), _scheme));
            if (vm.Tracks.Count > 4) g.Lines.Add($"... and {vm.Tracks.Count - 4} more");
            return g;
        }
        catch { return null; }
    }

    /// <summary>A release object for a disc that matched no metadata source, built from what the tray
    /// header actually shows. Exists so the preview always feeds NamingContextMapper - never a second,
    /// hand-rolled context that can drift from it.</summary>
    private static CUEMetadata SyntheticMeta(RipViewModel vm, string artist, string year)
    {
        var m = new CUEMetadata("", vm.Tracks.Count) { Artist = artist, Title = vm.AlbumTitle, Year = year };
        for (int i = 0; i < vm.Tracks.Count && i < m.Tracks.Count; i++)
            m.Tracks[i].Title = vm.Tracks[i].Title ?? "";
        return m;
    }
}
