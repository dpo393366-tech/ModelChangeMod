"""Check, download and activate a signed release before the game loads the mod runtime."""
from pathlib import Path
import argparse,base64,datetime,hashlib,json,os,re,sys,time,urllib.request,urllib.error,zipfile,shutil
sys.path.insert(0,str(Path(__file__).resolve().parent))
from transaction458 import atomic,sha,digest,inside,commit,recover,lock,read_json,write_value,remove_tree
SCHEMA=2;MAX_ZIP=2*1024**3;MAX_FILES=15000
RUNTIME=('runtime/Laevat.Compat166.zip','runtime/Laevat.Compat166.manifest.json')
def root_for(game):return Path(game)/'BepInEx/LaevatPreview/updates420'
def config(game):return read_json(Path(game)/'BepInEx/LaevatPreview/update_config420.json',{})
def status(root,state,detail=''):
 atomic(root/'status.json',json.dumps({'utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'state':state,'detail':str(detail)[:3000]},ensure_ascii=False).encode())
def signed(raw,sig,cfg):
 key=cfg['public_key'];n=int(key['n'],16);e=int(key['e']);k=(n.bit_length()+7)//8
 if not 384<=k<=512 or e!=65537 or len(sig)!=k or int.from_bytes(sig,'big')>=n:raise ValueError('Invalid signature')
 di=bytes.fromhex('3031300d060960864801650304020105000420')+hashlib.sha256(raw).digest()
 if pow(int.from_bytes(sig,'big'),e,n).to_bytes(k,'big')!=b'\0\1'+b'\xff'*(k-len(di)-3)+b'\0'+di:raise ValueError('Publisher signature mismatch')
 m=json.loads(raw)
 if m.get('schema')!=SCHEMA or m.get('product')!='ModelChangeMod' or not isinstance(m.get('sequence'),int) or m['sequence']<1 or not re.fullmatch(r'[A-Za-z0-9._-]{1,80}',m.get('tag','')):raise ValueError('Invalid release manifest')
 return m
def allowed(rel):
 try:inside(Path.cwd(),rel)
 except (ValueError,TypeError):return False
 parts=rel.split('/');low=[p.lower() for p in parts]
 if any(p in ('_work','__pycache__','original_models','원본모델_넣는곳','.git') for p in low):return False
 if rel in RUNTIME:return True
 if rel in ('BepInEx/LaevatTools/auto_update.py','BepInEx/LaevatTools/transaction458.py'):return True
 if rel.startswith('Mods/ModelChangeMod/'):return Path(rel).suffix.lower() in ('.json','.txt','.png','.csv')
 if rel.startswith('BepInEx/LaevatTools/model_catalog458/'):
  sub=parts[3:]
  return bool(sub) and Path(rel).suffix.lower() in ('.json','.txt','.png','.bin','.delta') and Path(rel).name.lower() not in ('model.lvp','model.bin')
 if not rel.startswith('BepInEx/LaevatPreview/'):return False
 sub=parts[2:]
 if not sub or sub[0].lower() in ('updates420','compat166','calibration','aura_calibration','calibration223','precision','logs'):return False
 if any(p.lower() in ('model.lvp','model.bin') for p in sub):return False
 if len(sub)==1:return sub[0].endswith('_CG.png') or sub[0] in ('character_catalog458.json','model_sources458.txt')
 return Path(rel).suffix.lower() in ('.json','.png','.bin','.bundle','.cgframes','.shader','.txt')
def fetch(url,limit,timeout=5):
 request=urllib.request.Request(url,headers={'User-Agent':'ModelChangeMod-Updater/458','Cache-Control':'no-cache'})
 with urllib.request.urlopen(request,timeout=timeout) as f:
  if not f.geturl().startswith('https://'):raise ValueError('HTTPS required')
  data=f.read(limit+1)
  if len(data)>limit:raise ValueError('Response too large')
  return data
def verify_zip(path,m,dest=None):
 files=m.get('files',{});remove=m.get('remove',[])
 if not 2<=len(files)<=MAX_FILES or not all(k in files for k in RUNTIME):raise ValueError('Missing runtime payload')
 if len(remove)>MAX_FILES or len(set(x.casefold() for x in remove))!=len(remove) or {x.casefold() for x in files}&{x.casefold() for x in remove}:raise ValueError('Invalid removals')
 if any(not allowed(x) or x.startswith('runtime/') for x in remove):raise ValueError('Disallowed removal')
 if Path(path).stat().st_size>MAX_ZIP or sha(path)!=m['zip_sha256']:raise ValueError('Incomplete update download')
 with zipfile.ZipFile(path) as z:
  names=z.namelist()
  if len(set(n.casefold() for n in names))!=len(names) or set(names)!=set(files) or sum(i.file_size for i in z.infolist())>3*1024**3:raise ValueError('Invalid ZIP listing')
  for info in z.infolist():
   if not allowed(info.filename) or (info.external_attr>>16)&0o170000==0o120000 or info.file_size>256*1024**2:raise ValueError('Disallowed payload')
   data=z.read(info.filename)
   if digest(data)!=files[info.filename]:raise ValueError('Incomplete payload: '+info.filename)
   if dest is not None:atomic(inside(dest,info.filename),data)
def active_info(root):
 value=read_json(root/'active.json',{'sequence':0})
 return value if isinstance(value,dict) and isinstance(value.get('sequence'),int) else {'sequence':0}
def runtime_valid(root,active):
 if not active.get('sequence'):return True
 try:
  folder=Path(active['runtime']).resolve()
  if not folder.is_relative_to((root/'runtime').resolve()):return False
  return all(sha(folder/Path(rel).name)==active['hashes'][Path(rel).name] for rel in RUNTIME)
 except (OSError,KeyError,ValueError):return False
def repair(game):
 root=root_for(game);recover(root/'transaction458')
 active=active_info(root);attempt=read_json(root/'boot_attempt.json',{});runtime_status=read_json(Path(game)/'BepInEx/LaevatPreview/runtime_start420.json',{})
 failed=False
 try:failed=active['sequence'] and attempt.get('sequence')==active['sequence'] and runtime_status.get('state')=='failed' and datetime.datetime.fromisoformat(runtime_status['utc'].replace('Z','+00:00')).timestamp()>attempt['epoch']
 except (ValueError,KeyError,TypeError):pass
 if failed or not runtime_valid(root,active):
  recover(root/'transaction458',explicit=True)
  atomic(root/'rejected.json',json.dumps({'sequence':active['sequence']}).encode())
  if not runtime_valid(root,active_info(root)):atomic(root/'active.json',b'{"sequence":0}')
  status(root,'restored_previous_version',active['sequence'])
 return active_info(root)
def download(game):
 cfg=config(game);repo=cfg.get('repository','');root=root_for(game)
 if not cfg.get('enabled') or not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+',repo):return False
 base='https://github.com/'+repo+'/releases/'
 raw=fetch(base+'latest/download/modelchange-update.json',4*1024**2);sig=base64.b64decode(fetch(base+'latest/download/modelchange-update.sig',2048),validate=True);m=signed(raw,sig,cfg)
 current=max(active_info(root)['sequence'],int(cfg.get('installed_sequence',0)))
 if m['sequence']<=current:status(root,'up_to_date',current);return False
 if m['sequence']==read_json(root/'rejected.json',{}).get('sequence'):status(root,'kept_working_version','A newer corrected release will be checked next start.');return False
 pending=root/'pending458'
 if pending.is_dir():
  try:
   old=signed((pending/'manifest.json').read_bytes(),base64.b64decode((pending/'manifest.sig').read_bytes()),cfg)
   if old['sequence']==m['sequence'] and sha(pending/'update.zip')==m['zip_sha256']:return True
  except Exception:pass
 temp=root/'download458.partial';start=time.monotonic();total=0;h=hashlib.sha256();status(root,'downloading',m['tag'])
 request=urllib.request.Request(base+'download/'+m['tag']+'/modelchange-update.zip',headers={'User-Agent':'ModelChangeMod-Updater/458'})
 with urllib.request.urlopen(request,timeout=10) as response,temp.open('wb') as f:
  if not response.geturl().startswith('https://'):raise ValueError('HTTPS required')
  while True:
   chunk=response.read(1024*1024)
   if not chunk:break
   total+=len(chunk)
   if total>MAX_ZIP or time.monotonic()-start>180:raise TimeoutError('Update download deferred')
   h.update(chunk);f.write(chunk)
 if h.hexdigest()!=m['zip_sha256']:raise ValueError('Incomplete download')
 verify_zip(temp,m)
 if pending.exists():remove_tree(pending,root)
 pending.mkdir();os.replace(temp,pending/'update.zip');atomic(pending/'manifest.json',raw);atomic(pending/'manifest.sig',base64.b64encode(sig));return True
def activate(game):
 game=Path(game).resolve();root=root_for(game);cfg=config(game);old=active_info(root);pending=root/'pending458'
 if not pending.is_dir():return old
 raw=(pending/'manifest.json').read_bytes();m=signed(raw,base64.b64decode((pending/'manifest.sig').read_bytes()),cfg)
 if m['sequence']<=max(old['sequence'],int(cfg.get('installed_sequence',0))):remove_tree(pending,root);return old
 stage=root/'stage458'
 if stage.exists():remove_tree(stage,root)
 verify_zip(pending/'update.zip',m,stage);changes=[];skipped=[];runtime=root/'runtime'/str(m['sequence']);managed=read_json(root/'managed458.json',{});removed=set(read_json(root/'removed458.json',[]))
 for rel,h in m['files'].items():
  if rel.startswith('runtime/'):write_value(runtime/Path(rel).name,stage/rel);continue
  target=inside(game,rel);actual=sha(target)
  known=set(m.get('before',{}).get(rel,[]));previous=managed.get(rel)
  if previous:known.add(previous)
  # Preserve custom art/settings and unknown local changes without blocking other updates.
  if actual is not None and actual!=h and actual not in known and rel not in ('BepInEx/LaevatTools/auto_update.py','BepInEx/LaevatTools/transaction458.py','BepInEx/LaevatPreview/character_catalog458.json','BepInEx/LaevatPreview/model_sources458.txt') and not rel.startswith('BepInEx/LaevatTools/model_catalog458/'):
   skipped.append(rel);continue
  changes.append((target,stage/rel));managed[rel]=h;removed.discard(rel)
 for rel in m.get('remove',[]):
  target=inside(game,rel);actual=sha(target);known=set(m.get('before',{}).get(rel,[]))
  if managed.get(rel):known.add(managed[rel])
  if actual is None:managed.pop(rel,None);removed.add(rel);continue
  if actual not in known:skipped.append(rel);continue
  changes.append((target,None));managed.pop(rel,None);removed.add(rel)
 active={'sequence':m['sequence'],'runtime':str(runtime),'hashes':{Path(k).name:v for k,v in m['files'].items() if k.startswith('runtime/')}}
 changes.extend([(root/'managed458.json',json.dumps(managed).encode()),(root/'removed458.json',json.dumps(sorted(removed)).encode()),(root/'active.json',json.dumps(active).encode())])
 status(root,'applying',m['tag']);commit(root/'transaction458',changes)
 remove_tree(stage,root);remove_tree(pending,root);status(root,'applied',m['tag']+'; preserved user changes: '+str(skipped))
 # Original model data is never an update/removal target. Unused runtime caches can be retained safely.
 return active
def boot(game,network=True):
 game=Path(game).resolve();root=root_for(game);root.mkdir(parents=True,exist_ok=True)
 with lock(root/'boot458.lock'):
  current=repair(game)
  if network:
   try:download(game)
   except Exception as e:status(root,'offline_or_download_failed_using_existing',type(e).__name__+': '+str(e))
  if config(game).get('enabled'):
   try:current=activate(game)
   except Exception as e:
    recover(root/'transaction458');current=active_info(root);status(root,'update_failed_using_existing',e)
    failed=root/'failed458';pending=root/'pending458'
    if pending.exists():
     if failed.exists():remove_tree(failed,root)
     os.replace(pending,failed)
  atomic(root/'boot_attempt.json',json.dumps({'sequence':current['sequence'],'epoch':time.time()}).encode());return current
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('action',choices=['boot','repair','activate','download']);p.add_argument('--game',type=Path,required=True);a=p.parse_args()
 try:
  if a.action=='repair':
   with lock(root_for(a.game)/'boot458.lock'):repair(a.game)
  else:boot(a.game,network=a.action in ('boot','download'))
 except Exception as e:
  try:status(root_for(a.game),'updater_unavailable_using_existing',e)
  except Exception:pass
  sys.exit(1)
