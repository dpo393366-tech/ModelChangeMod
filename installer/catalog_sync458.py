"""Reuse game-delivered model recipes when the existing installer imports new originals."""
from pathlib import Path
import json,shutil,os
ROOTS={'source_catalog.json','direct_imports.json','lin_cache_expected.json','required_textures.json','runtime_requirements.json','지원모델_목록.txt'}
DIRS={'recipes','dae_recipes','fbx_recipes','texture_recipes'}
def sync(package,game):
 package=Path(package).resolve();source=Path(game)/'BepInEx/LaevatTools/model_catalog458'
 try:
  manifest=json.loads((source/'catalog_manifest458.json').read_bytes())
  local=json.loads((package/'catalog_version458.json').read_bytes()) if (package/'catalog_version458.json').is_file() else {'sequence':458}
  if manifest['sequence']<=local.get('sequence',0) and local.get('complete',True):return
 except (OSError,ValueError,KeyError,TypeError):return
 count=0
 for rel,old_hash in local.get('files',{}).items():
  if rel in manifest.get('files',{}) or '\\' in rel or ':' in rel or '..' in rel.split('/'):continue
  if rel not in ROOTS and rel.split('/')[0] not in DIRS:continue
  target=(package/rel).resolve()
  if not target.is_relative_to(package):continue
  try:
   import hashlib
   if target.is_file() and hashlib.sha256(target.read_bytes()).hexdigest()==old_hash:target.unlink()
  except OSError:pass
 for rel in manifest.get('files',{}):
  if not isinstance(rel,str) or '\\' in rel or ':' in rel or '..' in rel.split('/'):continue
  if rel not in ROOTS and rel.split('/')[0] not in DIRS:continue
  target=(package/rel).resolve()
  if not target.is_relative_to(package):continue
  try:
   if not (source/rel).is_file():continue
   target.parent.mkdir(parents=True,exist_ok=True)
   src=str((source/rel).resolve());dst=str(target)
   if os.name=='nt':
    src=src if src.startswith('\\\\?\\') else '\\\\?\\'+src
    dst=dst if dst.startswith('\\\\?\\') else '\\\\?\\'+dst
   shutil.copy2(src,dst);count+=1
  except OSError:continue
 if count:
  try:(package/'catalog_version458.json').write_text(json.dumps({'sequence':manifest['sequence'],'files':manifest.get('files',{}),'complete':count==len(manifest.get('files',{}))}),encoding='utf-8')
  except OSError:pass
  print('[신규 모델 정보 갱신]',count,'개 자료. 원본 모델은 별도로 내려받아 넣어 주세요.')
