using System;
using System.Linq;
using System.Reflection;
using Laevat.Preview;
class BootstrapQA {
 static void Main(string[] args) {
  if(args.Length>0) {
   var a=Assembly.LoadFrom(args[0]);
   var t=a.GetType("Vow.Performance.FramePacing");
   t.GetMethod("Load").Invoke(Activator.CreateInstance(t),null);
  }
  for(int i=0;i<10000;i++) ModelSupport460.Ensure();
  if(ModelSupport460.Status.StartsWith("FAILED")) throw new Exception(ModelSupport460.Status);
  int instances=0,loads=0;
  foreach(var a in AppDomain.CurrentDomain.GetAssemblies()) {
   if(a.GetName().Name.Contains("Koikatsu") || a.GetName().Name.Contains("HeroesVowCardPreview")) throw new Exception("Unexpected dependency");
   var t=a.GetType("Vow.Performance.FramePacing");
   if(t!=null) {instances++;loads+=(int)t.GetField("Loads").GetValue(null);}
  }
  if(instances!=1||loads!=1)throw new Exception("Duplicate/missing support initialization");
  Console.WriteLine("PASS bootstrap: no Koikatsu; one initialization after 10000 updates; "+ModelSupport460.Status);
 }
}
