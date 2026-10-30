"""Build a public installer and cumulative runtime update. Source models are never collected."""
from pathlib import Path
import argparse,base64,hashlib,json,sys,zipfile,shutil
from cryptography.hazmat.primitives import serialization,hashes
from cryptography.hazmat.primitives.asymmetric import padding
HERE=Path(__file__).resolve().parents[1];sys.path.insert(0,str(HERE/'updater'))
from auto_update import allowed,signed,verify_zip,RUNTIME
from transaction458 import sha,digest,inside,atomic
DIRS={'설치자료','payload','scripts','recipes','dae_recipes','fbx_recipes','texture_recipes','tools','ThirdParty','진단도구'}
ROOT_FILES={'모드 제거.bat','문제보고서_모으기.bat','설치전_점검.bat','설치하기(고사양).bat','설치하기(저사양).bat','사용자환경_대응.txt','지원모델_목록.txt','포함도구_라이선스안내.txt','direct_imports.json','lin_cache_expected.json','loops456.json','required_textures.json','runtime_requirements.json','source_catalog.json','catalog_version458.json'}
CATALOG_ROOT={'source_catalog.json','direct_imports.json','lin_cache_expected.json','required_textures.json','runtime_requirements.json','지원모델_목록.txt'}
CATALOG_DIRS={'recipes','dae_recipes','fbx_recipes','texture_recipes'}
FORBIDDEN_PARTS={'_work','__pycache__','.git','원본모델_넣는곳','original_models','개발참고'}
SOURCE_EXT={'.fbx','.dae','.pmx','.pmd','.blend','.lvp','.obj','.gltf','.glb'}
def public_files(package):
 result={}
 for f in Path(package).rglob('*'):
  if not f.is_file():continue
  rel=f.relative_to(package).as_posix();parts=rel.split('/')
  if any(x.lower() in FORBIDDEN_PARTS for x in parts) or f.suffix.lower() in SOURCE_EXT|{'.pyc','.log','.pem','.key'}:continue
  if parts[0] not in DIRS and rel not in ROOT_FILES:continue
  if f.suffix.lower() in ('.zip','.rar','.7z') and rel!='tools/python/python312.zip':raise ValueError('Unreviewed archive in public package: '+rel)
  if f.name.lower() in ('model.bin','model.lvp','release_history.json'):raise ValueError('Model/private state in public package')
  result[rel]=f
 return result
def catalog_files(files):return {rel:p for rel,p in files.items() if rel in CATALOG_ROOT or rel.split('/')[0] in CATALOG_DIRS}
def inventory(files):
 result={}
 for rel,path in files.items():
  if rel.startswith('설치자료/필수효과/'):target='BepInEx/LaevatPreview/'+rel[len('설치자료/필수효과/'):]
  elif rel.startswith('payload/'):target=rel[len('payload/'):]
  else:continue
  if allowed(target):result[target]=path
 return result
