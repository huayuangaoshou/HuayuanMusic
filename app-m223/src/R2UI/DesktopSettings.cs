using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using KugouAvaloniaPlayer.Services;
using SimpleAudio;

namespace HuayuanPreview.R2;

public partial class R2ShellView
{
    private R2Page? settingsPage;
    private Task behaviorPreferenceWrite = Task.CompletedTask;
    private static readonly string[] CacheDirectories = ["kugou-artwork-cache", "media-session-artwork"];

    private Task PersistPreferenceAsync(Func<DesktopOptions, DesktopOptions> change, string setting = "behavior")
    {
        return behaviorPreferenceWrite = SaveAsync(behaviorPreferenceWrite);

        async Task SaveAsync(Task previous)
        {
            await previous;
            await RunPreferenceActionAsync(setting, () => DesktopPreferences.SetBehaviorAsync(change));
        }
    }

    private async Task RunPreferenceActionAsync(string setting, Func<Task> action)
    {
        try { await action(); }
        catch (Exception error)
        {
            Program.Log("DESKTOP_PREFERENCE_FAILED", new { setting, type = error.GetType().Name });
            ShowToast("设置暂未保存");
        }
    }

    private void OpenSettings(object? sender, RoutedEventArgs args)
    {
        if (model == null) return;
        settingsPage = new R2Page("设置", BuildSettings());
        model.Workspace.Navigate(settingsPage);
    }

