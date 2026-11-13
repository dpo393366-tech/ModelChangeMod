using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine.Profiling;

namespace Vow.Performance;

// Bounded inclusive timings. No per-frame allocations, file I/O or object scans.
// Nested values must NOT be summed: they intentionally overlap.
internal static class StageTimings {
    sealed class Bucket {internal string name;internal long ticks,calls,peak;}
    struct Stamp {internal Bucket bucket;internal long start;}
    static readonly Dictionary<MethodBase,Bucket> methods=new();
    static readonly List<(string name,Recorder value)> native=new();
    static readonly List<string> unavailable=new();
    static bool connected,started;
    internal static void Start(){
        if(started)return;started=true;
        foreach(var name in new[]{"PlayerLoop","Camera.Render","RenderLoop.Draw","UpdateAllSkinnedMeshes","Animation.Update","ParticleSystem.Update","Gfx.WaitForPresentOnGfxThread"})try{
            var r=Recorder.Get(name);if(r!=null&&r.isValid){r.enabled=true;native.Add((name,r));}else unavailable.Add(name);
        }catch{unavailable.Add(name);}
    }
    internal static void Connect(Assembly a,Harmony h){
        if(connected)return;
        var session=a.GetType("Laevat.Preview.PreviewSession",false);if(session==null)return;
        foreach(var name in new[]{"ResetPose","UpdateSecondary","CorrectFootContacts","RestoreAura165","ApplyAura165","RefreshAuraTypes188","ApplyLiveColors185","RestoreLiveColors185","UpdateBodyShape","CheckSurfaceBinding","MatchRenderState"})Attach(AccessTools.DeclaredMethod(session,name),h);
        foreach(var spec in new[]{("PreviewDriver","Core457_Update"),("PreviewDriver","Core457_LateUpdate"),("WorldWork416","Tick"),("AnimatedCg","Core457_Tick"),("GameCameraRenderR71","OnPreCull"),("GameCameraRenderR71","OnPreRender"),("GameCameraRenderR71","OnPostRender")}){
            var t=a.GetType("Laevat.Preview."+spec.Item1,false);if(t!=null)Attach(AccessTools.DeclaredMethod(t,spec.Item2),h);
        }
        Attach(AccessTools.DeclaredMethod(typeof(MagicaCloth2.ClothManager),"ClothUpdate"),h);
        connected=true;
    }
    static void Attach(MethodInfo m,Harmony h){
        if(m==null||methods.ContainsKey(m)||methods.Count>=32)return;
        methods.Add(m,new(){name=m.DeclaringType.Name+"."+m.Name});
        h.Patch(m,prefix:new HarmonyMethod(typeof(StageTimings),nameof(Before)),finalizer:new HarmonyMethod(typeof(StageTimings),nameof(After)));
    }
    static void Before(MethodBase __originalMethod,out Stamp __state){
        __state=default;if(!FramePacing.Measure)return;
        if(methods.TryGetValue(__originalMethod,out var b))__state=new(){bucket=b,start=Stopwatch.GetTimestamp()};
    }
    static void After(Stamp __state){
        if(__state.start==0)return;long elapsed=Stopwatch.GetTimestamp()-__state.start;
        var b=__state.bucket;b.calls++;b.ticks+=elapsed;b.peak=Math.Max(b.peak,elapsed);
    }
    internal static string Summary(int frames){
        var rows=methods.Values.OrderByDescending(x=>x.ticks).Select(x=>$"{x.name}={x.ticks*1000d/Stopwatch.Frequency/Math.Max(1,frames):F3}ms/frame({x.calls}calls;peak={x.peak*1000d/Stopwatch.Frequency:F2})").ToArray();
        foreach(var b in methods.Values)b.ticks=b.calls=b.peak=0;
        var samples=new List<string>();
        foreach(var n in native)try{samples.Add(n.name+"="+(n.value.elapsedNanoseconds/1e6).ToString("F3")+"ms");}catch{}
        return "inclusiveStages: "+string.Join(" | ",rows)+"; nativeLastFrameOnly: "+string.Join(",",samples)+"; nativeUnavailable: "+string.Join(",",unavailable);
    }
}
