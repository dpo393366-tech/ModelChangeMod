using System;using System.Linq;using System.IO;using Mono.Cecil;using Mono.Cecil.Cil;using System.Security.Cryptography;using System.Collections.Generic;
class Audit460 {
 static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in All(t.NestedTypes))yield return n;}}
 static void Check(bool ok,string s){if(!ok)throw new Exception(s);Console.WriteLine("PASS "+s);}
 static void Main(string[] a){
  using var old=ModuleDefinition.ReadModule(a[0]);using var current=ModuleDefinition.ReadModule(a[1]);
  var before=All(old.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).ToDictionary(m=>m.FullName,m=>Code(m,0));
  foreach(var m in All(current.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody&&before.ContainsKey(m.FullName))){
   int skip=m.DeclaringType.Name=="PreviewDriver"&&m.Name=="Update"?1:0;
   if(skip==1)Check(m.Body.Instructions[0].Operand.ToString().Contains("ModelSupport460::Ensure"),"first-frame bootstrap connected");
   var code=Code(m,skip);
   CheckSilent(code.SequenceEqual(before[m.FullName]),"Unexpected original method change "+m.FullName);
  }
  Check(true,"all R459 original method instructions preserved except bootstrap call");
  var resource=(EmbeddedResource)current.Resources.Single(r=>r.Name=="Laevat.ModelSupport460.dll");
  Check(SHA256.HashData(resource.GetResourceData()).SequenceEqual(SHA256.HashData(File.ReadAllBytes(a[2]))),"exact tested 1.3.7 support embedded");
  using var support=ModuleDefinition.ReadModule(new MemoryStream(resource.GetResourceData()));
  Check(!support.AssemblyReferences.Concat(current.AssemblyReferences).Any(r=>r.Name.Contains("Koikatsu",StringComparison.OrdinalIgnoreCase)||r.Name.Contains("CardPreview")||r.Name.Contains("RX22")),"zero Koikatsu assembly dependencies");
  Check(!support.Types.SelectMany(t=>t.CustomAttributes).Any(c=>c.AttributeType.Name=="BepInDependency"),"no optional mod required by support");
  var indoor=current.Types.Single(t=>t.Name=="IndoorCompanions193");
  Check(indoor.Methods.Single(m=>m.Name=="Reconcile").Body.Instructions.Count(i=>i.Operand is MethodReference mr&&mr.Name=="Add"&&mr.DeclaringType.FullName=="System.Collections.Generic.List`1<System.String>")==1,"actual R459 cap hook contract");
  foreach(var n in new[]{"Spawn","Adopt210"}){var m=indoor.Methods.Single(x=>x.Name==n);Check(m.IsStatic&&m.Parameters.Count==3&&m.ReturnType.FullName=="System.Void","actual R459 "+n+" allocation guard contract");}
  Check(support.Types.Single(t=>t.Name=="IndoorCompanionLimit").Fields.Any(f=>f.HasConstant&&Equals(f.Constant,3)),"hard cap 3 compiled into release");
 }
 static void CheckSilent(bool ok,string s){if(!ok)throw new Exception(s);}
 static string[] Code(MethodDefinition m,int skip){return m.Body.Instructions.Skip(skip).Select(i=>i.OpCode+" "+(i.Operand is Instruction target?"target:"+(m.Body.Instructions.IndexOf(target)-skip):i.Operand is Instruction[] targets?string.Join(",",targets.Select(t=>m.Body.Instructions.IndexOf(t)-skip)):i.Operand?.ToString())).ToArray();}
}
