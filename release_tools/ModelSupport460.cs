using System;
using System.IO;
using System.Reflection;

namespace Laevat.Preview {
 // Invoked on the first Unity update, after BepInEx has finished discovering plugins.
 // No Koikatsu assemblies, assets, config, or scene objects are required or touched.
 public static class ModelSupport460 {
  static bool attempted;
  public static string Status = "pending";
  public static void Ensure() {
   if (attempted) return;
   attempted = true;
   try {
    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
     Type existing = assembly.GetType("Vow.Performance.FramePacing", false);
     if (existing == null) continue;
     FieldInfo instance = existing.GetField("mod", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
     if (instance == null || instance.GetValue(null) == null) continue;
     string version = "0.0.0";
     foreach (CustomAttributeData attr in existing.GetCustomAttributesData())
      if (attr.AttributeType.FullName == "BepInEx.BepInPlugin" && attr.ConstructorArguments.Count == 3)
       version = (string)attr.ConstructorArguments[2].Value;
     if (new Version(version) < new Version("1.3.7"))
      throw new InvalidOperationException("Older standalone FramePacing " + version + "; update/remove that optional plugin before using R460. Duplicate hooks were not installed.");
     Status = "reusing standalone model support " + version;
     Report();
     return;
    }
    Assembly owner = typeof(ModelSupport460).Assembly;
    byte[] bytes;
    using (Stream stream = owner.GetManifestResourceStream("Laevat.ModelSupport460.dll"))
    using (var buffer = new MemoryStream()) {
     if (stream == null) throw new InvalidDataException("R460 model support resource is missing");
     stream.CopyTo(buffer); bytes = buffer.ToArray();
    }
    Assembly support = Assembly.Load(bytes);
    Type plugin = support.GetType("Vow.Performance.FramePacing", true);
    object value = Activator.CreateInstance(plugin);
    plugin.GetMethod("Load", BindingFlags.Instance | BindingFlags.Public).Invoke(value, null);
    Status = "embedded model support 1.3.7 loaded; Koikatsu not required";
    Report();
   } catch (Exception e) {
    Status = "FAILED: " + e;
    Report();
   }
  }
  static void Report() {
   // Once per startup; no disk access in the per-frame fast path.
   try { Console.WriteLine("[ModelChangeMod R460] " + Status); } catch { }
  }
 }
}
