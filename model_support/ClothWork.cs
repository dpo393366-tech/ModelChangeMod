using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Vow.Performance;
internal static class ClothWork {
    internal static bool Connected;
    delegate Vector3 Projection(Vector3 p,Vector3 f,float[] radii,Vector3[] a,Vector3[] b,float scale,Vector3 plane,Vector3 normal,float clearance);
    static Projection original;
    static MethodInfo target;
    internal static void Connect(Assembly a,Harmony h){
        if(Connected)return;
        var t=a.GetType("Laevat.Preview.Cloth387",false);var session=a.GetType("Laevat.Preview.PreviewSession",false);
        if(t==null||session==null)return;
        var signature=new[]{typeof(Vector3),typeof(Vector3),typeof(float[]),typeof(Vector3[]),typeof(Vector3[]),typeof(float),typeof(Vector3),typeof(Vector3),typeof(float)};
        target=AccessTools.DeclaredMethod(t,"Project",signature);
        var update=AccessTools.DeclaredMethod(t,"Update",new[]{session});
        if(target==null||!target.IsStatic||target.ReturnType!=typeof(Vector3)||update==null||!update.IsStatic)throw new InvalidOperationException("Cloth projection contract changed; native implementation retained.");
        original=target.CreateDelegate<Projection>();
        h.Patch(update,transpiler:new HarmonyMethod(typeof(ClothWork),nameof(Replace)));
        Connected=true;FramePacing.mod.Log.LogInfo("Cloth fixed-point projection optimization connected; full bone/vertex set and motion cadence retained.");
    }
    internal static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> source){
        var code=source.ToList();var calls=code.Where(i=>i.opcode==OpCodes.Call&&Equals(i.operand,target)).ToArray();
        if(calls.Length!=1)throw new InvalidOperationException("Cloth Update projection call changed; native implementation retained.");
        calls[0].operand=AccessTools.Method(typeof(ClothWork),nameof(Project));return code;
    }
    internal static Vector3 Project(Vector3 p,Vector3 f,float[] radii,Vector3[] a,Vector3[] b,float scale,Vector3 plane,Vector3 normal,float clearance){
        if(!FramePacing.enabled.Value)return original(p,f,radii,a,b,scale,plane,normal,clearance);
        return ClothProjection.Project(p,f,radii,a,b,scale,plane,normal,clearance);
    }
}
