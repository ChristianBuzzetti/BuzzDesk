using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BuzzDesk;

/// <summary>Small builders for the views (written in code like BuzzPrint's dialogs), all on the theme resources so
/// palettes and fonts follow Personalize.</summary>
public static class Ui
{
    public static TextBlock Text(string text, string brush = "Ink", double? size = null, FontWeight? weight = null, bool mono = false)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        if (size is double s) t.FontSize = s; else t.SetResourceReference(TextBlock.FontSizeProperty, "UiFontSize");
        if (weight is FontWeight w) t.FontWeight = w;
        if (mono) t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        return t;
    }

    public static TextBlock Title(string text) { var t = new TextBlock { Text = text }; t.SetResourceReference(FrameworkElement.StyleProperty, "PageTitle"); return t; }

    /// <summary>Uppercase mono label above a group (DBMON's section label).</summary>
    public static TextBlock Label(string text, Thickness? margin = null)
    {
        var t = new TextBlock { Text = text.ToUpper(L.Culture), Margin = margin ?? new Thickness(0, 18, 0, 8) };
        t.SetResourceReference(FrameworkElement.StyleProperty, "SectionLabel");
        return t;
    }

    public static TextBlock Glyph(string glyph, double size = 13, string brush = "Muted")
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets") };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    /// <summary>Toolbar button: glyph + label, like BuzzPrint's subbar.</summary>
    public static Button ToolButton(string glyph, string text, RoutedEventHandler click, string? tip = null, bool accent = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var g = Glyph(glyph, 12, accent ? "OnAccent" : "Ink"); g.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(g);
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        if (accent) { label.FontWeight = FontWeights.SemiBold; label.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent"); }
        row.Children.Add(label);
        var b = new Button { Content = row, Margin = new Thickness(0, 0, 8, 0), ToolTip = tip };
        if (accent) { b.SetResourceReference(Control.BackgroundProperty, "Accent"); b.SetResourceReference(Control.BorderBrushProperty, "Accent"); }
        b.Click += click;
        return b;
    }

    public static Button IconButton(string glyph, RoutedEventHandler click, string? tip = null)
    {
        var b = new Button { Content = glyph, ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(FrameworkElement.StyleProperty, "IconButton");
        b.Click += click;
        return b;
    }

    /// <summary>Raised surface with hairline border (a panel on the canvas).</summary>
    public static Border Panel(UIElement child, Thickness? padding = null)
    {
        var b = new Border { Child = child, BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(18, 14, 18, 16) };
        b.SetResourceReference(Border.BackgroundProperty, "Surface");
        b.SetResourceReference(Border.BorderBrushProperty, "FaintBorder");
        b.SetResourceReference(Border.CornerRadiusProperty, "PanelRadius");
        return b;
    }

    /// <summary>Small rounded tag (labels, areas, status).</summary>
    public static Border Chip(string text, string background = "Badge", string foreground = "BadgeInk")
    {
        var t = new TextBlock { Text = text, FontSize = 11 };
        t.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
        var b = new Border { Child = t, Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, background);
        return b;
    }

    public static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
}
