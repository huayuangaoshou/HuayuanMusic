using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using KugouAvaloniaPlayer.Services;
using SimpleAudio;

namespace HuayuanPreview.R2;

public partial class R2ShellView
{
    private R2Page? settingsPage;
    private static readonly string[] CacheDirectories = ["kugou-artwork-cache", "media-session-artwork"];

    private async Task PersistPreferenceAsync(Func<DesktopOptions, DesktopOptions> change)
    {
        try { await DesktopPreferences.SetBehaviorAsync(change); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { ShowToast("设置暂未保存"); Program.Log("DESKTOP_PREFERENCE_FAILED", new { type=error.GetType().Name }); }
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
            panel.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeight.SemiBold });
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
        ToggleSwitch Check(string label,bool value,Func<bool,DesktopOptions,DesktopOptions> update)
        {
            var check = new ToggleSwitch { OnContent=label,OffContent=label,IsChecked=value };
            check.IsCheckedChanged += async (_,_) => await PersistPreferenceAsync(current=>update(check.IsChecked==true,current));
            return check;
        }
        var close = Options(["最小化到系统托盘","直接退出"],behavior.TrayOnClose ? 0 : 1);
        close.SelectionChanged += async (_,_) => await PersistPreferenceAsync(current=>current with {TrayOnClose=close.SelectedIndex==0});
        var theme = Options(["浅色粉蓝白","深色夜蓝"],ActualThemeVariant==ThemeVariant.Dark ? 1 : 0);
        theme.SelectionChanged += async (_,_) => await SetDesktopThemeAsync(theme.SelectedIndex==1);
        Section("常规与窗口", Row("关闭主窗口",close), Row("界面主题",theme),
            Check("启动后恢复上次队列，保持就绪", behavior.RestoreQueue,(value,current)=>current with {RestoreQueue=value}));
        var quality=Options(["标准","高品 MP3","无损 FLAC"],QualityChoice.SelectedIndex);
        quality.SelectionChanged += (_,_) => QualityChoice.SelectedIndex=quality.SelectedIndex;
        var fade=Check("暂停、恢复与切歌微淡入淡出",behavior.MicroFade,(value,current)=>
        { return current with {MicroFade=value}; });
        fade.IsCheckedChanged+=(_,_)=> SimpleAudioPlayer.DesktopMicroFade=fade.IsChecked==true;
        Section("音频与播放",Row("默认音质",quality), Row("音频输出设备",devices),fade);
        Section("歌词设置", new WrapPanel { ItemSpacing=6, LineSpacing=6, Children={
            ButtonFor("显示 / 隐藏桌面歌词",ToggleDesktopLyricsFromHost),
            ButtonFor("解锁鼠标穿透",UnlockDesktopLyricsFromHost),
            ButtonFor("重置位置和字号",ResetDesktopLyricsFromHost) } });
        var downloadPath = new TextBlock { Text = DesktopPreferences.DownloadFolder,
            Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
        var changeDownloadPath = new Button { Content = "更改下载位置", Classes = { "glass-pill" } };
        changeDownloadPath.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is not { } window) return;
            var choices = await window.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "选择下载位置" });
            if (choices.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return;
            await PersistPreferenceAsync(options => options with
            {
                DownloadFolder = folder,
                PreviousDownloadFolders = new[] { DesktopPreferences.DownloadFolderFor(options) }
                    .Concat(options.PreviousDownloadFolders ?? [])
                    .Where(path => !string.Equals(path, folder, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray()
            });
            downloadPath.Text = DesktopPreferences.DownloadFolder;
        };
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
        body.AttachedToVisualTree += async (_,_) =>
        {
            labels = await Task.Run(()=>SimpleAudioPlayer.GetOutputDevices().ToArray());
            devices.ItemsSource = labels.Select(item=>item.Name).ToArray();
            var index=Array.FindIndex(labels,item=>item.DeviceId==SettingsManager.Settings.AudioOutputDeviceId);
            devices.SelectedIndex = index>=0 ? index : 0;
            await RefreshCacheAsync(false);
        };
        devices.SelectionChanged += (_,_) =>
        {
            if(devices.SelectedIndex<0||devices.SelectedIndex>=labels.Length||model==null)return;
            var id=labels[devices.SelectedIndex].DeviceId;
            if(id==SettingsManager.Settings.AudioOutputDeviceId)return;
            if(model.Adapter.ChangeDesktopOutput(id))ShowToast("已切换音频输出设备");
            else
            {
                ShowToast("音频设备不可用，保留原设备");
                devices.SelectedIndex=Array.FindIndex(labels,device=>device.DeviceId==SettingsManager.Settings.AudioOutputDeviceId);
            }
        };
        var refresh = new DispatcherTimer { Interval=TimeSpan.FromSeconds(6) };
        refresh.Tick += async (_,_) => await RefreshCacheAsync(false);
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
