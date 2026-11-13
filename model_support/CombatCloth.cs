using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace Vow.Performance;

// These are presentation-only garment/body contact passes, not Unity physics,
// gameplay hit boxes, foot placement, or the secondary spring animation itself.
internal static class CombatCloth {
    internal static bool Active;
    internal static int HookCount;
    internal static long Skipped,Restored;
    sealed class State {internal bool skipped;}
    static readonly ConditionalWeakTable<object,State> states=new();
    static Func<object,bool> eligible;
    static Action<object> reset;
    internal static readonly string[] ContactMethods={
        "ResolveHairBackContact","ResolveSkirtLegContact","ResolveSiSkirtContact",
        "ResolveLinContactR38","ResolveHsinClothR40","ResolveCarlottaClothR131",
        "ResolveQingxiaoClothR130","ResolveCartethyiaClothR129","UpdateYeCurtainContact",
        "ResolveAnastasiaGarment","ResolveAshleyDressR17","ResolveVelinaGarment",
        "ResolveVivianSkirt","ResolveYaogwangDress","ResolvePellicaSkirt",
        "ResolveGilbertaDrape","ResolveYvonneSleeves"
    };
    internal static void Connect(Assembly assembly,Harmony h){
        if(eligible!=null)return;
        var s=assembly.GetType("Laevat.Preview.PreviewSession",false);
        var t=assembly.GetType("Laevat.Preview.ModelTarget",false);
        if(s==null||t==null)return;
        var arg=Expression.Parameter(typeof(object),"session");
        var session=Expression.Convert(arg,s);
        var target=Expression.Variable(t,"target");
        var targetField=AccessTools.Field(s,"Target");
        Expression Flag(string n){var p=AccessTools.Property(t,n);if(p?.PropertyType!=typeof(bool))throw new InvalidOperationException("Model target flag changed: "+n);return Expression.Property(target,p);}
        if(targetField?.FieldType!=t)throw new InvalidOperationException("Model target contract changed.");
        eligible=Expression.Lambda<Func<object,bool>>(Expression.Block(new[]{target},
            Expression.Assign(target,Expression.Field(session,targetField)),
            Expression.AndAlso(Expression.NotEqual(target,Expression.Constant(null,t)),
                Expression.AndAlso(Expression.Not(Flag("IsEditor")),Expression.OrElse(Flag("IsMissionActor"),Flag("IsBattle"))))),arg).Compile();
        var resets=new List<Expression>();
        var resetSecondary=AccessTools.DeclaredMethod(s,"ResetSecondary",new[]{typeof(bool)});
        if(resetSecondary==null)throw new InvalidOperationException("Secondary reset contract changed.");
        // Native reset clears contact offsets and rebases springs just once when
        // returning to the world, instead of resuming stale pre-battle contact.
        resets.Add(Expression.Call(session,resetSecondary,Expression.Constant(true)));
        // Invalidate only caches whose original methods explicitly rebuild on null.
        // Rebuild once after combat so no old penetration offset is carried forward.
        foreach(string name in new[]{"LinContactRest","HsinRestR40","CarlRest131","QingRest130","CartRest129","AnastasiaRest","AshleyShapeR25","VivianSkirtRest","YaogwangRest","PellicaClothRest","GilbertaPanelRest"}){
            var f=AccessTools.Field(s,name);
            if(f!=null&&!f.IsInitOnly&&!f.FieldType.IsValueType)resets.Add(Expression.Assign(Expression.Field(session,f),Expression.Constant(null,f.FieldType)));
        }
        var cloth=assembly.GetType("Laevat.Preview.Cloth387",false);
        var table=cloth==null?null:AccessTools.Field(cloth,"States");
        var remove=table?.FieldType.GetMethod("Remove",new[]{s});
        if(table!=null&&table.IsStatic&&remove!=null)resets.Add(Expression.Call(Expression.Field(null,table),remove,session));
        resets.Add(Expression.Empty());
        reset=Expression.Lambda<Action<object>>(Expression.Block(resets),arg).Compile();
        foreach(string name in ContactMethods){
            var m=AccessTools.DeclaredMethod(s,name,Type.EmptyTypes);
            if(m==null||m.IsStatic||m.ReturnType!=typeof(void))throw new InvalidOperationException("Contact contract changed: "+name);
        }
        var update=cloth==null?null:AccessTools.DeclaredMethod(cloth,"Update",new[]{s});
        if(update==null||!update.IsStatic||update.ReturnType!=typeof(void))throw new InvalidOperationException("Cloth contact entry changed.");
        foreach(string name in ContactMethods){h.Patch(AccessTools.DeclaredMethod(s,name,Type.EmptyTypes),prefix:new HarmonyMethod(typeof(CombatCloth),nameof(InstanceBefore)));HookCount++;}
        h.Patch(update,prefix:new HarmonyMethod(typeof(CombatCloth),nameof(StaticBefore)));HookCount++;
        FramePacing.mod.Log.LogInfo("Combat garment contact gate connected: "+HookCount+" passes; spring animation and gameplay collisions retained.");
    }
    internal static bool Allow(object session){
        if(session==null)return true;
        if(Active&&FramePacing.enabled.Value&&eligible(session)){
            states.GetOrCreateValue(session).skipped=true;Skipped++;return false;
        }
        if(states.TryGetValue(session,out var state)&&state.skipped){
            state.skipped=false;reset(session);Restored++;
        }
        return true;
    }
    static bool InstanceBefore(object __instance)=>Allow(__instance);
    static bool StaticBefore(object __0)=>Allow(__0);
    internal static string Summary(){string r=$"combatContactOff={Active} contactHooks={HookCount} skippedContactPasses={Skipped} contactRestores={Restored}";Skipped=Restored=0;return r;}
}
