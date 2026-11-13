using System;
using System.Reflection;
using HarmonyLib;
namespace Vow.Performance;

// The captured hot path repeatedly lowercases the same immutable bone names.
// Keep every original classification, including null; do not skip bone motion.
internal static class RigClassification {
    internal static bool Connected;
    static string[] basic,alternate;
    static long hits;
    internal static void Connect(Assembly assembly,Harmony harmony){
        if(Connected)return;
        var type=assembly.GetType("Laevat.Preview.CarlottaRigR131",false);if(type==null)return;
        var method=AccessTools.DeclaredMethod(type,"Group",new[]{typeof(bool),typeof(int)});
        var a=AccessTools.Field(type,"BaseNames");var b=AccessTools.Field(type,"AdultNames");
        if(method==null||!method.IsStatic||method.ReturnType!=typeof(string)||a?.FieldType!=typeof(string[])||b?.FieldType!=typeof(string[])||!a.IsInitOnly||!b.IsInitOnly)
            throw new InvalidOperationException("Bone classification contract changed; original method retained.");
        var namesA=(string[])a.GetValue(null);var namesB=(string[])b.GetValue(null);
        if(namesA==null||namesB==null||namesA.Length>4096||namesB.Length>4096)throw new InvalidOperationException("Unexpected classification table size.");
        var original=method.CreateDelegate<Func<bool,int,string>>();
        var x=new string[namesA.Length];var y=new string[namesB.Length];
        for(int i=0;i<x.Length;i++)x[i]=original(false,i);
        for(int i=0;i<y.Length;i++)y[i]=original(true,i);
        basic=x;alternate=y;
        harmony.Patch(method,prefix:new HarmonyMethod(typeof(RigClassification),nameof(Before)));
        Connected=true;
        FramePacing.mod.Log.LogInfo("Immutable rig classification cache connected; native bone/cloth cadence retained.");
    }
    static bool Before(bool __0,int __1,ref string __result){
        if(!FramePacing.enabled.Value)return true;
        var table=__0?alternate:basic;
        // Preserve original out-of-range behavior; never turn a malformed ID into null.
        if(table==null||(uint)__1>=(uint)table.Length)return true;
        __result=table[__1];hits++;return false;
    }
    internal static string Summary(){var value=hits;hits=0;return $"rigClassCache={Connected} rigClassHits={value}";}
}
