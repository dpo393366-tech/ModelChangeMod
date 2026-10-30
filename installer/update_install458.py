"""Install the real startup updater during either existing high/low installation path."""
from pathlib import Path
import json
def protected_paths(game,package=None):
 root=Path(game)/'BepInEx/LaevatPreview/updates420';paths=set()
 if package is not None:
  try:
   incoming=json.loads((Path(package)/'설치자료/필수효과/update_config420.json').read_bytes()).get('installed_sequence',0)
   active=json.loads((root/'active.json').read_bytes()).get('sequence',0) if (root/'active.json').is_file() else 0
   installed=json.loads((root.parent/'update_config420.json').read_bytes()).get('installed_sequence',0) if (root.parent/'update_config420.json').is_file() else 0
   if incoming>max(active,installed):return paths
  except (OSError,ValueError,TypeError):pass
 for name in ('managed458.json','removed458.json'):
  try:paths.update(json.loads((root/name).read_bytes()))
  except (OSError,ValueError,TypeError):pass
 return paths
def changes(package,game):
 package=Path(package);game=Path(game);result=[]
 protected=protected_paths(game,package)
 for name in ('auto_update.py','transaction458.py'):
  if 'BepInEx/LaevatTools/'+name in protected:continue
  source=package/'payload/BepInEx/LaevatTools'/name
  if source.is_file():result.append((game/'BepInEx/LaevatTools'/name,source.read_bytes()))
 for source in (package/'tools/python').glob('*'):
  target=game/'BepInEx/LaevatTools/python'/source.name
  if source.is_file() and not target.is_file():result.append((target,source.read_bytes()))
 source=package/'설치자료/필수효과/update_config420.json';target=game/'BepInEx/LaevatPreview/update_config420.json'
 try:
  shipped=json.loads(source.read_bytes());old=json.loads(target.read_bytes()) if target.is_file() else {}
  # Enable unconfigured legacy installations. Preserve an explicit opt-out from this configured repository.
  if old.get('repository')==shipped['repository'] and old.get('public_key'):
   shipped['enabled']=old.get('enabled',True)
   shipped['installed_sequence']=max(old.get('installed_sequence',0),shipped['installed_sequence'])
  result.append((target,json.dumps(shipped,indent=2).encode()))
 except (OSError,ValueError,KeyError,TypeError):pass
 return result
