using System.Windows;
using System.Windows.Controls;
using OpenXRInputExplorer.Services;

namespace OpenXRInputExplorer;

internal sealed class WindowPickerWindow : Window
{
    public nint SelectedWindow { get; private set; }

    public WindowPickerWindow()
    {
        Title = "Choose a window to capture and control";
        Width = 650; Height = 420;
        MinWidth = 420; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(12) };
        Content = panel;
        var help = new TextBlock
        {
            Text = "Select Dolphin's game window. Keep it restored while using mouse control.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        };
        DockPanel.SetDock(help, Dock.Top); panel.Children.Add(help);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var refresh = new Button { Content = "Refresh", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6) };
        var select = new Button { Content = "Capture selected window", IsDefault = true, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(6), IsEnabled = false };
        buttons.Children.Add(refresh); buttons.Children.Add(cancel); buttons.Children.Add(select);
        var list = new ListBox { ItemsSource = NativeDesktop.ListWindows() };
        panel.Children.Add(list);
        list.SelectionChanged += (_, _) => select.IsEnabled = list.SelectedItem is NativeDesktop.WindowChoice;
        refresh.Click += (_, _) => list.ItemsSource = NativeDesktop.ListWindows();
        void Choose()
        {
            if (list.SelectedItem is NativeDesktop.WindowChoice choice && NativeDesktop.IsWindow(choice.Handle))
            { SelectedWindow = choice.Handle; DialogResult = true; }
        }
        select.Click += (_, _) => Choose();
        list.MouseDoubleClick += (_, _) => Choose();
    }
}
