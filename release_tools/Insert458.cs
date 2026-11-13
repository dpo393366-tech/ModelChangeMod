using System;using System.IO;using System.Linq;using System.Collections.Generic;using Mono.Cecil;using Mono.Cecil.Cil;
class Replace426 {
 static IEnumerable<TypeDefinition> Tree(TypeDefinition t)=>new[]{t}.Concat(t.NestedTypes.SelectMany(Tree));
 static void Main(string[] a){using var m=ModuleDefinition.ReadModule(a[0]);using var h=ModuleDefinition.ReadModule(a[1]);using var baseline=ModuleDefinition.ReadModule(a[2]);
 foreach(var mt in m.Types.SelectMany(Tree).SelectMany(t=>t.Methods).Where(x=>x.HasBody)){var ignored=mt.Body.Instructions.Count;}var replace=a[4];var removed=m.Types.SingleOrDefault(t=>t.Name==replace);if(removed!=null)m.Types.Remove(removed);var types=m.Types.SelectMany(Tree).ToDictionary(t=>t.FullName);var methodMap=new Dictionary<MethodDefinition,MethodDefinition>();var fieldMap=new Dictionary<FieldDefinition,FieldDefinition>();var cloned=new Dictionary<string,TypeDefinition>();var sources=new List<TypeDefinition>();
 void Shell(TypeDefinition src,TypeDefinition parent=null,string name=null){var d=new TypeDefinition(src.Namespace,name??src.Name,src.Attributes,null){ClassSize=src.ClassSize,PackingSize=src.PackingSize};if(parent==null)m.Types.Add(d);else parent.NestedTypes.Add(d);cloned[src.FullName]=d;sources.Add(src);foreach(var gp in src.GenericParameters)d.GenericParameters.Add(new GenericParameter(gp.Name,d));foreach(var n in src.NestedTypes)Shell(n,d);}
 foreach(var t in h.Types.Where(t=>t.Name==replace))Shell(t,null,t.Name.StartsWith("<")?"VOC420_"+t.Name:null);
 TypeReference T(TypeReference r){
  if(r is GenericParameter gp){if(gp.Owner is TypeReference owner&&cloned.TryGetValue(owner.FullName,out var mapped))return mapped.GenericParameters[gp.Position];return r;}
  if(r is RequiredModifierType rm)return new RequiredModifierType(T(rm.ModifierType),T(rm.ElementType));
  if(r is OptionalModifierType om)return new OptionalModifierType(T(om.ModifierType),T(om.ElementType));
  if(r is PinnedType pt)return new PinnedType(T(pt.ElementType));
  if(r is ArrayType ar)return new ArrayType(T(ar.ElementType),ar.Rank);
  if(r is ByReferenceType br)return new ByReferenceType(T(br.ElementType));
  if(r is PointerType ptr)return new PointerType(T(ptr.ElementType));
  if(r is GenericInstanceType g){var x=new GenericInstanceType(T(g.ElementType));foreach(var arg in g.GenericArguments)x.GenericArguments.Add(T(arg));return x;}
  if(cloned.TryGetValue(r.FullName,out var d))return d;
  if(types.TryGetValue(r.FullName,out d))return d;
  if(r.FullName.StartsWith("System."))return m.ImportReference(r);
  var existing=m.GetTypeReferences().FirstOrDefault(t=>t.FullName==r.FullName);
  if(existing!=null)return existing;
  if(r.Scope.Name=="Api")throw new Exception("Unresolved stub type: "+r.FullName);
  return m.ImportReference(r);
 }
 foreach(var src in sources){var d=cloned[src.FullName];d.BaseType=src.BaseType==null?null:T(src.BaseType);foreach(var it in src.Interfaces)d.Interfaces.Add(new InterfaceImplementation(T(it.InterfaceType)));
  foreach(var f in src.Fields){var n=new FieldDefinition(f.Name,f.Attributes,T(f.FieldType));if(f.HasConstant)n.Constant=f.Constant;if(f.InitialValue!=null&&f.InitialValue.Length>0)n.InitialValue=f.InitialValue;if(f.HasLayoutInfo)n.Offset=f.Offset;d.Fields.Add(n);fieldMap[f]=n;}
  foreach(var f in src.Methods){var n=new MethodDefinition(f.Name,f.Attributes,T(f.ReturnType)){ImplAttributes=f.ImplAttributes,CallingConvention=f.CallingConvention};foreach(var gp in f.GenericParameters)n.GenericParameters.Add(new GenericParameter(gp.Name,n));foreach(var p in f.Parameters)n.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,T(p.ParameterType)));d.Methods.Add(n);methodMap[f]=n;}
 }
 MethodReference M(MethodReference r){
  string math=null;
  if(r.DeclaringType.FullName=="UnityEngine.Vector3")math=r.Name switch {"get_up"=>"Up","get_zero"=>"Zero","get_magnitude"=>"Mag","op_Addition"=>"Add","op_Subtraction"=>"Sub","op_Multiply"=>"Mul","op_Division"=>"Div","Dot"=>"Dot",_=>null};
  if(r.DeclaringType.FullName=="UnityEngine.Quaternion"&&r.Name=="op_Multiply")math=r.ReturnType.FullName=="UnityEngine.Vector3"?"Rotate":"QMul";
  if(math!=null)return types["Laevat.Preview.Math419"].Methods.Single(x=>x.Name==math);

  if(r is GenericInstanceMethod g){var n=new GenericInstanceMethod(M(g.ElementMethod));foreach(var x in g.GenericArguments)n.GenericArguments.Add(T(x));return n;}
  var owner=T(r.DeclaringType);if(owner is TypeDefinition d){var found=d.Methods.FirstOrDefault(x=>x.Name==r.Name&&x.Parameters.Count==r.Parameters.Count&&x.Parameters.Zip(r.Parameters).All(p=>p.First.ParameterType.FullName==T(p.Second.ParameterType).FullName||(p.First.ParameterType is GenericParameter&&p.Second.ParameterType is GenericParameter))&&x.GenericParameters.Count==r.GenericParameters.Count);if(found==null)throw new Exception("Missing method "+r.FullName);return found;}
  var nref=new MethodReference(r.Name,T(r.ReturnType),owner){HasThis=r.HasThis,ExplicitThis=r.ExplicitThis,CallingConvention=r.CallingConvention};foreach(var gp in r.GenericParameters)nref.GenericParameters.Add(new GenericParameter(gp.Name,nref));foreach(var p in r.Parameters)nref.Parameters.Add(new ParameterDefinition(T(p.ParameterType)));return m.ImportReference(nref);
 }
 FieldReference F(FieldReference r){var owner=T(r.DeclaringType);if(owner is TypeDefinition d){var f=d.Fields.SingleOrDefault(x=>x.Name==r.Name);if(f==null)throw new Exception("Missing field "+r.FullName);if(f.FieldType.FullName!=T(r.FieldType).FullName)throw new Exception("Field type mismatch "+r.FullName);return f;}return m.ImportReference(new FieldReference(r.Name,T(r.FieldType),owner));}
 foreach(var kv in methodMap){var src=kv.Key;var d=kv.Value;foreach(var ov in src.Overrides)d.Overrides.Add(M(ov));if(!src.HasBody)continue;d.Body=new MethodBody(d){InitLocals=src.Body.InitLocals,MaxStackSize=32};foreach(var v in src.Body.Variables)d.Body.Variables.Add(new VariableDefinition(T(v.VariableType)));var ins=src.Body.Instructions.ToDictionary(x=>x,x=>Instruction.Create(OpCodes.Nop));
  foreach(var x in src.Body.Instructions){var y=ins[x];y.OpCode=x.OpCode;y.Operand=x.Operand switch{Instruction i=>ins[i],Instruction[] arr=>arr.Select(i=>ins[i]).ToArray(),TypeReference r=>T(r),MethodReference r=>M(r),FieldReference r=>F(r),VariableDefinition v=>d.Body.Variables[v.Index],ParameterDefinition p=>d.Parameters[p.Index],_=>x.Operand};
   d.Body.Instructions.Add(y);}
  foreach(var eh in src.Body.ExceptionHandlers)d.Body.ExceptionHandlers.Add(new ExceptionHandler(eh.HandlerType){TryStart=ins[eh.TryStart],TryEnd=eh.TryEnd==null?null:ins[eh.TryEnd],HandlerStart=ins[eh.HandlerStart],HandlerEnd=eh.HandlerEnd==null?null:ins[eh.HandlerEnd],FilterStart=eh.FilterStart==null?null:ins[eh.FilterStart],CatchType=eh.CatchType==null?null:T(eh.CatchType)});
 }
 foreach(var src in sources)foreach(var prop in src.Properties){var np=new PropertyDefinition(prop.Name,prop.Attributes,T(prop.PropertyType));if(prop.GetMethod!=null)np.GetMethod=methodMap[prop.GetMethod];if(prop.SetMethod!=null)np.SetMethod=methodMap[prop.SetMethod];foreach(var parameter in prop.Parameters)np.Parameters.Add(new ParameterDefinition(parameter.Name,parameter.Attributes,T(parameter.ParameterType)));cloned[src.FullName].Properties.Add(np);}




 foreach(var method in m.Types.SelectMany(Tree).SelectMany(t=>t.Methods).Where(x=>x.HasBody))foreach(var i in method.Body.Instructions){if(i.Operand is TypeReference tr&&tr.FullName.Contains(replace))i.Operand=T(tr);if(i.Operand is MethodReference r&&r.FullName.Contains(replace))i.Operand=M(r);if(i.Operand is FieldReference f&&f.FullName.Contains(replace))i.Operand=F(f);}
 foreach(var t in m.Types.SelectMany(Tree)){foreach(var f in t.Fields)if(f.FieldType.FullName.Contains(replace))f.FieldType=T(f.FieldType);foreach(var md in t.Methods){if(md.ReturnType.FullName.Contains(replace))md.ReturnType=T(md.ReturnType);foreach(var param in md.Parameters)if(param.ParameterType.FullName.Contains(replace))param.ParameterType=T(param.ParameterType);if(md.HasBody)foreach(var v in md.Body.Variables)if(v.VariableType.FullName.Contains(replace))v.VariableType=T(v.VariableType);}} m.Write(a[3]);}}


