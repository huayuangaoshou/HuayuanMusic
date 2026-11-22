using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace HuayuanPreview;
public sealed partial class PreviewWindow {
 // A classification command is a native tab selection, not a drag gesture or material demo.
 Control LibraryCategoryTabs(){
  var names=new[]{"歌曲","专辑","歌手","歌单"};
  var tabs=new TabControl{Name="LibraryCategories",Height=42,HorizontalAlignment=HorizontalAlignment.Left,Background=Brushes.Transparent};
  Avalonia.Automation.AutomationProperties.SetName(tabs,"曲库分类");
  foreach(var name in names){var tab=new TabItem{Header=new TextBlock{Text=name,FontSize=13},FontSize=13,Padding=new Thickness(14,8),MinHeight=36};Avalonia.Automation.AutomationProperties.SetName(tab,name);tabs.Items.Add(tab);}
  tabs.SelectedIndex=Math.Max(0,Array.IndexOf(names,nightTab));
  tabs.SelectionChanged+=(_,_)=>{
   var index=tabs.SelectedIndex;if(index<0||index>=names.Length||nightTab==names[index])return;
   nightTab=names[index];Render();
   Dispatcher.UIThread.Post(()=>{
    var replacement=this.GetVisualDescendants().OfType<TabControl>().FirstOrDefault(x=>x.Name=="LibraryCategories");
    if(page=="本地音乐"&&nightTab==names[index]&&replacement?.SelectedItem is TabItem selected)selected.Focus();
   },DispatcherPriority.Loaded);
  };
  return tabs;
 }
}
