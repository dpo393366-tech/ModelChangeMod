using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace Vow.Performance;

internal static class PortraitWork {
    static bool connected,battleConnected,restored;
    internal static bool Complete=>connected&&battleConnected;
    static FieldInfo states;
    static MethodInfo restore;
    internal static void Connect(Assembly a,Harmony h){
        if(!connected){
            var t=a.GetType("Laevat.Preview.EventPortraitR37",false);
            if(t!=null){
                var tick=AccessTools.DeclaredMethod(t,"Core457_Tick");
                var f=AccessTools.Field(t,"States");
                var r=AccessTools.DeclaredMethod(t,"Restore");
                if(tick!=null&&f!=null&&r!=null&&typeof(IDictionary).IsAssignableFrom(f.FieldType)){
                    states=f;restore=r;
                    h.Patch(tick,prefix:new HarmonyMethod(typeof(PortraitWork),nameof(Before)));
                    connected=true;
                    FramePacing.mod.Log.LogInfo("Portrait automatic shrinking/repositioning removed; original UI layout retained.");
                }
            }
        }
        if(!battleConnected){
            var t=a.GetType("Laevat.Preview.BattlePortraitR33",false);
            var tick=t==null?null:AccessTools.DeclaredMethod(t,"Core457_Tick");
            var fit=t==null?null:AccessTools.DeclaredMethod(t,"FitClip");
            if(tick!=null&&fit!=null){
                h.Patch(tick,transpiler:new HarmonyMethod(typeof(PortraitWork),nameof(BattleLayout)));
                h.Patch(fit,prefix:new HarmonyMethod(typeof(PortraitWork),nameof(SkipFit)));
                battleConnected=true;
                FramePacing.mod.Log.LogInfo("Battle portrait shrinking/repositioning removed; portrait artwork selection retained.");
            }
        }
    }
    static bool Before(){
        // User disabled fitting independently of the performance switch. Restore
        // only adjustments still owned by the fitter, preserving newer UI edits.
        if(!restored){
            try{
                var saved=(IDictionary)states.GetValue(null);
                foreach(var s in saved.Values)restore.Invoke(null,new[]{s});
                saved.Clear();restored=true;
            }catch(Exception e){
                restored=true;
                FramePacing.mod.Log.LogWarning("Portrait previous-layout restore failed: "+e.Message);
            }
        }
        return false;
    }
    static bool SkipFit()=>false;
    static void KeepLayout(Transform transform,Vector3 value){}
    static IEnumerable<CodeInstruction> BattleLayout(IEnumerable<CodeInstruction> source){
        var code=source.ToList();int scale=0,position=0;
        foreach(var i in code){
            if(i.opcode!=OpCodes.Call||i.operand is not MethodInfo m||m.DeclaringType?.FullName!="Laevat.Preview.NativeTransforms435")continue;
            if(m.Name!="SetLocalScale"&&m.Name!="SetPosition")continue;
            var p=m.GetParameters();
            if(m.ReturnType!=typeof(void)||p.Length!=2||p[0].ParameterType!=typeof(Transform)||p[1].ParameterType!=typeof(Vector3))
                throw new InvalidOperationException("Portrait layout setter contract changed.");
            if(m.Name=="SetLocalScale")scale++;else position++;
            i.operand=AccessTools.Method(typeof(PortraitWork),nameof(KeepLayout));
        }
        if(scale!=1||position!=1)throw new InvalidOperationException("Battle portrait layout contract changed.");
        return code;
    }
    internal static string Summary()=>$"portraitAutoFitRemoved={connected} battlePortraitAutoFitRemoved={battleConnected}";
}
