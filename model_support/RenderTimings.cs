using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Il2CppInterop.Runtime;

namespace Vow.Performance;

// Read-only CPU timing around existing camera callbacks. Does not render a
// camera, alter culling masks, lower quality, or allocate on ordinary frames.
internal static class RenderTimings {
    sealed class Entry {internal string label;internal long begin,ticks,calls,peak;}
    static readonly Dictionary<int,Entry> cameras=new();
    static Camera.CameraCallback before,after;
    static bool started;
    static long previousAlloc=GC.GetTotalAllocatedBytes(false),previousTick=Stopwatch.GetTimestamp();
    static int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);
    internal static void Start(){
        if(started)return;started=true;
        try{
            before=DelegateSupport.ConvertDelegate<Camera.CameraCallback>((Action<Camera>)Before);
            after=DelegateSupport.ConvertDelegate<Camera.CameraCallback>((Action<Camera>)After);
            Camera.onPreCull+=before;Camera.onPostRender+=after;
        }catch(Exception e){FramePacing.mod.Log.LogWarning("Camera timing unavailable: "+e.Message);}
    }
    static void Before(Camera c){
        if(!FramePacing.Measure)return;
        try{
            if(c==null)return;int id=c.GetInstanceID();
            if(!cameras.TryGetValue(id,out var e)){
                if(cameras.Count>=32)return;
                var target=c.targetTexture;
                e=new Entry{label=CameraPath(c)+"#"+id+"["+(target!=null?"texture "+target.width+"x"+target.height:"screen")+";mask="+c.cullingMask.ToString("X8")+"]"};cameras.Add(id,e);
            }
            e.begin=Stopwatch.GetTimestamp();
        }catch{}
    }
    static string CameraPath(Camera c){
        // Once per new camera, not a hierarchy walk on ordinary frames.
        var t=c.transform;string path=t.name;
        for(int i=0;i<6;i++){t=t.parent;if(t==null)break;path=t.name+"/"+path;}
        return path;
    }
    static void After(Camera c){
        if(!FramePacing.Measure)return;
        try{
            if(c==null||!cameras.TryGetValue(c.GetInstanceID(),out var e)||e.begin==0)return;
            long t=Stopwatch.GetTimestamp()-e.begin;e.begin=0;e.ticks+=t;e.calls++;e.peak=Math.Max(e.peak,t);
        }catch{}
    }
    internal static string Summary(int frames){
        var ordered=cameras.Values.OrderByDescending(x=>x.ticks).ToArray();
        string rows=string.Join(" | ",ordered.Select(e=>$"{e.label}={e.ticks*1000d/Stopwatch.Frequency/Math.Max(1,frames):F3}ms/frame,{e.calls}renders,peak={e.peak*1000d/Stopwatch.Frequency:F2}"));
        // Release retired camera IDs so repeated battle/scene loads cannot fill
        // the bounded inventory and hide later cameras from diagnostics.
        foreach(var id in cameras.Where(kv=>kv.Value.calls==0).Select(kv=>kv.Key).ToArray())cameras.Remove(id);
        foreach(var e in ordered)e.ticks=e.calls=e.peak=0;
        long now=Stopwatch.GetTimestamp(),alloc=GC.GetTotalAllocatedBytes(false);double seconds=(now-previousTick)/(double)Stopwatch.Frequency;
        var info=GC.GetGCMemoryInfo();double pause=0;foreach(var p in info.PauseDurations)pause+=p.TotalMilliseconds;
        int a=GC.CollectionCount(0),b=GC.CollectionCount(1),c=GC.CollectionCount(2);
        string memory=$"managedAllocMiBps={(alloc-previousAlloc)/1048576d/Math.Max(.001,seconds):F2} managedHeapMiB={GC.GetTotalMemory(false)/1048576d:F1} gc0={a-gc0} gc1={b-gc1} gc2={c-gc2} latestGCPauseMs={pause:F3}";
        previousAlloc=alloc;previousTick=now;gc0=a;gc1=b;gc2=c;
        return "cameraCpuInclusive: "+rows+"; "+memory;
    }
}
