using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
class Hook460 {
 static void Main(string[] args) {
  using var m = ModuleDefinition.ReadModule(args[0]);
  var entry = m.Types.Single(t => t.FullName == "Laevat.Preview.ModelSupport460").Methods.Single(x => x.Name == "Ensure");
  var update = m.Types.Single(t => t.FullName == "Laevat.Preview.PreviewDriver").Methods.Single(x => x.Name == "Update" && x.Parameters.Count == 0);
  if (update.Body.Instructions.Any(i => i.Operand is MethodReference mr && mr.FullName == entry.FullName)) throw new Exception("Already hooked");
  update.Body.GetILProcessor().InsertBefore(update.Body.Instructions[0], Instruction.Create(OpCodes.Call, entry));
  m.Resources.Add(new EmbeddedResource("Laevat.ModelSupport460.dll", ManifestResourceAttributes.Private, File.ReadAllBytes(args[1])));
  m.Write(args[2]);
  Console.WriteLine("PASS first-update bootstrap and embedded model-only support");
 }
}
