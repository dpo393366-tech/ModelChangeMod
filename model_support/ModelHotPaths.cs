using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Vow.Performance;

internal static class ModelHotPaths {
    delegate void SetPose(IntPtr transform,ref Vector3 position,ref Quaternion rotation);
    static SetPose localPose;
    static Func<Transform,IntPtr> validPointer;
    static bool connected,poseChecked,poseUsable,particleConnected;
    static long poseCalls,particleCalls,scanDeferrals;
    static FieldInfo nextScan,scanHooks;
    internal static void Connect(Assembly a,Harmony h){
        if(connected)return;
        var session=a.GetType("Laevat.Preview.PreviewSession",false);
        var transforms=a.GetType("Laevat.Preview.NativeTransforms435",false);
        if(session==null||transforms==null)return;
        connected=true;
        var reset=AccessTools.DeclaredMethod(session,"ResetPose",Type.EmptyTypes);
        var pointer=AccessTools.DeclaredMethod(transforms,"P",new[]{typeof(Transform)});
        var api=AccessTools.Method(typeof(Transform),"SetLocalPositionAndRotation",new[]{typeof(Vector3),typeof(Quaternion)});
        // The runtime's P gate already checks engine version, live native pointer,
        // main thread and its comparison-disable switch. Never cache bone pointers.
        if(reset!=null&&pointer?.ReturnType==typeof(IntPtr)&&api!=null&&Harmony.GetPatchInfo(api)==null){
            IntPtr address=IL2CPP.il2cpp_resolve_icall("UnityEngine.Transform::SetLocalPositionAndRotation_Injected");
            if(address!=IntPtr.Zero){
                validPointer=pointer.CreateDelegate<Func<Transform,IntPtr>>();
                localPose=Marshal.GetDelegateForFunctionPointer<SetPose>(address);
                h.Patch(reset,transpiler:new HarmonyMethod(typeof(ModelHotPaths),nameof(PoseRewrite)));
            }
        }
        // Validate the field/rounding shortcuts against this engine's native API
        // once. They are used only in the model's two recoloring loops.
        if(CheckParticleContract()){
            foreach(string name in new[]{"ApplyLiveColors185","RestoreLiveColors185"}){
                var m=AccessTools.DeclaredMethod(session,name);
                if(m==null)throw new InvalidOperationException("Particle recoloring entry changed.");
                h.Patch(m,transpiler:new HarmonyMethod(typeof(ModelHotPaths),nameof(ParticleRewrite)));
            }
            particleConnected=true;
        }
        var cg=a.GetType("Laevat.Preview.AnimatedCg",false);
        nextScan=cg==null?null:AccessTools.Field(cg,"NextScan");scanHooks=cg==null?null:AccessTools.Field(cg,"Hooks237");
        var tick=cg==null?null:AccessTools.DeclaredMethod(cg,"Core457_Tick",Type.EmptyTypes);
        if(nextScan?.FieldType==typeof(long)&&scanHooks?.FieldType==typeof(bool)&&tick!=null)
            h.Patch(tick,prefix:new HarmonyMethod(typeof(ModelHotPaths),nameof(ScanBefore)),postfix:new HarmonyMethod(typeof(ModelHotPaths),nameof(ScanAfter)));
        FramePacing.mod.Log.LogInfo($"Model hot paths connected: direct combined pose={localPose!=null}, particle native-contract checked={particleConnected}, portrait fallback discovery={tick!=null}.");
    }
    static IEnumerable<CodeInstruction> PoseRewrite(IEnumerable<CodeInstruction> source){
        var code=source.ToList();int count=0;
        foreach(var i in code){
            if(i.operand is MethodInfo m&&m.DeclaringType==typeof(Transform)&&m.Name=="SetLocalPositionAndRotation"&&m.GetParameters().Length==2){
                i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(ModelHotPaths),nameof(Pose));count++;
            }
        }
        if(count!=1)throw new InvalidOperationException("ResetPose combined setter contract changed.");return code;
    }
    static float Error(Vector3 a,Vector3 b)=>Math.Max(Math.Abs(a.x-b.x),Math.Max(Math.Abs(a.y-b.y),Math.Abs(a.z-b.z)));
    static float Error(Quaternion a,Quaternion b){
        float same=Math.Max(Math.Abs(a.x-b.x),Math.Max(Math.Abs(a.y-b.y),Math.Max(Math.Abs(a.z-b.z),Math.Abs(a.w-b.w))));
        float inverse=Math.Max(Math.Abs(a.x+b.x),Math.Max(Math.Abs(a.y+b.y),Math.Max(Math.Abs(a.z+b.z),Math.Abs(a.w+b.w))));return Math.Min(same,inverse);
    }
    static void Pose(Transform t,Vector3 position,Quaternion rotation){
        if(FramePacing.enabled.Value&&localPose!=null){
            IntPtr p=validPointer(t);
            if(p!=IntPtr.Zero){
                if(!poseChecked){
                    poseChecked=true;
                    // No scene object is created. Rewrite the first live bone's
                    // exact existing pose and verify it before enabling the path.
                    Vector3 before=t.localPosition;Quaternion q=t.localRotation;
                    localPose(p,ref before,ref q);
                    poseUsable=Error(before,t.localPosition)<1e-5f&&Error(q,t.localRotation)<2e-5f;
                    if(!poseUsable)t.SetLocalPositionAndRotation(before,q);
                    FramePacing.mod.Log.LogInfo("Combined model pose native self-check="+poseUsable);
                }
                if(poseUsable){
                    // Live counters showed ~85% of bones change on every reset.
                    // Two native reads to avoid ~15% of setters add far more
                    // crossings than they save. Use one exact combined write;
                    // never trust a cached pose over another animator/IK writer.
                    localPose(p,ref position,ref rotation);poseCalls++;return;
                }
            }
        }
        t.SetLocalPositionAndRotation(position,rotation);
    }
    internal static bool Same(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
    internal static bool Same(Quaternion a,Quaternion b)=>a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w;
    internal static Color ToFloat(Color32 c)=>new Color{r=c.r/255f,g=c.g/255f,b=c.b/255f,a=c.a/255f};
    static byte Byte(float v)=>(byte)Math.Round(Math.Clamp(v,0,1)*255f,MidpointRounding.ToEven);
    internal static Color32 ToByte(Color c)=>new Color32{r=Byte(c.r),g=Byte(c.g),b=Byte(c.b),a=Byte(c.a)};
    static Color32 GetColor(ref ParticleSystem.Particle p){particleCalls++;return p.m_StartColor;}
    static void SetColor(ref ParticleSystem.Particle p,Color32 color){particleCalls++;p.m_StartColor=color;}
    static bool Equal(Color32 a,Color32 b)=>a.r==b.r&&a.g==b.g&&a.b==b.b&&a.a==b.a;
    static bool CheckParticleContract(){
        try{
            var p=default(ParticleSystem.Particle);
            for(int i=0;i<256;i++){
                var b=new Color32{r=(byte)i,g=(byte)(255-i),b=(byte)(i^73),a=(byte)(i^187)};
                p.startColor=b;if(!Equal(p.m_StartColor,b))return false;
                p.m_StartColor=b;if(!Equal(p.startColor,b))return false;
                Color native=b,ours=ToFloat(b);
                if(Math.Abs(native.r-ours.r)>1e-7||Math.Abs(native.g-ours.g)>1e-7||Math.Abs(native.b-ours.b)>1e-7||Math.Abs(native.a-ours.a)>1e-7)return false;
                // Include half-way values so round-to-even versus truncation is checked.
                var c=new Color{r=(i+.5f)/255f,g=(i+.499f)/255f,b=(i+.501f)/255f,a=(i-2)/250f};
                Color32 expected=c;if(!Equal(expected,ToByte(c)))return false;
            }
            return true;
        }catch(Exception e){FramePacing.mod.Log.LogWarning("Particle shortcut disabled: "+e.Message);return false;}
    }
    static IEnumerable<CodeInstruction> ParticleRewrite(IEnumerable<CodeInstruction> source){
        var code=source.ToList();int changed=0;
        foreach(var i in code){
            if(i.operand is not MethodInfo m)continue;
            string replace=null;
            if(m.DeclaringType==typeof(ParticleSystem.Particle)&&m.Name=="get_startColor")replace=nameof(GetColor);
            if(m.DeclaringType==typeof(ParticleSystem.Particle)&&m.Name=="set_startColor")replace=nameof(SetColor);
            if(m.DeclaringType==typeof(Color32)&&m.Name=="op_Implicit")replace=m.ReturnType==typeof(Color)?nameof(ToFloat):m.ReturnType==typeof(Color32)?nameof(ToByte):null;
            if(replace==null)continue;i.opcode=OpCodes.Call;i.operand=AccessTools.Method(typeof(ModelHotPaths),replace);changed++;
        }
        if(changed==0)throw new InvalidOperationException("No compatible particle color calls.");return code;
    }
    static void ScanBefore(out long __state)=>__state=(long)nextScan.GetValue(null);
    static void ScanAfter(long __state){
        if(!FramePacing.enabled.Value||!(bool)scanHooks.GetValue(null))return;
        long now=Environment.TickCount64,next=(long)nextScan.GetValue(null);
        // Image lifecycle hooks still enqueue immediately, and NextScan=0 refresh
        // requests are not delayed. Only extend a completed fallback scan.
        if(__state<=now&&next>now&&next<=now+3100){nextScan.SetValue(null,now+10000);scanDeferrals++;}
    }
    internal static string Summary(){string s=$"combinedPose={poseUsable} combinedPoseCalls={poseCalls} posePreReadRemoved=True particleDirect={particleConnected} particleFieldOps={particleCalls} portraitFallbackDeferrals={scanDeferrals}";poseCalls=particleCalls=scanDeferrals=0;return s;}
}
