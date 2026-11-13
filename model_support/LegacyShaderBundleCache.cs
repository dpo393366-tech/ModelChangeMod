using System;
using System.IO;
using UnityEngine;

namespace Vow.Performance;

// Borrow the legacy mod's exact original bundle. Never substitute the separately
// named RX22 display shader, unload a borrowed bundle, or change its materials.
internal static class LegacyShaderBundleCache {
 internal const string Name="wuwa_skin415.bundle";
 internal const string ShaderPath="assets/wuwa/hoyotoon/hoyotoonwutheringwaves.shader";
 internal static bool TryGet(string path,string directory,out AssetBundle result){
  result=null;
  if(string.IsNullOrEmpty(path)||!File.Exists(path) ||
   !string.Equals(Path.GetFullPath(path),Path.GetFullPath(Path.Combine(directory,Name)),StringComparison.OrdinalIgnoreCase))return false;
  var bundles=AssetBundle.GetAllLoadedAssetBundles_Native();
  if(bundles==null)return false;
  for(int i=0;i<bundles.Length;i++){
   var bundle=bundles[i];
   if(!bundle||bundle.name!=Name||!bundle.Contains(ShaderPath))continue;
   bundle.hideFlags|=HideFlags.DontUnloadUnusedAsset;
   result=bundle;return true;
  }
  return false;
 }
}
