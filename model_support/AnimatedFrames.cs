using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace Vow.Performance;

// Cache decoded pixels, not a replacement Texture: overlays keep the original
// texture identity, alpha, dimensions, clock, frame selection and cleanup path.
internal static class AnimatedFrames {
    internal static bool Connected;
    sealed class Entry {
        internal byte[] pixels;
        internal int width,height; internal TextureFormat format;
        internal long bytes,used;
    }
    static readonly Dictionary<byte[],Entry> frames=new();
    static readonly List<byte[]> expired=new();
    static long bytes,nextSweep,hits,misses,failures,hitTicks,missTicks;
    static MethodInfo conversion,load;
    internal static long Budget=256L*1024*1024;
    internal static long Used=>bytes;
    internal static int Count=>frames.Count;
    internal static void Connect(Assembly a,Harmony h){
        if(Connected)return;
        var t=a.GetType("Laevat.Preview.AnimatedCg+FrameStream",false);
        if(t==null)return;
        var step=AccessTools.DeclaredMethod(t,"Step",new[]{typeof(long)});
        conversion=typeof(Il2CppStructArray<byte>).GetMethods(BindingFlags.Static|BindingFlags.Public).Single(m=>m.Name=="op_Implicit"&&m.ReturnType==typeof(Il2CppStructArray<byte>)&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==typeof(byte[]));
        load=AccessTools.DeclaredMethod(typeof(ImageConversion),"LoadImage",new[]{typeof(Texture2D),typeof(Il2CppStructArray<byte>),typeof(bool)});
        if(step==null||load==null)throw new InvalidOperationException("Animated frame contract changed; original decode retained.");
        h.Patch(step,transpiler:new HarmonyMethod(typeof(AnimatedFrames),nameof(Replace)));
        Connected=true;FramePacing.mod.Log.LogInfo("Animated portrait decoded-frame cache connected (256 MiB cap; original animation cadence).");
    }
    internal static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> source){
        var code=source.Select(i=>new CodeInstruction(i)).ToList();
        var at=code.Select((i,n)=>(i,n)).Where(x=>x.i.Calls(load)).Select(x=>x.n).ToArray();
        if(at.Length!=1)throw new InvalidOperationException("Expected one animated frame decode; original method retained.");
        int n=at[0];
        // Require the exact byte[] -> IL2CPP array -> false -> LoadImage contract.
        if(n<2||!code[n-2].Calls(conversion)||code[n-1].opcode!=OpCodes.Ldc_I4_0)
            throw new InvalidOperationException("Animated frame argument layout changed; original method retained.");
        code[n-2].opcode=OpCodes.Nop;code[n-2].operand=null;
        code[n].operand=AccessTools.Method(typeof(AnimatedFrames),nameof(Decode));
        return code;
    }
    internal static void Maintain(long now){
        if(!FramePacing.enabled.Value){Clear();return;}
        if(now<nextSweep)return;nextSweep=now+1000;
        expired.Clear();foreach(var p in frames)if(now-p.Value.used>=60000)expired.Add(p.Key);
        foreach(var key in expired)Remove(key);expired.Clear();
    }
    static void Remove(byte[] key){if(frames.Remove(key,out var e))bytes-=e.bytes;}
    internal static void Clear(){frames.Clear();expired.Clear();bytes=0;nextSweep=0;}
    internal static unsafe bool Decode(Texture2D texture,byte[] data,bool unreadable){
        if(!FramePacing.enabled.Value||unreadable||data==null)return ImageConversion.LoadImage(texture,data,unreadable);
        long now=Environment.TickCount64,start=Stopwatch.GetTimestamp();
        if(frames.TryGetValue(data,out var e)){
            try {
                if(texture.width!=e.width||texture.height!=e.height||texture.format!=e.format){
                    if(!texture.Resize(e.width,e.height,e.format,false))throw new InvalidOperationException("Texture resize failed");
                }
                fixed(byte* p=e.pixels)texture.LoadRawTextureData((IntPtr)p,e.pixels.Length);
                texture.Apply(false,false);
                e.used=now;hits++;hitTicks+=Stopwatch.GetTimestamp()-start;return true;
            } catch {failures++;Remove(data);} // Fall back to the exact original decoder, never lose a frame.
        }
        bool ok=ImageConversion.LoadImage(texture,data,false);
        misses++;missTicks+=Stopwatch.GetTimestamp()-start;
        if(!ok)return false;
        try {
            int w=texture.width,h=texture.height;var f=texture.format;
            int bpp=f==TextureFormat.RGB24?3:4;
            long rawSize=checked((long)w*h*bpp);
            long cost=checked(rawSize+data.LongLength+128);
            // Stable admission avoids total cache thrashing when a looping movie
            // exceeds the budget. Unsupported formats retain their native path.
            if(w>0&&h>0&&texture.mipmapCount==1&&(f==TextureFormat.RGB24||f==TextureFormat.RGBA32||f==TextureFormat.ARGB32)&&cost<=Budget-bytes){
                // Use the verified non-generic native API: some IL2CPP games do
                // not have an AOT instantiation of NativeArray<byte>.ToArray.
                // Copy now; the next JPEG decode will overwrite this buffer.
                var pointer=texture.GetWritableImageData(0);
                if(pointer!=IntPtr.Zero&&texture.GetRawImageDataSize()==(ulong)rawSize&&rawSize<=int.MaxValue){
                    var pixels=new byte[(int)rawSize];Marshal.Copy(pointer,pixels,0,pixels.Length);
                    frames.Add(data,new Entry{pixels=pixels,width=w,height=h,format=f,bytes=cost,used=now});bytes+=cost;
                }
            }
        } catch {failures++;} // A cache allocation failure must not stop the portrait.
        return true;
    }
    internal static string Summary(){
        string s=$"cgCache={Connected} cgFrames={frames.Count} cgMiB={bytes/1048576d:F1} cgHits={hits} cgDecodes={misses} cgHitMs={hitTicks*1000d/Stopwatch.Frequency/Math.Max(1,hits):F3} cgDecodeMs={missTicks*1000d/Stopwatch.Frequency/Math.Max(1,misses):F3} cgFallbacks={failures}";
        hits=misses=failures=hitTicks=missTicks=0;return s;
    }
}
