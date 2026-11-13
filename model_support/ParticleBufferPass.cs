using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace Vow.Performance;

// The interop array indexer reconstructs its Span (native pointer + native array
// length query) on EVERY read and write. Acquire the public Span once AFTER
// ReadLive185 has grown/refilled the buffer, then preserve the exact native loop.
// No retained pointers, copied particles, altered colors or reduced update rate.
internal static class ParticleBufferPass {
    static bool connected;
    static long buffers,accesses;
    internal static void Connect(Assembly a,Harmony h){
        if(connected)return;
        var t=a.GetType("Laevat.Preview.PreviewSession",false);if(t==null)return;
        foreach(var name in new[]{"ApplyLiveColors185","RestoreLiveColors185"}){
            var method=AccessTools.DeclaredMethod(t,name);
            if(method==null)throw new InvalidOperationException("Particle loop missing: "+name);
            h.Patch(method,transpiler:new HarmonyMethod(typeof(ParticleBufferPass),nameof(Rewrite)));
        }
        connected=true;
    }
    static Span<ParticleSystem.Particle> View(Il2CppStructArray<ParticleSystem.Particle> b){buffers++;return b.AsSpan();}
    static ParticleSystem.Particle Get(Il2CppArrayBase<ParticleSystem.Particle> b,int i,ref Span<ParticleSystem.Particle> view){
        accesses++;return FramePacing.enabled.Value?view[i]:b[i];
    }
    static void Set(Il2CppArrayBase<ParticleSystem.Particle> b,int i,ParticleSystem.Particle value,ref Span<ParticleSystem.Particle> view){
        accesses++;if(FramePacing.enabled.Value)view[i]=value;else b[i]=value;
    }
    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source,ILGenerator generator){
        var code=source.ToList();
        var read=code.Where(x=>x.operand is MethodInfo m&&m.Name=="ReadLive185").ToArray();
        if(read.Length!=1||read[0].operand is not MethodInfo readMethod||!readMethod.IsStatic||readMethod.ReturnType!=typeof(int)||readMethod.GetParameters().Length!=1)
            throw new InvalidOperationException("ReadLive185 buffer lifetime changed.");
        var stateType=readMethod.GetParameters()[0].ParameterType;
        var buffer=AccessTools.Field(stateType,"Buffer");
        if(buffer?.FieldType!=typeof(Il2CppStructArray<ParticleSystem.Particle>))throw new InvalidOperationException("Particle buffer type changed.");
        var state=generator.DeclareLocal(stateType);
        var view=generator.DeclareLocal(typeof(Span<ParticleSystem.Particle>));
        var result=new List<CodeInstruction>();int gets=0,sets=0;
        foreach(var i in code){
            if(i==read[0]){
                var dup=new CodeInstruction(OpCodes.Dup);dup.labels.AddRange(i.labels);i.labels.Clear();dup.blocks.AddRange(i.blocks);i.blocks.Clear();
                result.Add(dup);result.Add(new(OpCodes.Stloc,state));result.Add(i);
                // Preserve returned count on the stack while acquiring the resized buffer.
                result.Add(new(OpCodes.Ldloc,state));result.Add(new(OpCodes.Ldfld,buffer));
                result.Add(new(OpCodes.Call,AccessTools.Method(typeof(ParticleBufferPass),nameof(View))));result.Add(new(OpCodes.Stloc,view));
            }else if(i.operand is MethodInfo m&&m.DeclaringType==typeof(Il2CppArrayBase<ParticleSystem.Particle>)&&(m.Name=="get_Item"||m.Name=="set_Item")){
                var address=new CodeInstruction(OpCodes.Ldloca,view);address.labels.AddRange(i.labels);i.labels.Clear();address.blocks.AddRange(i.blocks);i.blocks.Clear();result.Add(address);
                bool get=m.Name=="get_Item";if(get)gets++;else sets++;
                i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(ParticleBufferPass),get?nameof(Get):nameof(Set));result.Add(i);
            }else result.Add(i);
        }
        if(gets!=1||sets!=1)throw new InvalidOperationException($"Particle buffer access contract changed: {gets}/{sets}");
        return result;
    }
    internal static string Summary(){var s=$"particleSpan={connected} particleBufferPasses={buffers} particleSpanAccesses={accesses}";buffers=accesses=0;return s;}
}