    private Control BuildSettings()
    {
        var behavior = DesktopPreferences.Behavior;
        var body = new StackPanel { MaxWidth = 790, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 13, Margin = new Thickness(5) };
        var stats = new TextBlock { Classes = { "muted" }, Text = "正在统计缓存…" };
        var devices = new ComboBox { MinWidth = 230, MaxWidth = 400 };
        var labels = Array.Empty<AudioOutputDevice>();
        var clearing = false;
        void Section(string title, params Control[] fields)
        {
            var panel = new StackPanel { Spacing = 9 };
            panel.Children.Add(new TextBlock { Text = title, Classes = { "section-title" }, FontWeight = FontWeight.SemiBold });
            foreach (var field in fields) panel.Children.Add(field);
            body.Children.Add(new Border { Classes = { "porcelain" }, Padding = new Thickness(16,12), Child = panel });
        }
        static Control Row(string title, Control input)
        {
            var label = new TextBlock { Text=title, VerticalAlignment=VerticalAlignment.Center };
            var group = new Grid { ColumnDefinitions = new ColumnDefinitions("160,*"), ColumnSpacing=10 };
            Grid.SetColumn(input,1);
            group.Children.Add(label); group.Children.Add(input);
            return group;
        }
        static Button ButtonFor(string label, Action callback)
        {
            var button = new Button { Classes = { "glass-pill" }, Content = label };
            button.Click += (_, _) => callback();
            return button;
        }
        static ComboBox Options(string[] choices,int selected) => new()
        { ItemsSource=choices,SelectedIndex=selected,MinWidth=145,HorizontalAlignment=HorizontalAlignment.Left };
        ToggleSwitch Check(string label,bool value,Func<bool,DesktopOptions,DesktopOptions> update, Func<Task>? afterSave = null)
        {
            var check = new ToggleSwitch { OnContent=label,OffContent=label,IsChecked=value };
            check.IsCheckedChanged += (_, _) =>
            {
                var selected = check.IsChecked == true;
                _ = RunPreferenceActionAsync(label, async () =>
                {
                    await PersistPreferenceAsync(current => update(selected, current), label);
                    if (afterSave != null) await afterSave();
                });
            };
            return check;
        }
        Section("常规与窗口",
            Check("启动后恢复上次队列，保持就绪", behavior.RestoreQueue,(value,current)=>current with {RestoreQueue=value}),
            Check("自动监视音乐文件夹变化", behavior.WatchMusicFolders,
                (value, current) => current with { WatchMusicFolders = value },
                () => model?.FolderWatch?.SetEnabledAsync(DesktopPreferences.Behavior.WatchMusicFolders) ?? Task.CompletedTask));
        var quality=Options(["标准","高品 MP3","无损 FLAC"],QualityChoice.SelectedIndex);
        quality.SelectionChanged += (_,_) => QualityChoice.SelectedIndex=quality.SelectedIndex;
        var fade=Check("暂停、恢复与切歌微淡入淡出",behavior.MicroFade,(value,current)=>
        { return current with {MicroFade=value}; });
        fade.IsCheckedChanged+=(_,_)=> SimpleAudioPlayer.DesktopMicroFade=fade.IsChecked==true;
        Section("音频与播放",Row("默认音质",quality), Row("音频输出设备",devices),fade);
        var lyricFont = Options(["无衬线（默认）", "文楷（衬线）"], DesktopPreferences.DesktopLyrics.Serif ? 1 : 0);
        lyricFont.SelectionChanged += (_, _) =>
        {
            if (lyricFont.SelectedIndex < 0) return;
            var serif = lyricFont.SelectedIndex == 1;
            desktopLyrics?.SetSerif(serif);
            var options = desktopLyrics?.Capture(true) ?? DesktopPreferences.DesktopLyrics;
            QueueLyricPreferences(options with { Serif = serif });
        };
        var lyricBackground = Options(["透明", "实底"], behavior.LyricBackground == DesktopLyricBackground.Transparent ? 0 : 1);
        lyricBackground.SelectionChanged += async (_, _) =>
        {
            if (lyricBackground.SelectedIndex < 0) return;
            var selected = lyricBackground.SelectedIndex == 0 ? DesktopLyricBackground.Transparent : DesktopLyricBackground.Solid;
            await PersistPreferenceAsync(current => current with { LyricBackground = selected }, "desktop_lyric_background");
            desktopLyrics?.SetBackgroundMode(DesktopPreferences.Behavior.LyricBackground);
        };
        Section("歌词设置", Row("桌面歌词字体", lyricFont), Row("底色模式", lyricBackground));
        var downloadPath = new TextBlock { Text = DesktopPreferences.DownloadFolder,
            Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
        var changeDownloadPath = new Button { Content = "更改下载位置", Classes = { "glass-pill" } };
        changeDownloadPath.Click += (_, _) => _ = RunPreferenceActionAsync("download_folder", async () =>
        {
            if (TopLevel.GetTopLevel(this) is not { } window) return;
            var choices = await window.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "选择下载位置" });
            string? folder;
            try { folder = choices.FirstOrDefault()?.TryGetLocalPath(); }
            finally { foreach (var choice in choices) choice.Dispose(); }
            if (folder == null) return;
            await PersistPreferenceAsync(options => options with
            {
                DownloadFolder = folder,
                PreviousDownloadFolders = new[] { DesktopPreferences.DownloadFolderFor(options) }
                    .Concat(options.PreviousDownloadFolders ?? [])
                    .Where(path => !string.Equals(path, folder, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray()
            }, "download_folder");
            downloadPath.Text = DesktopPreferences.DownloadFolder;
        });
        Section("歌曲下载", downloadPath, new WrapPanel { ItemSpacing = 7, LineSpacing = 7, Children =
            { changeDownloadPath, ButtonFor("打开下载目录", OpenDownloadFolder) } });
        var clear=new Button { Classes={"glass-pill"},Content="一键清理缓存" };
        clear.Click += async (_,_) => await RefreshCacheAsync(true);
        Section("缓存与存储", stats, new WrapPanel { ItemSpacing=7, LineSpacing=7, Children={
            ButtonFor("打开缓存目录",OpenCacheFolder),clear } });

        var version = new TextBlock { Text = $"当前版本 v{HuayuanPreview.Services.UpdateManager.CurrentVersion}",
            Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
        var checkUpdate = new Button { Content = "检查更新", Classes = { "glass-pill" } };
        checkUpdate.Click += CheckForUpdates;
        Section("关于与更新", Row("版本", version),
            new WrapPanel { ItemSpacing = 7, LineSpacing = 7, Children = { checkUpdate } });
        body.AttachedToVisualTree += (_, _) => _ = RunPreferenceActionAsync("load_settings", async () =>
        {
            labels = await Task.Run(()=>SimpleAudioPlayer.GetOutputDevices().ToArray());
            devices.ItemsSource = labels.Select(item=>item.Name).ToArray();
            var index=Array.FindIndex(labels,item=>item.DeviceId==SettingsManager.Settings.AudioOutputDeviceId);
            devices.SelectedIndex = index>=0 ? index : 0;
            await RefreshCacheAsync(false);
        });
        devices.SelectionChanged += (_, _) => _ = RunPreferenceActionAsync("audio_output", () =>
        {
            if(devices.SelectedIndex<0||devices.SelectedIndex>=labels.Length||model==null)return Task.CompletedTask;
            var id=labels[devices.SelectedIndex].DeviceId;
            if(id==SettingsManager.Settings.AudioOutputDeviceId)return Task.CompletedTask;
            if(model.Adapter.ChangeDesktopOutput(id))ShowToast("已切换音频输出设备");
            else
            {
                ShowToast("音频设备不可用，保留原设备");
                devices.SelectedIndex=Array.FindIndex(labels,device=>device.DeviceId==SettingsManager.Settings.AudioOutputDeviceId);
            }
            return Task.CompletedTask;
        });
        var refresh = new DispatcherTimer { Interval=TimeSpan.FromSeconds(6) };
        var cacheCensus = new TimerCensus("settingsCacheRefresh", refresh);
        refresh.Tick += async (_,_) =>
        {
            Task work;
            using (cacheCensus.Measure()) work = RefreshCacheAsync(false);
            await work;
        };
        body.AttachedToVisualTree += (_,_) => refresh.Start();
        body.DetachedFromVisualTree += (_,_) => refresh.Stop();
        return new ScrollViewer { Content=body, HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        async Task RefreshCacheAsync(bool delete)
        {
            if(clearing)return;
            clearing=true;
            clear.IsEnabled=false;
            try
            {
                var report=await Task.Run(()=>MeasureCoverCache(delete));
                stats.Text=$"封面磁盘缓存 {report.Bytes/1048576d:0.##} MiB  ·  音频磁盘缓存 0 B";
                if(delete)ShowToast($"已清理 {report.Removed:N0} 个封面文件，在播音频继续播放");
            }
            catch(Exception error) when(error is IOException or UnauthorizedAccessException)
            { stats.Text="暂时无法读取缓存目录"; }
            finally { clearing=false;clear.IsEnabled=true; }
        }
    }

    private static (long Bytes,int Removed) MeasureCoverCache(bool delete)
    {
        long bytes=0;
        int removed=0;
        foreach(var subfolder in CacheDirectories)
        {
            var folder=new DirectoryInfo(Path.Combine(AppDataPaths.LocalRoot,subfolder));
            if(!folder.Exists||(folder.Attributes&FileAttributes.ReparsePoint)!=0)continue;
            foreach(var file in folder.EnumerateFiles())
            {
                if((file.Attributes&FileAttributes.ReparsePoint)!=0)continue;
                if(delete)
                {
                    try { file.Delete();removed++;continue; }
                    catch(Exception error) when(error is IOException or UnauthorizedAccessException) { }
                }
                if(file.Exists)bytes+=file.Length;
            }
        }
        return (bytes,removed);
    }

    private void OpenCacheFolder()
    {
        try
        {
            var folder=Path.Combine(AppDataPaths.LocalRoot,CacheDirectories[0]);
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder){UseShellExecute=true});
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { ShowToast("暂时无法打开缓存目录"); }
    }
}
