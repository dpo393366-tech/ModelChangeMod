using System;using System.IO;using System.Diagnostics;using System.Text.Json;using System.Security.Cryptography;
namespace Laevat.AutomaticCompatibility {
public static class AutoUpdateBridge420 {
 public static string RuntimeFolder;
 public static bool NeedsActivation(string root)=>File.Exists(Path.Combine(root,"active.json"))||Directory.Exists(Path.Combine(root,"pending458"));
 public static ProcessStartInfo Start(string python,string script,string game,string work,string action){
  var p=new ProcessStartInfo(python){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=work};
  foreach(var s in new[]{"-X","utf8","-B",script,action,"--game",game})p.ArgumentList.Add(s);return p;
 }
 static void Run(string python,string script,string game,string work,string action,int timeout){
  using var p=Process.Start(Start(python,script,game,work,action));if(p==null)return;
  if(!p.WaitForExit(timeout)){try{p.Kill(true);p.WaitForExit(3000);}catch{}if(action=="boot")Run(python,script,game,work,"repair",30000);}
 }
 public static void Begin(string folder){
  RuntimeFolder=null;string root=Path.Combine(folder,"updates420");
  try{
   string bepin=Directory.GetParent(folder).FullName,game=Directory.GetParent(bepin).FullName;
   string python=Path.Combine(bepin,"LaevatTools","python","python.exe"),script=Path.Combine(bepin,"LaevatTools","auto_update.py");
   if(File.Exists(Path.Combine(folder,"update_config420.json"))&&File.Exists(python)&&File.Exists(script))Run(python,script,game,bepin,"boot",240000);
  }catch{}
  // An offline/update error never prevents the previously installed runtime from starting.
  try{
   var active=Path.Combine(root,"active.json");if(!File.Exists(active))return;
   using var doc=JsonDocument.Parse(File.ReadAllBytes(active));var r=doc.RootElement;if(r.GetProperty("sequence").GetInt32()<=0)return;
   string dir=Path.GetFullPath(r.GetProperty("runtime").GetString());string prefix=Path.GetFullPath(Path.Combine(root,"runtime"))+Path.DirectorySeparatorChar;
   if(!dir.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return;
   foreach(var name in new[]{"Laevat.Compat166.zip","Laevat.Compat166.manifest.json"}){
    string path=Path.Combine(dir,name);if(!File.Exists(path))return;
    using var stream=File.OpenRead(path);using var sha=SHA256.Create();string hash=Convert.ToHexString(sha.ComputeHash(stream));
    if(!string.Equals(hash,r.GetProperty("hashes").GetProperty(name).GetString(),StringComparison.OrdinalIgnoreCase))return;
   }
   RuntimeFolder=dir;
  }catch{RuntimeFolder=null;}
 }
 public static byte[] Resource(string name){
  if(RuntimeFolder==null||name!="Laevat.Compat166.zip"&&name!="Laevat.Compat166.manifest.json")return null;
  try{return File.ReadAllBytes(Path.Combine(RuntimeFolder,name));}catch{return null;}
 }
}
}
class BridgeEntry458 {static void Main(){}}
