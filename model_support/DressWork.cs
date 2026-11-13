using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Vow.Performance;
internal static class DressWork {
    internal static bool Connected;
    sealed class State {
        internal readonly DressContact contact=new();
        internal Vector3[] a,b;internal float size;internal bool active,prepared,valid;
    }
    static readonly ConditionalWeakTable<object,State> states=new();
    static Func<object,Vector3[][]> surface;
    static Func<object,Vector3[]> directions;
    static MethodInfo push;static DynamicMethod bridge;
    static long calls,skipped;
    internal static string Summary()=>$"dressBroadPhase={Connected} dressQueries={calls} dressCapsulesSkipped={skipped}";
    internal static void Connect(Assembly a,Harmony h){
        if(Connected)return;var t=a.GetType("Laevat.Preview.LinDressR40",false);if(t==null)return;
        var contact=AccessTools.DeclaredMethod(t,"Contact",new[]{typeof(Vector3),typeof(Quaternion),typeof(Vector3),typeof(Vector3[]),typeof(Vector3[]),typeof(float),typeof(float),typeof(bool),typeof(float)});
        push=t.GetMethods(BindingFlags.NonPublic|BindingFlags.Instance).SingleOrDefault(m=>m.Name.Contains("g__Push|")&&m.ReturnType==typeof(float));
        var args=push?.GetParameters();
        if(contact==null||args==null||args.Length!=3||args[0].ParameterType!=typeof(int)||args[1].ParameterType!=typeof(Vector3)||!args[2].ParameterType.IsByRef)throw new InvalidOperationException("Dress contact contract changed; original retained.");
        surface=Getter<Vector3[][]>(t,"Surface");directions=Getter<Vector3[]>(t,"RunOutR39");
        bridge=new DynamicMethod("BoundedDressContact",typeof(float),new[]{t,typeof(int),typeof(Vector3),args[2].ParameterType},typeof(DressWork).Module,true);
        var il=bridge.GetILGenerator();var value=il.DeclareLocal(typeof(float));var original=il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldarg_2);il.Emit(OpCodes.Ldloca,value);
        il.Emit(OpCodes.Call,AccessTools.Method(typeof(DressWork),nameof(TryPush)));il.Emit(OpCodes.Brfalse,original);il.Emit(OpCodes.Ldloc,value);il.Emit(OpCodes.Ret);
        il.MarkLabel(original);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldarg_2);il.Emit(OpCodes.Ldarg_3);il.Emit(OpCodes.Call,push);il.Emit(OpCodes.Ret);
        h.Patch(contact,prefix:new HarmonyMethod(typeof(DressWork),nameof(Begin)),transpiler:new HarmonyMethod(typeof(DressWork),nameof(Replace)),finalizer:new HarmonyMethod(typeof(DressWork),nameof(End)));
        Connected=true;FramePacing.mod.Log.LogInfo("Dress contact broad phase connected; every surface point, solver pass and visible animation update retained.");
    }
    static Func<object,T> Getter<T>(Type t,string name){
        var f=AccessTools.Field(t,name);if(f?.FieldType!=typeof(T))throw new InvalidOperationException("Dress field changed: "+name);
        var p=Expression.Parameter(typeof(object));return Expression.Lambda<Func<object,T>>(Expression.Field(Expression.Convert(p,t),f),p).Compile();
    }
    static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> input){
        var code=input.ToList();int count=0;foreach(var c in code)if(c.opcode==OpCodes.Call&&Equals(c.operand,push)){c.operand=bridge;count++;}
        if(count!=4)throw new InvalidOperationException("Dress contact call sites changed; original retained: "+count);return code;
    }
    static void Begin(object __instance,Vector3[] __3,Vector3[] __4,float __5){
        var s=states.GetOrCreateValue(__instance);s.active=FramePacing.enabled.Value;s.prepared=s.valid=false;s.a=__3;s.b=__4;s.size=__5;
    }
    static void End(object __instance){if(states.TryGetValue(__instance,out var s))s.active=false;}
    static bool TryPush(object owner,int i,Vector3 p,out float value){
        value=0;if(!states.TryGetValue(owner,out var s)||!s.active)return false;
        if(!s.prepared){s.prepared=true;s.valid=s.contact.Prepare(surface(owner),directions(owner),s.a,s.b,s.size);}
        if(!s.valid)return false;
        long before=s.contact.SkippedCapsules;bool ok=s.contact.TryPush(i,p,out value);calls++;skipped+=s.contact.SkippedCapsules-before;return ok;
    }
}
