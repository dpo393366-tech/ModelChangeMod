using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Vow.Performance;

// This fixes the model-import loader itself and must work with no RX22 plugin.
internal static class LegacyBundles {
    internal static bool Connected;
    static int messages;
    internal static void Connect(Assembly a,Harmony h){
        if(Connected)return;
        var t=a.GetType("Laevat.Preview.FamilyBundle287",false);if(t==null)return;
        var m=AccessTools.DeclaredMethod(t,"Load",new[]{typeof(string)});
        if(m==null||!m.IsStatic||m.ReturnType!=typeof(AssetBundle))throw new InvalidOperationException("Model shader bundle loader contract changed.");
        h.Patch(m,prefix:new HarmonyMethod(typeof(LegacyBundles),nameof(Reuse)));
        Connected=true;
        FramePacing.mod.Log.LogInfo("Model shader bundle reuse connected independently of character-import plugins.");
    }
    static bool Reuse(string __0,ref AssetBundle __result){
        if(!LegacyShaderBundleCache.TryGet(__0,Path.Combine(BepInEx.Paths.BepInExRootPath,"LaevatPreview/shaders"),out var bundle))return true;
        __result=bundle;
        if(messages++<8)FramePacing.mod.Log.LogInfo("Model shader bundle reused: "+bundle.name);
        return false;
    }
    internal static string Summary()=>$"modelShaderReuse={Connected}";
}
