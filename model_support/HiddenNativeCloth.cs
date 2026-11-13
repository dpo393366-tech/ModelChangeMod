using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MagicaCloth2;
using UnityEngine;

namespace Vow.Performance;

// The imported body hides native renderers but still needs the native animator.
// Stop only mesh-cloth components whose EVERY source renderer is owned by that
// session's hidden list. Do not disable animators, objects, bone cloth or visible
// game clothing. Native OnDisable/OnEnable keeps Magica's team lifecycle intact.
internal static class HiddenNativeCloth {
    sealed class State {
        internal WeakReference<object> owner;
        internal long nextCheck,nextScan;
        internal Transform avatar;
        internal MagicaCloth[] candidates=Array.Empty<MagicaCloth>();
        internal readonly HashSet<int> hidden=new();
        internal readonly HashSet<int> wanted=new();
        internal readonly List<int> claims=new();
        internal bool failed;
    }
    sealed class Lease {internal MagicaCloth cloth;internal int owners;}
    static readonly ConditionalWeakTable<object,State> states=new();
    static readonly Dictionary<int,Lease> leases=new();
    static readonly List<State> living=new();
    static long nextMaintenance;
    static FieldInfo avatar,hidden,disposed,root;
    static bool connected;
    static string failure="none";
    internal static void ConnectionFailed(Exception e){failure=e.GetType().Name+":"+e.Message.Replace('\r',' ').Replace('\n',' ');}
    static long scans,acquired,released,eligible,candidatesSeen;
    internal static void Connect(Assembly a,Harmony h){
        if(connected)return;var t=a.GetType("Laevat.Preview.PreviewSession",false);if(t==null)return;
        avatar=AccessTools.Field(t,"Avatar");hidden=AccessTools.Field(t,"Hidden");disposed=AccessTools.Field(t,"Disposed");root=AccessTools.Field(t,"Root");
        // The runtime has BOTH ModelTarget and MakeNPCDisplayManager overloads.
        // Name-only lookup throws before the hook is installed.
        var target=a.GetType("Laevat.Preview.ModelTarget",false);
        var tick=target==null?null:AccessTools.DeclaredMethod(t,"Core457_Tick",new[]{target});var end=AccessTools.DeclaredMethod(t,"Dispose",Type.EmptyTypes);
        if(avatar?.FieldType!=typeof(Transform)||hidden?.FieldType!=typeof(List<(Renderer,bool)>)||disposed?.FieldType!=typeof(bool)||root?.FieldType!=typeof(GameObject)||tick==null||end==null)
            throw new InvalidOperationException("Hidden native cloth ownership contract changed.");
        h.Patch(tick,postfix:new HarmonyMethod(typeof(HiddenNativeCloth),nameof(AfterTick)));
        h.Patch(end,prefix:new HarmonyMethod(typeof(HiddenNativeCloth),nameof(End)));
        connected=true;
        failure="none";
        FramePacing.mod.Log.LogInfo("Hidden native mesh-cloth suspension connected; visible clothing and source animation preserved.");
    }
    static void AfterTick(object __instance){
        var s=states.GetValue(__instance,owner=>{var state=new State{owner=new WeakReference<object>(owner)};living.Add(state);return state;});
        long now=Environment.TickCount64;if(now<s.nextCheck)return;s.nextCheck=now+500;
        try{
            if(s.failed||!FramePacing.enabled.Value||(bool)disposed.GetValue(__instance)||root.GetValue(__instance) is not GameObject model||model==null){ReleaseAll(s);return;}
            var a=avatar.GetValue(__instance) as Transform;
            if(a==null){ReleaseAll(s);return;}
            if(s.avatar!=a){ReleaseAll(s);s.avatar=a;s.nextScan=0;}
            var owned=(List<(Renderer renderer,bool enabled)>)hidden.GetValue(__instance);
            s.hidden.Clear();foreach(var x in owned)if(x.renderer!=null&&!x.renderer.enabled)s.hidden.Add(x.renderer.GetInstanceID());
            if(s.hidden.Count==0){ReleaseAll(s);return;}
            if(now>=s.nextScan){
                s.nextScan=now+5000;scans++;
                var found=a.GetComponentsInChildren<MagicaCloth>(true);
                var list=new List<MagicaCloth>();foreach(var c in found)if(c!=null&&!c.transform.IsChildOf(model.transform))list.Add(c);
                s.candidates=list.ToArray();
                candidatesSeen+=s.candidates.Length;
            }
            Reconcile(s);
        }catch(Exception e){ReleaseAll(s);s.failed=true;FramePacing.mod.Log.LogWarning("Native cloth ownership check retained original behavior: "+e.Message);}
    }
    static bool Eligible(MagicaCloth c,HashSet<int> owned){
        if(c==null)return false;var data=c.SerializeData;
        if(data==null||data.clothType!=ClothProcess.ClothType.MeshCloth)return false;
        var renderers=data.sourceRenderers;if(renderers==null||renderers.Count==0)return false;
        // Unknown/null entries and mixed visible/hidden clothing retain native behavior.
        for(int i=0;i<renderers.Count;i++){var r=renderers[i];if(r==null||r.enabled||!owned.Contains(r.GetInstanceID()))return false;}
        return true;
    }
    static void Reconcile(State s){
        s.wanted.Clear();
        foreach(var c in s.candidates){
            if(!Eligible(c,s.hidden))continue;int id=c.GetInstanceID();
            if(!s.wanted.Add(id))continue;eligible++;
            if(s.claims.Contains(id))continue;
            if(leases.TryGetValue(id,out var existing)){existing.owners++;s.claims.Add(id);continue;}
            // A component disabled by the game or another mod is not ours to restore.
            if(!c.enabled)continue;
            leases.Add(id,new Lease{cloth=c,owners=1});s.claims.Add(id);
            c.enabled=false;acquired++;
        }
        for(int i=s.claims.Count-1;i>=0;i--)if(!s.wanted.Contains(s.claims[i])){Release(s.claims[i]);s.claims.RemoveAt(i);}
    }
    static void Release(int id){
        if(!leases.TryGetValue(id,out var l))return;if(--l.owners>0)return;
        leases.Remove(id);released++;
        try{if(l.cloth!=null&&!l.cloth.enabled)l.cloth.enabled=true;}catch{}
    }
    static void ReleaseAll(State s){foreach(var id in s.claims)Release(id);s.claims.Clear();}
    static void End(object __instance){if(states.TryGetValue(__instance,out var s)){ReleaseAll(s);living.Remove(s);states.Remove(__instance);}}
    internal static void Maintain(long now){
        if(now<nextMaintenance)return;nextMaintenance=now+1000;
        for(int i=living.Count-1;i>=0;i--){var s=living[i];
            if(!s.owner.TryGetTarget(out _)){ReleaseAll(s);living.RemoveAt(i);}
            else if(!FramePacing.enabled.Value)ReleaseAll(s);
        }
    }
    internal static string Summary(){string text=$"hiddenNativeCloth={connected} nativeClothSuspended={leases.Count} nativeClothScans={scans} nativeClothAcquired={acquired} nativeClothRestored={released} nativeClothEligibleChecks={eligible} nativeClothCandidates={candidatesSeen} nativeClothConnectionError=[{failure}]";scans=acquired=released=eligible=candidatesSeen=0;return text;}
}
