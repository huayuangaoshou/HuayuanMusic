using System.Diagnostics;
namespace HuayuanPreview;
public sealed partial class PreviewWindow{
 async Task C31Bench(){
  await Task.Delay(2000);var process=Process.GetCurrentProcess();var cpu=process.TotalProcessorTime;var clock=Stopwatch.StartNew();var costs=new List<double>();var memory=new List<long>();
  Program.Log("BENCH_BEGIN",new{pid=Environment.ProcessId,args=Program.Args,RenderScaling,Width,Height,logicalCPUs=Environment.ProcessorCount});
  for(int step=0;step<40;step++){
   var action=Stopwatch.StartNew();
   switch(step%8){
    case 0:Go("首页");break;case 1:Go("本地音乐");break;
    case 2:queueOpen=true;Render();break;
    case 3:Go("正在播放");seconds=(step%6)*32;UpdatePlayback();break;
    case 4:queueOpen=false;Render();break;
    case 5:OpenPlatform("qq");chosenAccount["qq"]="alt";Render();break;
    case 6:OpenPlatform("kugou");break;case 7:Go("本地歌单");break;
   }
   UpdateLayout();action.Stop();costs.Add(action.Elapsed.TotalMilliseconds);process.Refresh();memory.Add(process.WorkingSet64);await Task.Delay(400);
  }
  clock.Stop();process.Refresh();var cpuSeconds=(process.TotalProcessorTime-cpu).TotalSeconds;
  Program.Log("BENCH_END",new{seconds=clock.Elapsed.TotalSeconds,cpuSeconds,oneCoreCPUPercent=100*cpuSeconds/clock.Elapsed.TotalSeconds,totalCPUPercent=100*cpuSeconds/clock.Elapsed.TotalSeconds/Environment.ProcessorCount,workingSetMiB=process.WorkingSet64/1048576d,peakWorkingSetMiB=process.PeakWorkingSet64/1048576d,workingSetMinMiB=memory.Min()/1048576d,workingSetMaxMiB=memory.Max()/1048576d,uiActionLayoutMs=costs,notice="UI action/layout durations are not presentation frame times; use external PID-filtered PresentMon CSV"});Close();
 }
}
