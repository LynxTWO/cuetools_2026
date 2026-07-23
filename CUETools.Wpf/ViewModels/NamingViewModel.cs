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
/// engine's trackFilenameFormat so real rips/converts use it.
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

    public NamingViewModel(CUEConfig config, AppSettings settings)
    {
        Title = "Naming";
        Group = "Setup";
        Subtitle = "Design how ripped files and folders are named, with a live preview.";
        _config = config;
        _settings = settings;
        _scheme = settings.LoadNamingScheme();
        // reflect the loaded template into the engine so a rip uses it even before the page is opened
        _config.trackFilenameFormat = _scheme.Template;
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
        _config.trackFilenameFormat = _scheme.Template;   // real output uses this
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
            var rip = App.Services?.GetService(typeof(IEnumerable<PageViewModel>)) as IEnumerable<PageViewModel>;
            var vm = rip?.OfType<RipViewModel>().FirstOrDefault();
            if (vm == null || vm.Tracks.Count == 0 || string.IsNullOrWhiteSpace(vm.AlbumTitle)) return null;

            string year = vm.SelectedRelease?.Year ?? "";
            // AlbumArtist is a DISPLAY string with "  (Year)" appended for the header; strip that
            // trailing year-parenthetical so the engine gets a clean artist (the year is a field).
            string cleanArtist = System.Text.RegularExpressions.Regex.Replace(
                vm.AlbumArtist ?? "", @"\s*\(\d{4}\)\s*$", "").Trim();
            var g = new NamingPreviewGroup { Label = "Disc in tray: " + vm.AlbumTitle };
            foreach (var t in vm.Tracks.Take(4))
            {
                var ctx = new NamingContext
                {
                    AlbumArtist = cleanArtist,
                    Artist = cleanArtist,
                    Album = vm.AlbumTitle,
                    Title = t.Title,
                    Year = year,
                    TrackNumber = t.Number,
                    TotalTracks = vm.Tracks.Count,
                };
                g.Lines.Add(NamingEngine.Render(ctx, _scheme));
            }
            if (vm.Tracks.Count > 4) g.Lines.Add($"... and {vm.Tracks.Count - 4} more");
            return g;
        }
        catch { return null; }
    }
}
