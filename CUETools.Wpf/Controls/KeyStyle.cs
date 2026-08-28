using System.Windows;

namespace CUETools.Wpf.Controls;

/// <summary>What job a console key does, which decides whether it carries a backlit legend strip.
/// </summary>
public enum KeyRole
{
    /// <summary>An ordinary key: no legend strip.</summary>
    Normal,

    /// <summary>A key in the RUN group: the legend strip is present but unlit.</summary>
    Transport,

    /// <summary>The RUN group's primary key: the legend strip is lit and blooms.</summary>
    TransportPrimary,
}

/// <summary>
/// Marks a Button's role so the shared key template can light its legend strip.
///
/// The Linux head expresses this with Avalonia selectors that reach into another style's template
/// (<c>Button.transport /template/ Border#legend</c>). WPF has no equivalent: a Style setter cannot
/// carry a TargetName, and TargetName is legal only inside the ControlTemplate that declares the
/// name. So the variant sets this attached property instead, and the one key template triggers on
/// it. Keeping every part-level override inside the single template is also what keeps trigger
/// precedence honest - the trap that made the codec picker's selection go system-cyan under high
/// contrast (D14).
/// </summary>
public static class KeyStyle
{
    public static readonly DependencyProperty RoleProperty =
        DependencyProperty.RegisterAttached(
            "Role",
            typeof(KeyRole),
            typeof(KeyStyle),
            new FrameworkPropertyMetadata(KeyRole.Normal));

    public static KeyRole GetRole(DependencyObject element) =>
        (KeyRole)element.GetValue(RoleProperty);

    public static void SetRole(DependencyObject element, KeyRole value) =>
        element.SetValue(RoleProperty, value);
}
