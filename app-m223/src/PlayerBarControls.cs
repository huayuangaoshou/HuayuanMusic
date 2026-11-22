using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 readonly List<(Button Button,int Track)> favoriteControls=new();readonly List<CommitSlider> volumeControls=new();
 readonly Dictionary<int,TextBlock> playingMarkers=new();Button? volumeAction;Button? popupMute;Flyout? volumeFlyout;TextBlock? volumePercent;
 Control FavoriteGlyph(bool liked){var red=HighContrast?R("Primary"):Brush.Parse(dark?"#F27B90":"#CF526A");var data=NightIcons.Paths["heart"];if(!iconCache.TryGetValue(data,out var geometry))iconCache[data]=geometry=Geometry.Parse(data);return new Avalonia.Controls.Shapes.Path{Data=geometry,Width=20,Height=20,Stretch=Stretch.Uniform,Stroke=liked?red:R("Text"),Fill=liked?red:Brushes.Transparent,StrokeThickness=1.6,StrokeJoin=PenLineJoin.Round};}
 Button CoreLikeButton(){int id=core.State.CurrentId>0?core.State.CurrentId:selectedSong;var b=NI("heart","收藏",()=>Like(core.State.CurrentId>0?core.State.CurrentId:selectedSong));b.Name="CoreFavorite";favoriteControls.Add((b,id));PaintFavorite(b,id);return b;}
 void PaintFavorite(Button b,int id){bool liked=core.State.Favorites.Contains(id);var visual=(liked,dark,HighContrast);if(Equals(b.Tag,visual))return;b.Tag=visual;b.Content=FavoriteGlyph(liked);b.Background=Brushes.Transparent;AutomationProperties.SetName(b,liked?"取消收藏":"收藏");AutomationProperties.SetHelpText(b,liked?"已收藏：真实本地SQLite记录":"未收藏");ToolTip.SetTip(b,liked?"取消收藏":"收藏歌曲");}
 CommitSlider MakeSeek(){int editTrack=0;var slider=new CommitSlider{Name="CoreSeek",Minimum=0,Maximum=Math.Max(1,core.State.Duration>0?core.State.Duration:Track(nowSong).Duration),MinWidth=70};AutomationProperties.SetName(slider,"播放进度");ToolTip.SetTip(slider,"拖动预览，松开定位；方向键1秒，Page键5秒，Esc取消");slider.Sync(core.State.Position);slider.EditStarted+=()=>editTrack=core.State.CurrentId;
  slider.Submit=value=>{int expected=editTrack;return SubmitCore(async()=>{if(core.State.CurrentId!=expected){Notify("已切换歌曲，取消旧定位");return;}await core.SeekAsync(value);});};slider.PreviewChanged+=UpdatePlaybackText;return slider;}
 CommitSlider MakeVolume(double width){var slider=new CommitSlider{Minimum=0,Maximum=1,SmallChange=.05,LargeChange=.1,Width=width};AutomationProperties.SetName(slider,"音量");ToolTip.SetTip(slider,"音量：拖动预览后松开应用；方向键每次5%");slider.Sync(core.State.Volume);slider.Submit=value=>SubmitCore(()=>core.VolumeAsync(value));volumeControls.Add(slider);return slider;}
 void MuteFromBar(){foreach(var s in volumeControls)s.CancelPreview();RunCore(core.MuteAsync);}
 string VolumeTip=>core.State.Volume<=0?"音量为零（静音）；点击恢复上次音量":$"静音 · 当前音量 {core.State.Volume:P0}";
 Button VolumeButton(){var b=NI(core.State.Volume<=0?"mute":"volume",narrow?"音量设置":"静音",()=>{if(narrow)OpenVolume();else MuteFromBar();});b.Name="CoreVolumeButton";volumeAction=b;ToolTip.SetTip(b,narrow?"打开音量调节":VolumeTip);return b;}
 void OpenVolume(){if(volumeAction==null)return;if(volumeFlyout?.IsOpen==true){volumeFlyout.Hide();return;}var slider=MakeVolume(184);popupMute=NB(core.State.Volume<=0?"恢复音量":"静音",MuteFromBar);popupMute.Name="PopupMute";volumePercent=T(core.State.Volume.ToString("P0"),12,false,true);var panel=new StackPanel{Spacing=10,Children={H(T("应用音量",13,false,true),volumePercent),slider,popupMute}};
  var openedFlyout=volumeFlyout=new Flyout{Content=new Border{Width=224,MinHeight=128,Padding=new(16),Background=R("Surface"),BorderBrush=R("Line"),BorderThickness=new(1),CornerRadius=new(12),Child=panel}};
  volumeFlyout.Closed+=(_,_)=>{slider.CancelPreview();volumeControls.Remove(slider);if(ReferenceEquals(volumeFlyout,openedFlyout)){popupMute=null;volumePercent=null;}};volumeFlyout.ShowAt(volumeAction);slider.Focus();}
 void RefreshBarState(){foreach(var (b,id) in favoriteControls)PaintFavorite(b,id);foreach(var v in volumeControls)v.Sync(core.State.Volume);
  if(volumeAction!=null){var icon=(core.State.Volume<=0,dark,HighContrast);if(!Equals(volumeAction.Tag,icon)){volumeAction.Tag=icon;volumeAction.Content=C3Icon(core.State.Volume<=0?"mute":"volume");}AutomationProperties.SetName(volumeAction,narrow?"音量设置":core.State.Volume<=0?"恢复音量":"静音");ToolTip.SetTip(volumeAction,narrow?$"打开音量调节 · {core.State.Volume:P0}":VolumeTip);}
  if(popupMute!=null){popupMute.Content=core.State.Volume<=0?"恢复音量":"静音";AutomationProperties.SetName(popupMute,core.State.Volume<=0?"恢复音量":"静音");}if(volumePercent!=null)volumePercent.Text=core.State.Volume.ToString("P0");
  foreach(var (id,t) in playingMarkers){bool current=id==core.State.CurrentId;t.Text=current?(core.State.Playing?"▶":core.State.Paused?"Ⅱ":"●"):id.ToString("00");t.Foreground=R(current?"Primary":"Sub");ToolTip.SetTip(t,current?(core.State.Playing?"当前播放":core.State.Paused?"当前曲目 · 已暂停":"当前曲目 · 已停止"):"歌曲序号");}
 }
 Button CurrentMuteControl()=>narrow?(popupMute??throw new InvalidOperationException("Open volume popup first")):(volumeAction??throw new InvalidOperationException());
 Control EmptySearch(){var text=T("没有找到匹配的歌曲",17,false,true);var box=V(text,T("仅搜索当前隔离曲库；试试歌名、歌手或专辑。",12,true),NB("清除搜索",()=>{query="";RefreshRows();}));box.Name="LibraryEmpty";box.Margin=new(0,22);return box;}
 Task SubmitCore(Func<Task> operation){RunCore(operation);return PendingCoreOperations;}
}
