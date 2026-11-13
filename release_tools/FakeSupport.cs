using System;
namespace BepInEx {public class BepInPlugin:Attribute { public BepInPlugin(string id,string title,string version){} }}
namespace Vow.Performance {
 [BepInEx.BepInPlugin("local.heroesvow.framepacing","Model frame pacing","1.3.7")]
 public class FramePacing {
  internal static FramePacing mod;
  public static int Loads;
  public void Load(){mod=this;Loads++;}
 }
}
