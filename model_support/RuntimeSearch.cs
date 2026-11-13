using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace Vow.Performance;

// Optional hooks in the installed model runtimes. Never alter their assets or
// replace their camera filtering, pose, cloth or animation implementation.
internal static class RuntimeSearch {
    static bool camera;
    internal static bool Complete=>camera;
    static int queries,reused;
    [ThreadStatic] static int depth;
    [ThreadStatic] static Il2CppArrayBase<RawImage> current;
    internal static void Connect(Assembly a,Harmony h){
        if(!camera){
            var t=a.GetType("Laevat.Preview.GameCameraR71",false);
            var candidate=t==null?null:AccessTools.DeclaredMethod(t,"Candidate",new[]{typeof(Camera)});
            var discover=t==null?null:AccessTools.DeclaredMethod(t,"Discover",Type.EmptyTypes);
            if(candidate!=null&&candidate.IsStatic&&candidate.ReturnType==typeof(bool)&&discover!=null&&discover.IsStatic&&discover.ReturnType==typeof(void)){
                // Transpiler rejects unexpected versions before changing any instructions.
                // Outside Discover, replacement is exactly the original query.
                h.Patch(candidate,transpiler:new HarmonyMethod(typeof(RuntimeSearch),nameof(ShareImages)));
                h.Patch(discover,prefix:new HarmonyMethod(typeof(RuntimeSearch),nameof(Begin)),finalizer:new HarmonyMethod(typeof(RuntimeSearch),nameof(End)));
                camera=true;FramePacing.mod.Log.LogInfo("Camera discovery shares one RawImage query per discovery pass.");
            }
        }
    }
    internal struct Scope {internal int depth;internal Il2CppArrayBase<RawImage> images;}
    internal static void Begin(out Scope __state){
        __state=new Scope{depth=depth,images=current};depth++;current=null;
    }
    internal static Exception End(Exception __exception,Scope __state){
        current=__state.images;depth=__state.depth;return __exception;
    }
    internal static Il2CppArrayBase<RawImage> Images(){
        if(!FramePacing.enabled.Value||depth==0){queries++;return UnityEngine.Object.FindObjectsOfType<RawImage>();}
        if(current!=null){reused++;return current;}
        queries++;return current=UnityEngine.Object.FindObjectsOfType<RawImage>();
    }
    internal static IEnumerable<CodeInstruction> ShareImages(IEnumerable<CodeInstruction> source){
        var code=source.ToList();
        var calls=code.Where(i=>i.opcode==OpCodes.Call&&i.operand is MethodInfo m&&
            m.DeclaringType==typeof(UnityEngine.Object)&&m.Name=="FindObjectsOfType"&&
            m.IsGenericMethod&&m.GetGenericArguments().SequenceEqual(new[]{typeof(RawImage)})&&
            m.GetParameters().Length==0&&m.ReturnType==typeof(Il2CppArrayBase<RawImage>)).ToArray();
        if(calls.Length!=1)throw new InvalidOperationException("Camera RawImage query contract changed; native path retained.");
        calls[0].operand=AccessTools.Method(typeof(RuntimeSearch),nameof(Images));
        return code;
    }
    internal static string Summary(){
        string s=$"cameraQueryCache={camera} cameraQueries={queries} reusedCameraQueries={reused}";
        queries=reused=0;return s;
    }
}
