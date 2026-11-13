using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace Vow.Performance;

[BepInPlugin("local.heroesvow.framepacing","Model frame pacing","1.3.7")]
public sealed class FramePacing:BasePlugin
{
    internal static FramePacing mod;
    internal static ConfigEntry<bool> enabled;
    static ConfigEntry<bool> measure,distant;
    internal static bool Measure=>measure?.Value==true;
    static int slow,fast;static bool crowded;
    static Harmony hooks;
    static bool legacy;
    static FieldInfo nextPose,poseInterval,lastMotion,nextScan;
    static long modelTicks;static int modelCalls;
    static double modelWorst;
    sealed class Slot {internal int phase;}
    static readonly ConditionalWeakTable<object,Slot> phases=new();
    static int serial;
    public override void Load(){
        mod=this;hooks=new Harmony("local.heroesvow.framepacing");
        enabled=Config.Bind("Performance","Enabled",true,"Offscreen model LOD, staggered distant poses and less frequent fallback scans. Visible combat animation is unchanged.");
        measure=Config.Bind("Diagnostics","FrameSummary",true,"A bounded summary every 30 seconds. No per-frame disk writes or popups.");
        distant=Config.Bind("Performance","AdaptiveDistantModels",true,"When frame time exceeds 33 ms for three seconds, tiny non-combat world models use 100/180 ms pose updates. Close-up, editor and visible combat models keep their native animation cadence.");
        enabled.Value=true;distant.Value=true;
        RuntimeDiscovery.Start();
        AddComponent<FrameSampler>();
    }
    internal static void Connect(){
        while(RuntimeDiscovery.TryTake(out var a))try{
            var indoorHooks=new Harmony("local.heroesvow.model.indoorlimit");
            try{IndoorCompanionLimit.Connect(a,indoorHooks);}catch(Exception e){indoorHooks.UnpatchSelf();mod.Log.LogWarning("Indoor companion limit not connected: "+e.Message);}
            LegacyBundles.Connect(a,hooks);
            RigClassification.Connect(a,hooks);
            AnimatedFrames.Connect(a,hooks);
            ClothWork.Connect(a,hooks);
            DressWork.Connect(a,hooks);
            RuntimeSearch.Connect(a,hooks);
            PortraitWork.Connect(a,hooks);
            var contactHooks=new Harmony("local.heroesvow.framepacing.combatcloth");
            try{CombatCloth.Connect(a,contactHooks);}catch(Exception e){contactHooks.UnpatchSelf();mod.Log.LogWarning("Combat cloth hooks retained native behavior: "+e.Message);}
            var hotHooks=new Harmony("local.heroesvow.framepacing.hotpaths");
            try{ModelHotPaths.Connect(a,hotHooks);}catch(Exception e){hotHooks.UnpatchSelf();mod.Log.LogWarning("Model hot paths retained native behavior: "+e.Message);}
            var bufferHooks=new Harmony("local.heroesvow.framepacing.particlebuffers");
            try{ParticleBufferPass.Connect(a,bufferHooks);}catch(Exception e){bufferHooks.UnpatchSelf();mod.Log.LogWarning("Particle buffer loop retained native behavior: "+e.Message);}
            var timingHooks=new Harmony("local.heroesvow.framepacing.stages");
            try{StageTimings.Connect(a,timingHooks);}catch(Exception e){timingHooks.UnpatchSelf();mod.Log.LogWarning("Optional stage timing unavailable: "+e.Message);}
            var hiddenClothHooks=new Harmony("local.heroesvow.framepacing.hiddencloth");
            try{HiddenNativeCloth.Connect(a,hiddenClothHooks);}catch(Exception e){hiddenClothHooks.UnpatchSelf();HiddenNativeCloth.ConnectionFailed(e);mod.Log.LogWarning("Native cloth preserved: "+e.Message);}
            var session=a.GetType("Laevat.Preview.PreviewSession");var target=a.GetType("Laevat.Preview.ModelTarget");
            if(!legacy && session!=null && target!=null){
                var lod=a.GetType("Laevat.Preview.CameraLodR92");var world=a.GetType("Laevat.Preview.WorldModels");var work=a.GetType("Laevat.Preview.WorldWork416");
                nextPose=AccessTools.Field(session,"NextOffscreenTick");poseInterval=AccessTools.Field(session,"LastLodInterval");lastMotion=AccessTools.Field(session,"LastMotionTick");nextScan=AccessTools.Field(world,"NextScan");
                if(nextPose?.FieldType!=typeof(long)||lastMotion?.FieldType!=typeof(long)||poseInterval?.FieldType!=typeof(int)||nextScan?.FieldType!=typeof(long))continue;
                var core=AccessTools.DeclaredMethod(session,"Core457_Tick",new[]{target});
                var interval=AccessTools.DeclaredMethod(lod,"Interval",new[]{typeof(float),typeof(bool),typeof(bool),typeof(bool)});
                var tick=AccessTools.DeclaredMethod(work,"Tick");
                if(core==null||interval==null||tick==null)continue;
                hooks.Patch(interval,postfix:new HarmonyMethod(typeof(FramePacing),nameof(Lod)));
                hooks.Patch(core,prefix:new HarmonyMethod(typeof(FramePacing),nameof(PoseBefore)),postfix:new HarmonyMethod(typeof(FramePacing),nameof(PoseAfter)));
                hooks.Patch(tick,prefix:new HarmonyMethod(typeof(FramePacing),nameof(ScanBefore)),postfix:new HarmonyMethod(typeof(FramePacing),nameof(ScanAfter)));
                legacy=true;mod.Log.LogInfo("Frame pacing connected to "+a.GetName().Name);
            }

        }catch(Exception e){mod.Log.LogWarning("Optional performance hook: "+e.Message);}
    }
    // Keep full-rate close-up / visible combat animation. Only a proven out-of-view
    // combat actor loses the old unconditional full-rate override.
    static void Lod(float __0,bool __1,bool __2,bool __3,ref int __result){
        if(enabled.Value && __3 && !__2 && float.IsFinite(__0) && __0<=0)__result=500;
        if(enabled.Value && distant.Value && crowded && !__1 && !__2 && !__3 && float.IsFinite(__0) && __0>0 && __0<80)
            __result=Math.Max(__result,__0<24?180:100);
    }
    internal static void FrameLoad(double mean){
        if(!Application.isFocused){slow=fast=0;return;}
        if(mean>33.3){fast=0;if(++slow>=3)crowded=true;}
        else if(mean<25){slow=0;if(++fast>=5)crowded=false;}
        else{slow=fast=0;}
    }
    internal static long Deadline(long now,int interval,int phase){
        long p=(long)(phase%997)*interval/997;
        long boundary=(now/interval)*interval+p;
        // No shorter-than-half interval on phase alignment; subsequent deadlines
        // stay on the same clock, so distant models no longer wake in one burst.
        while(boundary<now+Math.Max(1,interval/2))boundary+=interval;
        return boundary;
    }
    struct PoseState {internal long motion,start;}
    static void PoseBefore(object __instance,out PoseState __state){
        __state=new PoseState{motion=enabled.Value?(long)lastMotion.GetValue(__instance):0,start=measure.Value?Stopwatch.GetTimestamp():0};
    }
    static void PoseAfter(object __instance,PoseState __state){
        if(__state.start!=0){long elapsed=Stopwatch.GetTimestamp()-__state.start;modelTicks+=elapsed;modelCalls++;modelWorst=Math.Max(modelWorst,elapsed*1000d/Stopwatch.Frequency);}
        if(!enabled.Value || (long)lastMotion.GetValue(__instance)==__state.motion)return;
        int interval=(int)poseInterval.GetValue(__instance);if(interval<=0)return;
        var slot=phases.GetValue(__instance,_=>new Slot{phase=(++serial*619)%997});
        nextPose.SetValue(__instance,Deadline(Environment.TickCount64,interval,slot.phase));
    }
    static void ScanBefore(out long __state){__state=enabled.Value?(long)nextScan.GetValue(null):long.MaxValue;}
    static void ScanAfter(long __state){
        if(!enabled.Value)return;long now=Environment.TickCount64;
        // Lifecycle/refresh hooks still register new actors immediately. Change only
        // the fallback full-scene rediscovery that just ran, never a pending dirty scan.
        long next=(long)nextScan.GetValue(null);
        if(__state<=now && next>now && next<=now+3100)nextScan.SetValue(null,now+10000);
    }
    internal static void Summary(double[] frames,int count){
        if(!measure.Value){modelTicks=0;modelCalls=0;modelWorst=0;return;}
        if(count==0)return;
        Array.Sort(frames,0,count);double mean=0;for(int i=0;i<count;i++)mean+=frames[i];mean/=count;
        string line=$"{DateTime.Now:s} scene={(Fi_Controller.m_isInBattle?"martial":De_Controller.m_isInDebate?"debate":"world/menu")} focused={Application.isFocused} frames={count} meanMs={mean:F2} p95Ms={frames[(int)((count-1)*.95)]:F2} maxMs={frames[count-1]:F2} modelMsPerFrame={modelTicks*1000d/Stopwatch.Frequency/count:F2} modelCalls={modelCalls} worstModelMs={modelWorst:F2} legacy={legacy} enabled={enabled.Value} {PortraitWork.Summary()} {RuntimeSearch.Summary()} clothFixedPoint={ClothWork.Connected}";
        line+=" "+AnimatedFrames.Summary()+" "+RigClassification.Summary()+" "+DressWork.Summary()+" "+CombatCloth.Summary()+" "+ModelHotPaths.Summary()+" "+ParticleBufferPass.Summary()+" "+HiddenNativeCloth.Summary();
        line+=" "+CombatFlagCameras.Summary()+" "+IndoorCompanionLimit.Summary();
        string stages=$"{DateTime.Now:s} frames={count} "+StageTimings.Summary(count);
        string rendering=$"{DateTime.Now:s} frames={count} "+RenderTimings.Summary(count);
        modelTicks=0;modelCalls=0;modelWorst=0;
        Task.Run(()=>{try{
            void Append(string name,string value){string path=Path.Combine(Paths.BepInExRootPath,name);if(File.Exists(path)&&new FileInfo(path).Length>262144)File.Move(path,path+".previous",true);File.AppendAllText(path,value+Environment.NewLine);}
            Append("FramePacing-summary.log",line);Append("FramePacing-stages.log",stages);Append("FramePacing-render.log",rendering);
        }catch{}});
    }
}
public sealed class FrameSampler:MonoBehaviour
{
    public FrameSampler(IntPtr p):base(p){}
    readonly double[] frames=new double[32768];int count,loadCount;float next,loadNext;double loadTime;
    public void Update(){
        // One scene check per frame, never per cloth vertex or bone. The army
        // controller can survive scene exit, so require its active camera too.
        bool army=false;
        if(!Fi_Controller.m_isInBattle&&!De_Controller.m_isInDebate){
            var c=Wa_Controller.m_instance;var camera=Wa_Controller.m_cameraCtrl;
            army=c!=null&&c.gameObject.activeInHierarchy&&camera!=null&&camera.isActiveAndEnabled&&!Wa_Controller.m_isQuiting;
        }
        CombatCloth.Active=Fi_Controller.m_isInBattle||De_Controller.m_isInDebate||army;
        CombatFlagCameras.Tick(Fi_Controller.m_isInBattle||De_Controller.m_isInDebate,Environment.TickCount64);
        if(FramePacing.Measure){StageTimings.Start();RenderTimings.Start();}
        FramePacing.Connect();AnimatedFrames.Maintain(Environment.TickCount64);HiddenNativeCloth.Maintain(Environment.TickCount64);if(count<frames.Length)frames[count++]=Time.unscaledDeltaTime*1000d;
        loadTime+=Time.unscaledDeltaTime*1000d;loadCount++;
        if(Time.unscaledTime>=loadNext){loadNext=Time.unscaledTime+1;FramePacing.FrameLoad(loadTime/Math.Max(1,loadCount));loadCount=0;loadTime=0;}
        if(Time.unscaledTime<next)return;next=Time.unscaledTime+30;FramePacing.Summary(frames,count);count=0;
    }
    public void OnDestroy(){CombatFlagCameras.Release();}
}
