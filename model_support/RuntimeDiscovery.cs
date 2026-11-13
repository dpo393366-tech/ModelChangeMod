using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Vow.Performance;

// Discover only the model runtime, on assembly arrival. Absence of any other
// plugin never creates a polling loop or an incomplete dependency condition.
internal static class RuntimeDiscovery {
    static readonly ConcurrentQueue<Assembly> pending=new();
    static readonly HashSet<Assembly> seen=new();
    static readonly object gate=new();
    static bool started;
    internal static void Start(){
        lock(gate){if(started)return;started=true;}
        AppDomain.CurrentDomain.AssemblyLoad+=Loaded;
        foreach(var a in AppDomain.CurrentDomain.GetAssemblies())Offer(a);
    }
    static void Loaded(object sender,AssemblyLoadEventArgs e)=>Offer(e.LoadedAssembly);
    static void Offer(Assembly a){
        string name=a.GetName().Name;
        if(name!="Laevat.Preview.Runtime"&&name!="Laevat.Preview")return;
        lock(gate){if(!seen.Add(a))return;pending.Enqueue(a);}
    }
    internal static bool TryTake(out Assembly assembly){
        while(pending.TryDequeue(out assembly)){
            if(assembly.GetType("Laevat.Preview.PreviewSession",false)!=null)return true;
        }
        assembly=null;return false;
    }
}
