using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HuayuanPreview.R2;

public partial class R2ShellView : UserControl
{
    private readonly DispatcherTimer filterDelay = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer drawerDelay = new() { Interval = TimeSpan.FromMilliseconds(240) };
    private readonly DispatcherTimer toastDelay = new() { Interval = TimeSpan.FromMilliseconds(2250) };
    private ShellModel? model;
    private bool queueOpen;
    private bool updatingFilter;
    private string? ignoredFilterText;

    public R2ShellView()
    {
        InitializeComponent();
        InitializeOnlineSearch();
        InitializeLyricOverlay();
        QualityChoice.SelectedIndex=FullQuality.SelectedIndex=DesktopPreferences.Quality switch { "320"=>1,"flac"=>2,_=>0 };
        filterDelay.Tick += (_, _) =>
        {
            filterDelay.Stop();
            if(model?.Workspace.Local==true) model.Workspace.ApplyFilter(CurrentFilter.Text ?? "");
        };
        DesktopProgress.PointerEntered += (_, _) => DesktopProgress.Resources["SliderTrackThemeHeight"] = 6d;
        DesktopProgress.PointerExited += (_, _) => DesktopProgress.Resources["SliderTrackThemeHeight"] = 3d;
        drawerDelay.Tick += (_, _) => { drawerDelay.Stop(); if (!queueOpen) DrawerLayer.IsVisible = false; };
        toastDelay.Tick += (_, _) =>
        {
            toastDelay.Stop();
            ToastPlate.Opacity = 0;
            DispatcherTimer.RunOnce(() => { if (!toastDelay.IsEnabled) ToastPlate.IsVisible = false; }, TimeSpan.FromMilliseconds(160));
        };
        DataContextChanged += (_, _) => BindModel();
        DetachedFromVisualTree += (_, _) => { filterDelay.Stop(); drawerDelay.Stop(); toastDelay.Stop(); };
        AddHandler(KeyDownEvent, TransportKeyDown, RoutingStrategies.Tunnel);
    }