def build(package,repo,sequence,key_path,output):
 package=Path(package).resolve();key_path=Path(key_path).resolve();output=Path(output).resolve()
 if key_path.is_relative_to(package) or key_path.is_relative_to(HERE) or output.is_relative_to(package):raise ValueError('Private key and build output must be outside public sources')
 key=serialization.load_pem_private_key(key_path.read_bytes(),password=None);numbers=key.public_key().public_numbers();public={'n':format(numbers.n,'x'),'e':numbers.e}
 history_path=key_path.with_name('release_history.json');history=json.loads(history_path.read_bytes()) if history_path.exists() else {'last_sequence':0,'baseline':{},'known':{}}
 if sequence<=history['last_sequence']:raise ValueError('Release sequence must increase')
 files=public_files(package)
 # Regenerate model metadata from the current package on every publication, including future characters.
 generated=output/('_metadata_'+str(sequence));generated.mkdir(parents=True,exist_ok=True)
 catalog=catalog_files(files);prefix='payload/BepInEx/LaevatTools/model_catalog458/'
 files={k:v for k,v in files.items() if not k.startswith(prefix)}
 for rel,path in catalog.items():files[prefix+rel]=path
 def generated_file(rel,value):
  f=generated/(str(len(list(generated.iterdir())))+'.json');f.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8');files[rel]=f
 generated_file(prefix+'catalog_manifest458.json',{'sequence':sequence,'files':{rel:sha(p) for rel,p in catalog.items()}})
 generated_file('catalog_version458.json',{'sequence':sequence,'files':{rel:sha(p) for rel,p in catalog.items()},'complete':True})
 generated_file('설치자료/필수효과/character_catalog458.json',{'sequence':sequence,'original_models_included':False,'models':sorted(json.loads(files['required_textures.json'].read_bytes())),'download_list':'model_sources458.txt'})
 files['설치자료/필수효과/model_sources458.txt']=files['지원모델_목록.txt']
 current_files=inventory(files);current={rel:sha(p) for rel,p in current_files.items()}
 # The package carries signed-update public configuration; no private signing material is embedded.
 cfg={'enabled':True,'repository':repo,'installed_sequence':sequence,'public_key':public,'check_at_game_start':True}
 cfg_rel='설치자료/필수효과/update_config420.json';config_bytes=json.dumps(cfg,indent=2).encode()
 sys.path.insert(0,str(package/'scripts'));from extract_resources import extract
 resources=extract(files['payload/BepInEx/plugins/LaevatPreview/Laevat.Preview.dll'].read_bytes())
 updates={'runtime/'+name:data for name,data in resources.items() if 'runtime/'+name in RUNTIME}
 always={'BepInEx/LaevatTools/auto_update.py','BepInEx/LaevatTools/transaction458.py'}
 for rel,p in current_files.items():
  if rel in always or rel.startswith('BepInEx/LaevatTools/model_catalog458/') or (history['last_sequence'] and history['baseline'].get(rel)!=current[rel]):updates[rel]=p.read_bytes()
 if not all('BepInEx/LaevatTools/model_catalog458/'+rel in updates for rel in catalog):raise ValueError('Model conversion catalog is incomplete in update payload')
 current_case={k.casefold() for k in current}
 removed=sorted(k for k in history['known'] if k.casefold() not in current_case)
 before={rel:history['known'].get(rel,[]) for rel in set(updates)|set(removed) if not rel.startswith('runtime/')}
 dest=output/('modelchange-'+str(sequence));dest.mkdir(parents=True,exist_ok=True)
 update_zip=dest/'modelchange-update.zip'
 with zipfile.ZipFile(update_zip,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
  for rel,data in updates.items():z.writestr(rel,data)
 manifest={'schema':2,'product':'ModelChangeMod','sequence':sequence,'tag':dest.name,'zip_sha256':sha(update_zip),'files':{rel:digest(data) for rel,data in updates.items()},'before':before,'remove':removed}
 raw=json.dumps(manifest,sort_keys=True,separators=(',',':')).encode();sig=key.sign(raw,padding.PKCS1v15(),hashes.SHA256());signed(raw,sig,cfg);verify_zip(update_zip,manifest)
 atomic(dest/'modelchange-update.json',raw);atomic(dest/'modelchange-update.sig',base64.b64encode(sig))
 original_manifest=json.loads((package/'package_manifest.json').read_bytes());original_manifest['release']='R'+str(sequence)+'-Patched';original_manifest['files']={rel:sha(f) for rel,f in files.items()};original_manifest['files'][cfg_rel]=digest(config_bytes)
 install_zip=dest/('ModelChangeMod-R'+str(sequence)+'-install.zip')
 with zipfile.ZipFile(install_zip,'w',zipfile.ZIP_DEFLATED,compresslevel=6,allowZip64=True) as z:
  for rel,f in files.items():
   if rel==cfg_rel:z.writestr('ModelChangeMod/'+rel,config_bytes)
   else:z.write(f,'ModelChangeMod/'+rel)
  if cfg_rel not in files:z.writestr('ModelChangeMod/'+cfg_rel,config_bytes)
  z.writestr('ModelChangeMod/package_manifest.json',json.dumps(original_manifest,ensure_ascii=False,indent=2))
  z.writestr('ModelChangeMod/원본모델_넣는곳/',b'')
 if install_zip.stat().st_size>=2*1024**3:raise ValueError('Installer exceeds GitHub asset size limit')
 if not history['last_sequence']:history['baseline']=current
 for rel,h in current.items():history['known'][rel]=sorted(set(history['known'].get(rel,[]))|{h})
 history['last_sequence']=sequence
 # Caller promotes this history only after successful publication.
 atomic(key_path.with_name('release_history.pending.json'),json.dumps(history,indent=2).encode())
 audit={'sequence':sequence,'files':len(files),'original_model_files':0,'work_files':0,'empty_original_folder':True,'update_entries':len(updates),'removals':len(removed),'assets':{f.name:{'bytes':f.stat().st_size,'sha256':sha(f)} for f in dest.iterdir() if f.is_file() and f.name!='build-audit.json'}}
 atomic(dest/'build-audit.json',json.dumps(audit,indent=2).encode());print(json.dumps(audit,ensure_ascii=False));return dest
if __name__=='__main__':
 p=argparse.ArgumentParser()
 for name in ('package','repo','sequence','key','output'):p.add_argument('--'+name,required=True,type=int if name=='sequence' else str)
 a=p.parse_args();build(a.package,a.repo,a.sequence,a.key,a.output)
