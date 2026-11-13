using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vow.Performance;

// These two cameras generate army flag lettering, not duel portraits or FX.
// Retain their textures and objects; pause only automatic camera rendering during
// martial/debate combat and restore the enabled state at combat exit.
internal static class CombatFlagCameras {
    sealed class Entry {internal Wa_FlagWordCtrl owner;internal Camera camera;internal RenderTexture target;internal bool paused;}
    static readonly List<Entry> entries=new();
    static readonly HashSet<int> scanned=new();
    static long nextScan,pausedFrames,restores;
    static string error="none";
    static bool failed;
    internal static void Tick(bool duel,long now){
        if(failed)return;
        try{
            bool pause=duel&&FramePacing.enabled.Value;
            if(now>=nextScan){
                nextScan=now+1000;
                for(int i=entries.Count-1;i>=0;i--){var e=entries[i];
                    if(e.owner==null||e.camera==null||e.owner.m_texture!=e.target||e.camera.targetTexture!=e.target){Restore(e);entries.RemoveAt(i);scanned.Clear();}
                }
                var words=Wa_FlagWordCtrl.m_word;
                if(words!=null)foreach(var owner in words){
                    if(owner==null||owner.m_texture==null)continue;
                    int id=owner.GetInstanceID();if(scanned.Contains(id))continue;
                    bool found=false;
                    foreach(var c in owner.GetComponentsInChildren<Camera>(true)){
                        if(c==null||c.targetTexture!=owner.m_texture)continue;
                        found=true;bool known=false;foreach(var e in entries)if(e.camera==c){known=true;break;}
                        if(!known)entries.Add(new Entry{owner=owner,camera=c,target=owner.m_texture});
                    }
                    if(found)scanned.Add(id);
                }
            }
            foreach(var e in entries){
                if(e.camera==null)continue;
                if(pause){if(e.camera.enabled){e.camera.enabled=false;e.paused=true;}if(e.paused)pausedFrames++;}
                else Restore(e);
            }
        }catch(Exception ex){failed=true;error=ex.GetType().Name+":"+ex.Message.Replace('\r',' ').Replace('\n',' ');foreach(var e in entries)Restore(e);}
    }
    static void Restore(Entry e){if(!e.paused)return;e.paused=false;try{if(e.camera!=null&&!e.camera.enabled){e.camera.enabled=true;restores++;}}catch{}}
    internal static void Release(){foreach(var e in entries)Restore(e);entries.Clear();scanned.Clear();}
    internal static string Summary(){int count=0;foreach(var e in entries)if(e.paused)count++;string s=$"armyFlagCameras={entries.Count} armyFlagCamerasPaused={count} armyFlagCameraFramesSkipped={pausedFrames} armyFlagCameraRestores={restores} armyFlagCameraError=[{error}]";pausedFrames=restores=0;return s;}
}
