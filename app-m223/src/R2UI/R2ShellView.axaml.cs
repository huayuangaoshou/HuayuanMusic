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
    private readonly DispatcherTimer drawerDelay = new() { Interval = DesignTokens.MotionSpatialDefault.Duration };
    private readonly DispatcherTimer toastDelay = new() { Interval = TimeSpan.FromMilliseconds(2250) };
    private ShellModel? model;
    private InteractionController? queueController;
    private InteractionController.ViewState? queueViewState;
    private TrackRow[]? queueRows;
    private Guid queueEntry;
    private bool queueOpen;
    private bool updatingFilter;
    private string? ignoredFilterText;

    public R2ShellView()
    {
        InitializeComponent();
        InitializeOnlineSearch();
        if (!DesignTokens.ShaderDegrade)
        {
            MainSidebar.Classes.Add("glass-enter");
            MainDock.Classes.Add("glass-enter");
            SuggestionsPopup.Opened += (_, _) => SuggestionsSurface.Classes.Add("popup-enter");
            SuggestionsPopup.Closed += (_, _) => SuggestionsSurface.Classes.Remove("popup-enter");
        }
        InitializeLyricOverlay();
        QueueEmpty.Empty("队列还空着。放一首喜欢的歌进来。", "浏览本地曲库", () =>
        {
            SetQueueOpen(false);
            if (lyricOpen) CloseLyricOverlay();
            if (model is not { } shell) return;
            shell.Switch(shell.LocalWorkspace);
            shell.LocalWorkspace.Navigate(shell.LocalWorkspace.CatalogPage);
        });
        QualityChoice.SelectedIndex=FullQuality.SelectedIndex=DesktopPreferences.Quality switch { "320"=>1,"flac"=>2,_=>0 };
        filterDelay.Tick += (_, _) =>
        {
            filterDelay.Stop();
            if(model?.Workspace.Local==true) model.Workspace.ApplyFilter(CurrentFilter.Text ?? "");
        };
        drawerDelay.Tick += (_, _) => { drawerDelay.Stop(); if (!queueOpen) { DrawerLayer.IsVisible = false; QueueDrawerSurface.Classes.Remove("glass-enter"); } };
        toastDelay.Tick += (_, _) =>
        {
            toastDelay.Stop();
            SetToastTransition(DesignTokens.MotionEffectsDefault);
            ToastPlate.Opacity = 0;
            DispatcherTimer.RunOnce(() => { if (!toastDelay.IsEnabled) ToastPlate.IsVisible = false; }, DesignTokens.MotionEffectsDefault.Duration);
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

    internal void FocusSearch() => CurrentFilter.Focus();

    private void ShowToast(string text) => ShowFeedback(text, notice: false);
    private void ShowNotice(string text) => ShowFeedback(text, notice: true);

    private void ShowFeedback(string text, bool notice)
    {
        ToastPlate.Classes.Set("notice", notice);
        ToastText.Text = text;
        SetToastTransition(DesignTokens.MotionEffectsFast);
        ToastPlate.IsVisible = true;
        ToastPlate.Opacity = 1;
        toastDelay.Stop();
        toastDelay.Start();
    }

    private void SetToastTransition(DesignTokens.MotionSpec spec)
    {
        if (ToastPlate.Transitions?[0] is Avalonia.Animation.DoubleTransition transition)
        {
            transition.Duration = spec.Duration;
            transition.Easing = spec.Curve;
        }
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
        try { await DesktopPreferences.ChooseQualityAsync(selected); if (model != null) ShowNotice($"已切换音质 · {new[] { "标准", "高品 MP3", "无损 FLAC" }[index]}"); Program.Log("R2_QUALITY_PREFERENCE",new{quality=selected}); }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        { ShowNotice("音质设置暂未保存"); Program.Log("R2_QUALITY_WRITE_FAILED",new{type=error.GetType().Name}); }
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
        if (model != null && queueController is { } controller)
        {
            if (!ReferenceEquals(queueRows, model.Player.DisplayPlaybackQueue))
            {
                queueRows = model.Player.DisplayPlaybackQueue;
                controller.SetRows(queueRows);
                controller.RefreshCurrentState();
            }
            var entry = model.Player.State.CurrentEntryId;
            if (queueEntry != entry)
            {
                queueEntry = entry;
                if (queueOpen)
                {
                    controller.RefreshCurrentState();
                    controller.FollowCurrent();
                }
            }
        }
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
            // 无封面：回退到底色。Mica 生效时保持半透明以透出材质。
            target.Background = backdropMica
                ? Root.Background
                : (IBrush)Application.Current!.Resources["GlassBase"]!;
            return;
        }
        try
        {
            if (row.IsProviderTrack) { return; } // 在线曲目封面走 Control，本地取色仅覆盖本地封面
            // R18d: LoadLocalCover returns a SHARED, weakly-cached Bitmap (see
            // KugouArtwork.LoadLocal -> BoundedDiskCachedWebImageLoader.ProvideImageAsync).
            // The very same instance is bound to Image.Source by TrackArtwork rows. Never
            // Dispose it here: doing so kills the pixels still referenced by those Images and
            // the next layout pass throws ObjectDisposedException at Bitmap.get_Size().
            // Ownership stays with the cache; the extractor only reads pixels via a scaled copy.
            Bitmap? bitmap = await model.Concept.LoadLocalCover(row.Track.Cover!);
            if (request.IsCancellationRequested || tintTrackKey != key) return;
            var main = await CoverColorExtractor.ExtractAsync(bitmap, request.Token);
            if (request.IsCancellationRequested || tintTrackKey != key || main is not { } color) return;
            var dark = ActualThemeVariant == ThemeVariant.Dark;
            var tint = CoverColorExtractor.TintForBackground(color, dark);
            target.Background = new SolidColorBrush(tint);
        }
        catch (OperationCanceledException) { }
        catch { /* 取色失败回退，不报错 */ }
    }
    private Window? Window => TopLevel.GetTopLevel(this) as Window;

    // R6: 窗口底真实材质。Mica 生效时把根背景压到半透明，让系统材质透出；回退时恢复 GlassBase 平涂兜底。
    private bool backdropMica;
    internal void ApplyBackdrop(bool mica)
    {
        backdropMica = mica;
        var baseColor = ActualThemeVariant == ThemeVariant.Dark ? DesignTokens.DarkBase : DesignTokens.LightBase;
        var surface = mica
            ? Color.FromArgb(219, baseColor.R, baseColor.G, baseColor.B) // ~86% 不透明，与已有 Mica 路径的 WindowFill 一致
            : baseColor;
        var backdrop = new SolidColorBrush(surface);
        Root.Background = backdrop;
        // 无封面时 CoverTint 回退到底色；材质激活时改用半透明底，避免盖住 Mica。
        if (tintTrackKey == null)
            CoverTint.Background = backdrop;
    }

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
        var menu = new AnchoredMenuFlyout();
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
            Content = GlassDialogSurface.Create(new StackPanel { Children = { holder, refresh } }, new Thickness(0))
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

    private static void SetFadeDirection(Border surface, bool entering)
    {
        if (surface.Transitions?[0] is Avalonia.Animation.DoubleTransition fade)
            fade.Easing = entering ? DesignTokens.EmphasizedDecelerate : DesignTokens.EmphasizedAccelerate;
    }

    private void SetQueueOpen(bool open)
    {
        if (!open && queueOpen && queueController is { } closing)
        {
            queueViewState = closing.CaptureView();
            closing.CancelFollow();
        }
        queueOpen = open;
        drawerDelay.Stop();
        if (open) DrawerLayer.IsVisible = true; else drawerDelay.Start();
        if (open && !DesignTokens.ShaderDegrade) QueueDrawerSurface.Classes.Add("glass-enter");
        DrawerLayer.IsHitTestVisible = open;
        SetFadeDirection(DrawerShade, open);
        DrawerShade.Opacity = open ? 1 : 0;
        QueueDrawer.RenderTransform = TransformOperations.Parse(open ? "translateX(0px)" : "translateX(380px)");
        if (open && queueController is { } opening)
        {
            opening.RefreshCurrentState();
            if (queueViewState is { } saved) opening.RestoreView(saved);
            else opening.FollowCurrent(firstOpen: true);
        }
    }
    internal bool IsPlaybackListExposed(bool queue) => queue ? queueOpen : !lyricOpen && !queueOpen;

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
