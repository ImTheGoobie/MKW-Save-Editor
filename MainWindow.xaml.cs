using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using System.Linq;
using WinRT.Interop;
namespace MKWWinUI;
public sealed partial class MainWindow : Window
{
    private readonly SaveEngine engine = new();
    private readonly List<Driver> visible = new();
    private readonly Dictionary<string, List<Driver>> groups = new(StringComparer.OrdinalIgnoreCase);
    private bool updatingGroupChecks;
    public MainWindow()
    {
        InitializeComponent();
        foreach (var driver in engine.Drivers)
            driver.PropertyChanged += (_, _) => UpdateCount();
        Filter();
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 790));
    }
    private void Filter_Changed(object sender, object e) => Filter();
    private void Filter()
    {
        if (GroupPanel is null || SearchBox is null) return;
        var query = SearchBox.Text.Trim();
        visible.Clear();
        foreach (var d in engine.Drivers)
        {
            if (!d.Actor.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !d.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !d.Character.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            visible.Add(d);
        }
        RenderGroups();
        UpdateCount();
    }
    private void RenderGroups()
    {
        GroupColumn0.Children.Clear();
        GroupColumn1.Children.Clear();
        GroupColumn2.Children.Clear();
        groups.Clear();
        foreach (var d in visible)
        {
            var key = d.Character;
            if (!groups.TryGetValue(key, out var members))
                groups[key] = members = new List<Driver>();
            members.Add(d);
        }
        var columnIndex = 0;
        foreach (var pair in groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var members = pair.Value.OrderBy(d => d.DisplayName).ToList();
            var parent = new CheckBox
            {
                IsThreeState = true,
                Content = pair.Key,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            void SyncParent()
            {
                updatingGroupChecks = true;
                var checkedCount = members.Count(d => d.Selected);
                parent.IsChecked = checkedCount == 0 ? false
                    : checkedCount == members.Count ? true : null;
                updatingGroupChecks = false;
            }
            parent.Checked += (_, _) => SetGroup(true);
            parent.Unchecked += (_, _) => SetGroup(false);
            parent.Indeterminate += (_, _) => { }; // Partial is a display state only.
            void SetGroup(bool selected)
            {
                if (updatingGroupChecks) return;
                foreach (var d in members) d.Selected = selected;
                SyncParent();
            }

            // Place the amount at the trailing edge of the header, immediately
            // before the Expander's built-in chevron toggle.
            var header = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(parent, 0);
            header.Children.Add(parent);
            var amountText = new TextBlock
            {
                Text = $"{members.Count(d => d.Selected)} / {members.Count}",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 4, 0),
                Opacity = 0.65
            };
            Grid.SetColumn(amountText, 1);
            header.Children.Add(amountText);
            void RefreshAmount() => amountText.Text = $"{members.Count(d => d.Selected)} / {members.Count}";
            foreach (var driver in members)
                driver.PropertyChanged += (_, _) => RefreshAmount();
            var content = new StackPanel
            {
                Spacing = 4,
                Padding = new Thickness(8, 6, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            foreach (var d in members)
            {
                var name = d.DisplayName;
                var check = new CheckBox
                {
                    Content = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap },
                    IsChecked = d.Selected,
                    IsThreeState = false,
                    MinHeight = 34,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(6, 4, 6, 4)
                };
                check.Checked += (_, _) => { d.Selected = true; SyncParent(); };
                check.Unchecked += (_, _) => { d.Selected = false; SyncParent(); };
                content.Children.Add(check);
            }
            var expander = new Expander
            {
                Header = header,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = content,
                IsExpanded = !string.IsNullOrWhiteSpace(SearchBox.Text)
            };
            expander.Width = 310;
            expander.Margin = new Thickness(0);
            var columns = new[] { GroupColumn0, GroupColumn1, GroupColumn2 };
            columns[columnIndex % columns.Length].Children.Add(expander);
            columnIndex++;
            SyncParent();
        }
    }

    private void UpdateCount()
    {
        if (SelectedText is not null)
            SelectedText.Text = $"{engine.Drivers.Count(d => d.Selected)} / {engine.Drivers.Count} selected";
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var d in visible) d.Selected = true;
        RenderGroups();
    }
    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var d in visible) d.Selected = false;
        RenderGroups();
    }
    private async void FindEden_Click(object sender, RoutedEventArgs e) =>
        await FindEdenAsync(silent: false);

    private async Task FindEdenAsync(bool silent)
    {
        if (FindEdenButton is null) return;
        FindEdenButton.IsEnabled = false;
        try
        {
            var found = await Task.Run(EdenSaveFinder.Find);
            if (found.Count == 0)
            {
                if (!silent)
                    await ShowMessage("Eden save not found",
                        "No Eden-format account.sav was found in the common Eden data folders. " +
                        "Use Open save… to locate it manually, especially for portable installations.");
                return;
            }
            string? chosen;
            if (found.Count == 1)
                chosen = found[0];
            else
            {
                // Never silently pick one of several emulator profiles.
                if (silent)
                {
                    FooterText.Text = $"{found.Count} Eden saves found. Click Find Eden save to choose one.";
                    return;
                }
                var options = new ComboBox
                {
                    ItemsSource = found.Select((path, i) => $"{i + 1}. {path}").ToArray(),
                    SelectedIndex = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = $"Found {found.Count} Eden saves",
                    Content = options,
                    PrimaryButtonText = "Open selected",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                chosen = found[options.SelectedIndex];
            }
            if (chosen is not null)
                LoadSave(chosen);
        }
        catch (Exception ex)
        {
            if (!silent) await ShowMessage("Eden search failed", ex.Message);
        }
        finally { FindEdenButton.IsEnabled = true; }
    }

    private void LoadSave(string path)
    {
        engine.Open(path);
        FormatText.Text = engine.Format == SaveFormat.Eden
            ? $"Eden save · {engine.LoadedCount} driver records"
            : $"Switch save · {engine.LoadedCount} driver records · experimental writes";
        PathText.Text = path;
        SaveButton.IsEnabled = true;
        Notice.Severity = engine.Format == SaveFormat.Switch ? InfoBarSeverity.Warning : InfoBarSeverity.Success;
        Notice.Title = engine.Format == SaveFormat.Switch ? "Switch editing is experimental" : "Eden save ready";
        Notice.Message = engine.Format == SaveFormat.Switch
            ? "Driver IDs can be appended, but ownership flags and list metadata are unknown. Removal is blocked. Changes may not unlock anything in-game."
            : "Driver unlocks can be edited. The app will create a .bak backup before overwriting.";
        Filter();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".sav");
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            LoadSave(file.Path);
        }
        catch (Exception ex) { await ShowMessage("Unable to open save", ex.Message); }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (engine.Format == SaveFormat.None) return;
        var warning = engine.Format == SaveFormat.Switch
            ? "SWITCH EXPERIMENTAL: Only appending IDs is supported. The ownership metadata is unknown, so this may not unlock anything in-game. A .bak backup will be created. Continue?"
            : "Overwrite the loaded Eden account.sav? A .bak backup will be created first.";
        var confirm = new ContentDialog
        {
            XamlRoot = Content.XamlRoot, Title = "Save changes?",
            Content = warning, PrimaryButtonText = "Overwrite save", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var result = engine.Save();
            if (result.backup.Length == 0)
            {
                await ShowMessage("No changes", "Your selected unlocks already match the save.");
                return;
            }
            FormatText.Text = $"{engine.Format} save · {result.total} driver records";
            await ShowMessage("Saved successfully",
                $"{result.added} driver IDs added.\nBackup: {result.backup}");
        }
        catch (Exception ex) { await ShowMessage("Save failed", ex.Message); }
    }
    private async Task ShowMessage(string title, string body)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot, Title = title,
            Content = body, CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }
}
