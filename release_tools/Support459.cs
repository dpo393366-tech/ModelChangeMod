using System;using System.IO;using System.Linq;using System.Collections.Generic;using Mono.Cecil;using Mono.Cecil.Cil;
class Support459 {
 static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in All(t.NestedTypes))yield return n;}}
 static void Main(string[] a){
  using var m=ModuleDefinition.ReadModule(a[1]);
  if(a[0]=="patch"){
   var method=All(m.Types).Single(t=>t.FullName=="Laevat.Preview.PreviewSession").Methods.Single(x=>x.Name=="CreateExtra319");
   var item=method.Body.Instructions.Single(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand=="model.lvp");item.Operand="wing_mesh459.bin";m.Write(a[2]);
   using var updated=ModuleDefinition.ReadModule(a[2]);var before=All(ModuleDefinition.ReadModule(a[1]).Types).SelectMany(t=>t.Methods).Where(x=>x.HasBody).ToDictionary(x=>x.FullName,x=>string.Join("\n",x.Body.Instructions));
   var changes=All(updated.Types).SelectMany(t=>t.Methods).Where(x=>x.HasBody&&before[x.FullName]!=string.Join("\n",x.Body.Instructions)).Select(x=>x.FullName).ToArray();
   if(changes.Length!=1||changes[0]!=method.FullName)throw new Exception("Unexpected runtime changes: "+string.Join(",",changes));
   Console.WriteLine("PASS only CreateExtra319 support-resource path changed");
  }else if(a[0]=="embed"){
   foreach(var f in Directory.GetFiles(a[2])){var old=m.Resources.Single(r=>r.Name==Path.GetFileName(f));m.Resources.Remove(old);m.Resources.Add(new EmbeddedResource(old.Name,old.Attributes,File.ReadAllBytes(f)));}m.Write(a[3]);
  }
 }
}
