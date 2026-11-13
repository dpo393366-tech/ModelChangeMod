using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
namespace Vow.Performance;

// Presentation-only ceiling. Never truncate or write the game's companion roster.
internal static class IndoorCompanionLimit {
    internal const int Maximum=3;
    static FieldInfo owned;
    static bool connected;
    internal static void Connect(Assembly assembly,Harmony harmony){
        if(connected)return;
        var type=assembly.GetType("Laevat.Preview.IndoorCompanions193",false);
        if(type==null)return;
        var reconcile=AccessTools.DeclaredMethod(type,"Reconcile",Type.EmptyTypes);
        var spawn=AccessTools.DeclaredMethod(type,"Spawn",new[]{typeof(string),typeof(int),typeof(Vector3)});
        var adopt=AccessTools.DeclaredMethod(type,"Adopt210",new[]{typeof(NPCCtrl),typeof(string),typeof(int)});
        var field=AccessTools.Field(type,"Owned");
        if(reconcile==null||spawn==null||adopt==null||!reconcile.IsStatic||!spawn.IsStatic||!adopt.IsStatic||
           reconcile.ReturnType!=typeof(void)||spawn.ReturnType!=typeof(void)||adopt.ReturnType!=typeof(void)||
           field==null||!field.IsStatic||!typeof(IDictionary).IsAssignableFrom(field.FieldType))
            throw new InvalidOperationException("Indoor companion ownership contract changed.");
        owned=field;
        harmony.Patch(reconcile,transpiler:new HarmonyMethod(typeof(IndoorCompanionLimit),nameof(LimitLocalRoster)));
        harmony.Patch(spawn,prefix:new HarmonyMethod(typeof(IndoorCompanionLimit),nameof(BeforeSpawn)){priority=Priority.Last});
        harmony.Patch(adopt,prefix:new HarmonyMethod(typeof(IndoorCompanionLimit),nameof(BeforeAdopt)){priority=Priority.Last});
        connected=true;
        FramePacing.mod.Log.LogInfo("Indoor companion hard limit: 3. Local presentation roster only; recruitment/save roster unchanged.");
    }
    internal static void AddLimited(List<string> localRoster,string id){
        if(localRoster.Count<Maximum)localRoster.Add(id);
    }
    internal static bool CanCreate(IDictionary current,string id,int slot)=>
        current!=null && !string.IsNullOrEmpty(id) && slot>=0 && slot<Maximum &&
        current.Count<Maximum && !current.Contains(id);
    static bool BeforeSpawn(string __0,int __1)=>CanCreate(owned.GetValue(null) as IDictionary,__0,__1);
    static bool BeforeAdopt(string __1,int __2)=>CanCreate(owned.GetValue(null) as IDictionary,__1,__2);
    internal static IEnumerable<CodeInstruction> LimitLocalRoster(IEnumerable<CodeInstruction> instructions){
        var code=instructions.ToList();
        var add=AccessTools.DeclaredMethod(typeof(List<string>),nameof(List<string>.Add),new[]{typeof(string)});
        var sites=code.Where(i=>(i.opcode==OpCodes.Callvirt||i.opcode==OpCodes.Call)&&Equals(i.operand,add)).ToArray();
        if(sites.Length!=1)throw new InvalidOperationException("Expected exactly one managed presentation roster Add in Reconcile.");
        // Same stack shape; preserve original labels and exception boundaries.
        // The existing Reconcile then releases excess owned clones via its own
        // cleanup and restores borrowed room residents without deleting them.
        sites[0].opcode=OpCodes.Call;
        sites[0].operand=AccessTools.DeclaredMethod(typeof(IndoorCompanionLimit),nameof(AddLimited));
        return code;
    }
    internal static string Summary()=>$"indoorCompanionLimit={Maximum} indoorLimitConnected={connected}";
}
