using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using VideoDesk.Core;

namespace VideoDesk.App;

internal sealed class SegmentedModeSwitch : Border
{
    private readonly TranslateTransform position = new();
    private readonly Button[] buttons = new Button[2];
    private readonly Brush ink, accent;
    private readonly Func<DiscoveryMode, Task> choose;
    private int selected;

    internal SegmentedModeSwitch(DiscoveryMode selected, Func<DiscoveryMode, Task> choose, Brush surface, Brush selectedBrush, Brush ink, Brush accent)
    {
        this.choose = choose; this.ink = ink; this.accent = accent;
        Width = 180; Height = 36; Background = surface; CornerRadius = new(12); VerticalAlignment = VerticalAlignment.Center; Tag = "discovery-mode-switch";
        var grid = new Grid();
        var indicator = new Border { Width = 90, Height = 36, Background = selectedBrush, CornerRadius = new(12), HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = position, IsHitTestVisible = false };
        grid.Children.Add(indicator);
        var row = new UniformGrid { Columns = 2 };
        grid.Children.Add(row); Child = grid;
        for (int i = 0; i < 2; i++)
        {
            var index = i;
            var button = new Button { Content = i == 0 ? "常规模式" : "漫游模式", Style = (Style)Application.Current.FindResource("PlainButton"), Height = 36, FontSize = 13, Padding = new Thickness(12, 0, 12, 0), Background = Brushes.Transparent };
            AutomationProperties.SetName(button, (string)button.Content);
            button.Click += async (_, _) => { if (this.selected == index) return; Select((DiscoveryMode)index, true); await this.choose((DiscoveryMode)index); };
            buttons[i] = button; row.Children.Add(button);
        }
        Select(selected, false);
    }

    internal void Select(DiscoveryMode mode, bool animate)
    {
        var index = mode == DiscoveryMode.Regular ? 0 : 1;
        if (selected == index && buttons[index].Foreground == accent) return;
        selected = index;
        for (int i = 0; i < 2; i++) { buttons[i].Foreground = i == selected ? accent : ink; AutomationProperties.SetItemStatus(buttons[i], i == selected ? "已选中" : "未选中"); }
        UiMotion.To(position, TranslateTransform.XProperty, selected * 90, animate);
    }
}
