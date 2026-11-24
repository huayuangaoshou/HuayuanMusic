using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace HuayuanPreview.R2;

public partial class R2ShellView : UserControl
{
    private readonly DispatcherTimer filterDelay = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer drawerDelay = new() { Interval = TimeSpan.FromMilliseconds(255) };
    private readonly DispatcherTimer toastDelay = new() { Interval = TimeSpan.FromMilliseconds(2250) };
    private ShellModel? model;
    private InteractionController? queueController;
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
        drawerDelay.Tick += (_, _) => { drawerDelay.Stop(); if (!queueOpen) DrawerLayer.IsVisible = false; };
        toastDelay.Tick += (_, _) =>
        {
            toastDelay.Stop();
            ToastPlate.Opacity = 0;
            DispatcherTimer.RunOnce(() => { if (!toastDelay.IsEnabled) ToastPlate.IsVisible = false; }, TimeSpan.FromMilliseconds(160));
        };
        DataContextChanged += (_, _) => BindModel();
        DetachedFromVisualTree += (_, _) => { filterDelay.Stop(); drawerDelay.Stop(); toastDelay.Stop(); downloadRequest?.Cancel(); CloseSongComments(); };
        AddHandler(KeyDownEvent, TransportKeyDown, RoutingStrategies.Tunnel);
    }

    private void BindModel()
    {
        if (model != null)
        {
            model.PropertyChanged -= WorkspaceChanged;
            model.Player.Updated -= UpdateVolume;
            model.Player.Toast -= ShowToast;
            model.Concept.Changed -= OnlineActionsChanged;
        }
        model = DataContext as ShellModel;
        if (model == null)
            return;
        model.PropertyChanged += WorkspaceChanged;
        model.Player.Updated += UpdateVolume;
        model.Player.Toast += ShowToast;
        model.Concept.Changed += OnlineActionsChanged;
        VolumeSlider.Submit = model.Player.SetVolume;
        FullVolume.Submit = model.Player.SetVolume;
        FullProgress.Submit = model.Player.Seek;
        queueController = InteractionController.Attach(DrawerItems, model.LocalWorkspace, model.Player, true);
        QueueLocateButton.DataContext = queueController;
        UpdateSearchContext();
        SyncFilter();
        UpdateVolume();
    }

    private void OnlineActionsChanged()
    {
        if (model?.Concept.Connected != true)
        {
            CloseSongComments();
            downloadRequest?.Cancel();
        }
        foreach (var row in model?.Player.DisplayPlaybackQueue ?? []) row.RefreshAccess();
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
            CloseSongComments();
            downloadRequest?.Cancel();
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
        UpdateCoverTint();
    }

    // R5: 封面主色染背景——随当前曲目切换，取色在后台线程，UI 只收结果，400ms 过渡，无封面回退 GlassBase。
    private string? tintTrackKey;
    private CancellationTokenSource? tintRequest;
    private async void UpdateCoverTint()
    {
        if (model == null) return;
        var row = model.Player.DisplayedPlayingSong;
        var key = row?.Track.Key;
        if (key == tintTrackKey) return;
        tintTrackKey = key;
        tintRequest?.Cancel();
        var request = new CancellationTokenSource();
        tintRequest = request;
        var target = CoverTint;
        if (row == null || row.Track.Cover == null)
        {
            target.Background = (IBrush)Application.Current!.Resources["GlassBase"]!;
            return;
        }
        try
        {
            if (row.IsProviderTrack) { return; } // 在线曲目封面走 Control，本地取色仅覆盖本地封面
            Bitmap? bitmap = await model.Concept.LoadLocalCover(row.Track.Cover!);
            if (request.IsCancellationRequested || tintTrackKey != key) { bitmap?.Dispose(); return; }
            var main = await CoverColorExtractor.ExtractAsync(bitmap, request.Token);
            bitmap?.Dispose();
            if (request.IsCancellationRequested || tintTrackKey != key || main is not { } color) return;
            var dark = ActualThemeVariant == ThemeVariant.Dark;
            var tint = CoverColorExtractor.TintForBackground(color, dark);
            target.Background = new SolidColorBrush(tint);
        }
        catch (OperationCanceledException) { }
        catch { /* 取色失败回退，不报错 */ }
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
    private void ShowAccount(object? sender, RoutedEventArgs args)
    {
        if (model is not { KugouEnabled: true } shell || sender is not Button button) return;
        if (!shell.Concept.Connected) { _ = ShowQrLoginAsync(shell); return; }
        var menu = new MenuFlyout();
        var details = new MenuItem { Header = "账号信息" };
        details.Click += (_, _) => shell.ShowAccount();
        var change = new MenuItem { Header = "切换账号" };
        change.Click += async (_, _) =>
        {
            await shell.Concept.CloseDesktopConnectionAsync();
            await ShowQrLoginAsync(shell);
        };
        var logout = new MenuItem { Header = "退出当前会话" };
        logout.Click += async (_, _) => await shell.Concept.CloseDesktopConnectionAsync();
        menu.Items.Add(details); menu.Items.Add(change); menu.Items.Add(logout);
        menu.ShowAt(button);
    }
    private async Task ShowQrLoginAsync(ShellModel shell)
    {
        if (Window is not { } owner || loginDialog != null) return;
        var account = shell.Concept.AccountPage;
        var oldParent = account.GetLogicalParent() as ContentControl;
        if(oldParent?.Content == account) oldParent.Content = null;
        var holder = new ContentControl { Content = account };
        var refresh = new Button { Content = "刷新二维码", Classes = { "glass-pill" } };
        var dialog = new Window
        {
            Title = "登录酷狗", Width = 460, Height = 440, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            RequestedThemeVariant = ActualThemeVariant,
            Content = new Border { Classes = { "glass", "glass-float" }, Margin = new Avalonia.Thickness(12),
                Child = new StackPanel { Children = { holder, refresh } } }
        };
        loginDialog = dialog;
        dialog.Closed += (_, _) =>
        {
            loginDialog = null;
            if (!shell.Concept.Connected) shell.Concept.CancelDesktopOperations();
            holder.Content = null;
            if(oldParent != null) oldParent.Content = account;
        };
        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = false;
            try
            {
                shell.Concept.CancelDesktopOperations();
                await shell.Concept.Pending;
                if(dialog.IsVisible&&!shell.Concept.Connected)await shell.Concept.StartDesktopQrAsync();
                if(dialog.IsVisible&&shell.Concept.Connected)dialog.Close();
            }
            finally { refresh.IsEnabled = true; }
        };
        var shown = dialog.ShowDialog(owner);
        await shell.Concept.StartDesktopQrAsync();
        if (shell.Concept.Connected && dialog.IsVisible) dialog.Close();
        await shown;
    }
    private Window? loginDialog;
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
        ToolTip.SetTip(MaximizeButton, window.WindowState == WindowState.Maximized ? "还原窗口" : "最大化窗口");
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
        QueueDrawer.RenderTransform = TransformOperations.Parse(open ? "translateX(0px)" : "translateX(380px)");
        if (open) LocateQueueCurrent();
    }

    /// <summary>After the queue drawer is laid out, scroll the current track to the centre
    /// without stealing the scroll position while the user is browsing the queue.</summary>
    private void LocateQueueCurrent()
    {
        var controller = queueController;
        if (controller == null) return;
        controller.RefreshCurrentState();
        Dispatcher.UIThread.Post(() =>
        {
            if (queueOpen && controller.HasCurrentTrack) controller.LocateCurrent();
        }, DispatcherPriority.Loaded);
    }

    private void TransportKeyDown(object? sender, KeyEventArgs args)
    {
        if (model == null)
            return;
        if (args.Key == Key.Escape && drawerCommentTrack != null)
        {
            CloseSongComments();
            args.Handled = true;
            return;
        }
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
