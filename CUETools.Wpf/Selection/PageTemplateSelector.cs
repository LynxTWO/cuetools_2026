using System.Windows;
using System.Windows.Controls;
using CUETools.Wpf.ViewModels;

namespace CUETools.Wpf.Selection;

/// <summary>Picks the real view for pages that have one, else the shared placeholder.
/// Extended as each page's view lands in Phase 3.</summary>
public sealed class PageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? RipTemplate { get; set; }
    public DataTemplate? DriveTemplate { get; set; }
    public DataTemplate? ReportTemplate { get; set; }
    public DataTemplate? SettingsTemplate { get; set; }
    public DataTemplate? PlaceholderTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        RipViewModel => RipTemplate,
        DriveViewModel => DriveTemplate,
        ReportViewModel => ReportTemplate,
        SettingsViewModel => SettingsTemplate,
        _ => PlaceholderTemplate
    };
}
