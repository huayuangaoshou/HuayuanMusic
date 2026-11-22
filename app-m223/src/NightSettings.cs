using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 string nightSettingsGroup="外观";
 Control NightSettingsExact(){var p=V(NH("MAKE IT YOURS","设置","把界面留给音乐，把偏好留给自己。"));p.Spacing=0;var layout=new Grid{ColumnDefinitions=ColumnDefinitions.Parse(narrow?"125,*":"160,*"),ColumnSpacing=30};var nav=V();nav.Spacing=6;foreach(var group in new[]{"外观","播放","歌词","性能与辅助","隐私与权限"}){var g=group;var b=NB(g,()=>{nightSettingsGroup=g;Render();});b.HorizontalAlignment=HorizontalAlignment.Stretch;b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Background=nightSettingsGroup==g?R("Selected"):Brushes.Transparent;nav.Children.Add(b);}layout.Children.Add(nav);var main=V();main.Spacing=12;main.MaxWidth=720;main.HorizontalAlignment=HorizontalAlignment.Stretch;
  Control Row(string title,string detail,Control action){var row=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("*,Auto"),ColumnSpacing=24};row.Children.Add(V(T(title,13,false,true),T(detail,11,true)));Grid.SetColumn(action,1);row.Children.Add(action);return new Border{Padding=new(0,15),BorderBrush=R("Line"),BorderThickness=new(0,0,0,1),Child=row};}
  Control Toggle(string label,bool value,Action a){var t=new ToggleSwitch{IsChecked=value};Avalonia.Automation.AutomationProperties.SetName(t,label);t.Click+=(_,_)=>a();return t;}
  if(nightSettingsGroup=="隐私与权限"){main.Children.Add(KugouPrivacyPage());}else if(nightSettingsGroup=="外观"){
   main.Children.Add(T("主题与氛围",15,false,true));main.Children.Add(T("温柔的日间，或安静的深夜。",11,true));var themes=H();foreach(var isDark in new[]{false,true}){var t=isDark;var preview=new Grid{ColumnDefinitions=ColumnDefinitions.Parse("16,*"),ColumnSpacing=6,Background=Brush.Parse(t?"#131928":"#f4f5fa"),Height=64};preview.Children.Add(new Border{Background=Brush.Parse("#25b7bfe0"),CornerRadius=new(3),Margin=new(8)});var inner=new Border{Background=Gradient(t?"#283e7b":"#6b8ddc",t?"#644674":"#c8a9d5"),Margin=new(3,9,9,9),CornerRadius=new(4)};Grid.SetColumn(inner,1);preview.Children.Add(inner);var card=NB("",()=>{dark=t;Render();});card.Content=V(preview,T(t?"夜航 · 深色":"晨雾 · 浅色",10));card.Width=150;card.Padding=new(7);card.BorderThickness=new(1);card.BorderBrush=dark==t?R("Primary"):R("Line");card.Background=R("Surface");themes.Children.Add(card);}main.Children.Add(themes);
   var strength=new Slider{Minimum=0,Maximum=1,Value=materialStrength,Width=130};Avalonia.Automation.AutomationProperties.SetName(strength,"材质强度");strength.PropertyChanged+=(_,e)=>{if(e.Property==Avalonia.Controls.Primitives.RangeBase.ValueProperty)materialStrength=strength.Value;};main.Children.Add(Row("材质强度","仅局部交互使用玻璃；主体保留柔光陶瓷。",strength));main.Children.Add(Row("高对比度","以清晰边界和可见焦点优先。",Toggle("高对比度",c31Contrast,()=>{c31Contrast=!c31Contrast;Render();})));
  }else if(nightSettingsGroup=="性能与辅助"){
   main.Children.Add(T("安静而及时的反馈",15,false,true));main.Children.Add(Row("轻量材质","关闭复杂材质和浮动阴影，使用实体表面。",Toggle("轻量材质",c31Low,()=>{c31Low=!c31Low;Render();})));main.Children.Add(Row("减少动态效果","关闭位移和回弹；同时遵从系统减少动态偏好。",Toggle("减少动态",reduced,()=>{reduced=!reduced;Render();})));main.Children.Add(Row("材质效果","主控件保留独立的表面与高光层。",Toggle("材质效果",c31Materials,()=>{c31Materials=!c31Materials;Render();})));
  }else if(nightSettingsGroup=="歌词"){
   main.Children.Add(T("歌词与专辑",15,false,true));main.Children.Add(Row("当前句层级","字重、轻微缩放和定位点；不遮挡歌词。",NB("预览 →",()=>Go("正在播放"))));main.Children.Add(Row("环境光","封面关联预设取色，650ms 原生画刷过渡。",T("封面关联",11,true)));var b=NB("未接入",()=>{});b.IsEnabled=false;main.Children.Add(Row("桌面歌词","本轮不创建真实桌面歌词窗口或原生助手。",b));
  }else{
   main.Children.Add(T("播放偏好",15,false,true));main.Children.Add(Row("本地播放","真实播放复用原生产核心，仅限已授权合成媒体。",T("受控真实播放",11,true)));foreach(var title in new[]{"恢复播放队列","音频输出设备"}){var b=NB("未接入",()=>{});b.IsEnabled=false;main.Children.Add(Row(title,"本轮不开放设备切换或自动队列恢复。",b));}
  }
  main.Children.Add(new Border{Background=R("Selected"),CornerRadius=new(11),Padding=new(14),Margin=new(0,18,0,0),Child=T("隐私与权限保存在本候选资料中；界面外观设置只作用于当前会话。",11,true)});Grid.SetColumn(main,1);layout.Children.Add(main);p.Children.Add(layout);return p;
 }
}
