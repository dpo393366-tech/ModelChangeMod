"""Publish a reviewed build with credentials already authorized in Git Credential Manager."""
from pathlib import Path
import argparse,subprocess,os,json,urllib.request,urllib.error,urllib.parse,http.client,time,hashlib,zipfile

def sha(path):
 h=hashlib.sha256()
 with path.open('rb') as f:
  for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
 return h.hexdigest()

def publish(folder,sequence,repository,key):
 root=Path(__file__).resolve().parents[1];folder=Path(folder).resolve();key=Path(key).resolve()
 env=dict(os.environ,GCM_INTERACTIVE='never',GIT_TERMINAL_PROMPT='0')
 gcm='C:/Program Files/Git/mingw64/bin/git-credential-manager-core.exe'
 result=subprocess.run([gcm,'get'],input='protocol=https\nhost=github.com\n\n',text=True,capture_output=True,env=env,timeout=30,check=True)
 fields=dict(x.split('=',1) for x in result.stdout.splitlines() if '=' in x);token=fields.get('password')
 if not token:raise RuntimeError('GitHub authentication is required')
 headers={'Authorization':'Bearer '+token,'User-Agent':'ModelChangeMod-Publisher','Accept':'application/vnd.github+json','X-GitHub-Api-Version':'2022-11-28'}
 def api(path,method='GET',body=None):
  data=None if body is None else json.dumps(body).encode()
  request=urllib.request.Request('https://api.github.com'+path,data=data,method=method,headers={**headers,'Content-Type':'application/json'})
  with urllib.request.urlopen(request,timeout=45) as r:return json.load(r) if r.status!=204 else None
 account=api('/user');details=api('/repos/'+repository)
 if not details.get('permissions',{}).get('push'):raise RuntimeError('Repository push permission missing')
 names=['modelchange-update.json','modelchange-update.sig','modelchange-update.zip','ModelChangeMod-R'+str(sequence)+'-install.zip']
 audit=json.loads((folder/'build-audit.json').read_bytes())
 for name in names:
  f=folder/name;expected=audit['assets'][name]
  if f.stat().st_size!=expected['bytes'] or sha(f)!=expected['sha256']:raise ValueError('Build changed after review: '+name)
 with zipfile.ZipFile(folder/names[-1]) as z:
  listing=z.namelist();original='ModelChangeMod/원본모델_넣는곳/'
  if original not in listing or any(n.startswith(original) and n!=original for n in listing):raise ValueError('Original folder must be empty')
  for n in listing:
   if any(s.lower() in ('_work','__pycache__','original_models','.git','private_publisher458') for s in n.split('/')):raise ValueError('Private directory in installer')
   if Path(n).suffix.lower() in ('.pem','.key','.lvp','.fbx','.dae','.pmx','.pmd','.blend','.obj','.gltf','.glb'):raise ValueError('Source model/private key in installer')
 def git(*args):return subprocess.run(['git','-C',str(root),*args],check=True,env=env,capture_output=True,text=True,encoding='utf-8').stdout.strip()
 git('config','user.name',account['login']);git('config','user.email',str(account['id'])+'+'+account['login']+'@users.noreply.github.com')
 git('add','--all')
 files=git('ls-files','-z').split('\0')
 for name in files:
  if not name:continue
  if name.startswith('원본모델_넣는곳/') and name!='원본모델_넣는곳/.gitkeep':raise ValueError('Original model staged')
  if any(x.lower() in ('_work','__pycache__','work') for x in name.split('/')) or Path(name).suffix.lower() in ('.pem','.key','.zip','.rar','.7z','.lvp','.fbx','.dae','.pmx','.pmd','.blend'):raise ValueError('Forbidden source file staged')
 if git('status','--porcelain'):git('commit','-m','Release R'+str(sequence)+': update mod runtime before game startup')
 commit=git('rev-parse','HEAD');git('push','origin','HEAD:main')
 print('SOURCE_PUSHED '+commit,flush=True)
 path='/repos/'+repository+'/releases';tag='modelchange-'+str(sequence)
 try:release=api(path+'/tags/'+tag)
 except urllib.error.HTTPError as e:
  if e.code!=404:raise
  release=None
 body='R'+str(sequence)+' 정식 배포\n\n기존과 동일하게 설치하기(고사양).bat 또는 설치하기(저사양).bat 하나를 실행하세요. R457 이전 설치는 이번 설치 버튼을 한 번 실행해야 자동 업데이트가 활성화됩니다. 이후에는 게임 시작 전에 모드 코드와 지원 파일의 추가·수정·제거가 반영됩니다.\n\n고사양은 원본 모델 텍스처, 저사양은 지원 모델 텍스처 최대 512px입니다. 두 모드 모두 CG 원본 화질·30fps와 검증된 27개 Live CG의 짧은 반복 구간을 사용합니다.\n\n원본 모델과 _work는 포함하지 않습니다. 원본모델_넣는곳은 빈 폴더이며 원본은 별도로 받아야 합니다. 신규 캐릭터의 코드·지원 자료·변환 정보는 업데이트할 수 있습니다.\n\n파일 누락·사용자 CG 변경·BepInEx 버전 차이만으로 모드 전체를 막는 환경 일치 검사는 사용하지 않습니다. 업데이트 실패 시 기존 설치로 실행합니다.\n\n설치 ZIP 전체를 풀고 게임과 TKEditor를 종료한 뒤 설치하세요. 별도 일반본이나 추가 패치는 없습니다.\n\n검증: 실제 R458 로더의 업데이트 후 런타임 선택, 고사양/저사양 설치 연동, 누적 추가·수정·삭제, 다운로드 실패 및 복원 시험. 이번 배포 과정에서는 실제 게임 플레이 재시험은 하지 않았습니다.'
 if release is None:release=api(path,'POST',{'tag_name':tag,'target_commitish':commit,'name':'ModelChangeMod R'+str(sequence),'body':body,'draft':True,'prerelease':False})
 if not release['draft']:raise RuntimeError('This version is already published; do not replace published files')
 rid=release['id'];assets={a['name']:a for a in api(path+'/'+str(rid)+'/assets')}
 for name in names:
  expected=audit['assets'][name];previous=assets.get(name)
  if previous and previous.get('digest')=='sha256:'+expected['sha256'] and previous['size']==expected['bytes']:
   print('ASSET_ALREADY_COMPLETE '+name,flush=True);continue
  if previous:api(path+'/assets/'+str(previous['id']),'DELETE')
  f=folder/name;url='/repos/'+repository+'/releases/'+str(rid)+'/assets?name='+urllib.parse.quote(name)
  connection=http.client.HTTPSConnection('uploads.github.com',timeout=180)
  try:
   connection.putrequest('POST',url)
   for k,v in {**headers,'Content-Type':'application/octet-stream','Content-Length':str(f.stat().st_size)}.items():connection.putheader(k,v)
   connection.endheaders();sent=0;last=time.monotonic()
   with f.open('rb') as src:
    for chunk in iter(lambda:src.read(1024*1024),b''):
     connection.send(chunk);sent+=len(chunk)
     if time.monotonic()-last>10:print('UPLOAD '+name+' '+str(round(sent/f.stat().st_size*100,1))+'%',flush=True);last=time.monotonic()
   response=connection.getresponse();raw=response.read()
   if response.status!=201:raise RuntimeError('GitHub upload failed: HTTP '+str(response.status))
   asset=json.loads(raw)
   if asset['size']!=expected['bytes'] or asset.get('digest','sha256:'+expected['sha256'])!='sha256:'+expected['sha256']:raise ValueError('Uploaded asset mismatch')
   print('ASSET_UPLOADED '+name+' '+str(asset['size']),flush=True)
  finally:connection.close()
 final_assets={a['name']:a for a in api(path+'/'+str(rid)+'/assets')}
 for name in names:
  if name not in final_assets or final_assets[name]['size']!=audit['assets'][name]['bytes']:raise ValueError('Release asset missing')
 released=api(path+'/'+str(rid),'PATCH',{'draft':False,'make_latest':'true','body':body})
 final_assets={a['name']:a for a in api(path+'/'+str(rid)+'/assets')}
 # Publication is the commit point for the cumulative-update history.
 pending=key.with_name('release_history.pending.json');history=key.with_name('release_history.json')
 if json.loads(pending.read_bytes())['last_sequence']!=sequence:raise ValueError('Publisher history sequence mismatch')
 os.replace(pending,history)
 receipt={'release':released['html_url'],'sequence':sequence,'commit':commit,'assets':{n:{'size':final_assets[n]['size'],'url':final_assets[n]['browser_download_url']} for n in names}}
 (folder/'published.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8');print('PUBLISHED '+json.dumps(receipt),flush=True)

if __name__=='__main__':
 p=argparse.ArgumentParser()
 for name in ('folder','sequence','repository','key'):p.add_argument('--'+name,required=True,type=int if name=='sequence' else str)
 a=p.parse_args();publish(a.folder,a.sequence,a.repository,a.key)