    private void BindModel()
    {
        if (model != null)
        {
            model.PropertyChanged -= WorkspaceChanged;
            model.Player.Updated -= UpdateVolume;
            model.Player.Toast -= ShowToast;
        }
        model = DataContext as ShellModel;
        if (model == null)
            return;
        model.PropertyChanged += WorkspaceChanged;
        model.Player.Updated += UpdateVolume;
        model.Player.Toast += ShowToast;
        VolumeSlider.Submit = model.Player.SetVolume;
        FullVolume.Submit = model.Player.SetVolume;
        FullProgress.Submit = model.Player.Seek;
        InteractionController.Attach(DrawerItems, model.LocalWorkspace, model.Player, true);
        UpdateSearchContext();
        SyncFilter();
        UpdateVolume();
    }

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        ToastPlate.IsVisible = true;
        ToastPlate.Opacity = 1;
        toastDelay.Stop();
        toastDelay.Start();
    }

    private void WorkspaceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellModel.Workspace)){UpdateSearchContext();SyncFilter();}
        if (args.PropertyName == nameof(ShellModel.KugouEnabled) && model?.KugouEnabled == false)
        {
            CancelSuggestions();
            CancelComments();
            lyricRequest?.Cancel();
        }
    }

    private void SyncFilter()
    {
        filterDelay.Stop();
        SetFilterText(model?.Workspace.FilterText);
    }

    private void SetFilterText(string? value)
    {
        if (CurrentFilter.Text == value) return;
        ignoredFilterText = value ?? "";
        updatingFilter = true;
        CurrentFilter.Text = value;
        updatingFilter = false;
    }

    private bool syncingQuality;
    private async void ChangeQuality(object? sender,SelectionChangedEventArgs args)
    {
        if(syncingQuality || sender is not ComboBox chooser || QualityChoice == null || FullQuality == null) return;
        var index=chooser.SelectedIndex;
        if(index is <0 or >2) return;
        syncingQuality=true;
        QualityChoice.SelectedIndex=FullQuality.SelectedIndex=index;
        syncingQuality=false;
        var selected=index switch { 1=>"320",2=>"flac",_=>"standard" };
        if(selected==DesktopPreferences.Quality) return;
        try { await DesktopPreferences.ChooseQualityAsync(selected); Program.Log("R2_QUALITY_PREFERENCE",new{quality=selected}); }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        { Program.Log("R2_QUALITY_WRITE_FAILED",new{type=error.GetType().Name}); }
    }

    private void UpdateVolume()
    {
        var state=model?.Player.State;
        VolumeSlider.Sync(state?.Volume ?? 0);
        FullVolume.Sync(state?.Volume ?? 0);
        if (lyricOpen && state != null)
        {
            FullProgress.IsEnabled = state.Duration > 0;
            FullProgress.Maximum = Math.Max(0, state.Duration);
            FullProgress.Sync(state.Position);
        }
        UpdateLyricOverlay();
    }
    private Window? Window => TopLevel.GetTopLevel(this) as Window;

    private void FilterChanged(object? sender, TextChangedEventArgs args)
    {
        var ignored = ignoredFilterText;
        ignoredFilterText = null;
        if (updatingFilter || ignored != null && ignored == (CurrentFilter.Text ?? ""))
            return;
        if(model?.Workspace.IsKugou==true)
        {
            var query=CurrentFilter.Text?.Trim()??"";
            if(lastSubmittedSearch!=query) lastSubmittedSearch=null;
            _ = RequestSuggestionsAsync(query);
            return;
        }
        filterDelay.Stop();
        filterDelay.Start();
    }

    private void GoBack(object? sender, RoutedEventArgs args) { model?.Workspace.Back(); SyncFilter(); }
    private void GoForward(object? sender, RoutedEventArgs args) { model?.Workspace.Forward(); SyncFilter(); }
    private void OpenPluginCenter(object? sender, RoutedEventArgs args) => model?.Workspace.ShowPlugins();
    private void ShowLyrics(object? sender, RoutedEventArgs args) => ToggleLyricOverlay();
    private void ShowAccount(object? sender, RoutedEventArgs args) => model?.ShowAccount();
    private async void ToggleTheme(object? sender, RoutedEventArgs args) => await SetDesktopThemeAsync(ActualThemeVariant!=ThemeVariant.Dark);

    private async Task SetDesktopThemeAsync(bool dark)
    {
        if (Window is { } window)
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        await PersistPreferenceAsync(options=>options with {Dark=dark});
    }
    private void MinimizeWindow(object? sender, RoutedEventArgs args)
    {
        if (Window is { } window)
            window.WindowState = WindowState.Minimized;
    }
    private void ToggleWindowSize(object? sender, RoutedEventArgs args)
    {
        if (Window is not { } window)
            return;
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeButton.Content = window.WindowState == WindowState.Maximized ? "❐" : "□";
    }
    private void CloseWindow(object? sender, RoutedEventArgs args) => Window?.Close();
    private void StartWindowDrag(object? sender, PointerPressedEventArgs args)
    {
        if (args.ClickCount == 1 && args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Window?.BeginMoveDrag(args);
    }

    private void ToggleQueue(object? sender, RoutedEventArgs args) => SetQueueOpen(!queueOpen);
    private void CloseQueueFromBackdrop(object? sender, PointerPressedEventArgs args) => SetQueueOpen(false);

    private void SetQueueOpen(bool open)
    {
        queueOpen = open;
        drawerDelay.Stop();
        if (open) DrawerLayer.IsVisible = true; else drawerDelay.Start();
        DrawerLayer.IsHitTestVisible = open;
        DrawerShade.Opacity = open ? 1 : 0;
        QueueDrawer.RenderTransform = TransformOperations.Parse(open ? "translateX(0px)" : "translateX(360px)");
    }

    private void TransportKeyDown(object? sender, KeyEventArgs args)
    {
        if (model == null)
            return;
        if (args.Key == Key.Escape && queueOpen)
        {
            SetQueueOpen(false);
            args.Handled = true;
            return;
        }
        if(args.Key==Key.Escape && lyricOpen){CloseLyricOverlay(); args.Handled=true; return;}
        if (args.Source is Visual source &&
            (source is TextBox or Slider or SelectableTextBlock || source.GetVisualAncestors().Any(item=>item is TextBox or SelectableTextBlock)))
            return;
        var command = (args.Key, args.KeyModifiers) switch
        {
            (Key.Space, KeyModifiers.None) => model.Player.TogglePlayPauseCommand,
            (Key.Left, KeyModifiers.Control) => model.Player.PlayPreviousCommand,
            (Key.Right, KeyModifiers.Control) => model.Player.PlayNextCommand,
            _ => null
        };
        if (command?.CanExecute(null) != true)
            return;
        command.Execute(null);
        args.Handled = true;
    }
}
