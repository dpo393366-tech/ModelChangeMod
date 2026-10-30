"""Offline, incremental source import. Preparing does not modify a game folder."""
from pathlib import Path
import argparse,hashlib,json,subprocess,tempfile,zipfile,os,sys
import model_delta
import image_delta


def remove_extracted_tree(path):
    """Remove our temporary extraction tree, including archived read-only files.

    Clear read-only flags only on the failed temporary entry. Retry sharing
    violations briefly; permanent failures remain visible, never ignored.
    """
    import errno, shutil, stat, time
    path = Path(path)
    def unlock_and_retry(function, name, exc_info):
        error = exc_info[1]
        if not isinstance(error, OSError):
            raise error
        if isinstance(error, FileNotFoundError):
            return
        if error.errno not in {errno.EACCES, errno.EPERM, errno.EBUSY} and getattr(error, 'winerror', None) not in {5, 32, 33}:
            raise error
        os.chmod(name, os.stat(name).st_mode | stat.S_IWUSR)
        function(name)
    delays = (0, 0.1, 0.25, 0.5, 1.0)
    for attempt, delay in enumerate(delays):
        if delay:
            time.sleep(delay)
        try:
            if path.exists():
                shutil.rmtree(path, onerror=unlock_and_retry)
            return
        except OSError as error:
            transient = error.errno in {errno.EACCES, errno.EPERM, errno.EBUSY} or getattr(error, 'winerror', None) in {5, 32, 33}
            if not transient or attempt == len(delays) - 1:
                raise RuntimeError('Temporary extraction cleanup failed after read-only repair/retry: ' + str(path) + '; ' + str(error)) from error

def sha(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
    return h.hexdigest()

def atomic(path,data):
    value=str(Path(path).resolve())
    if os.name=='nt' and not value.startswith('\\\\?\\'):value='\\\\?\\UNC\\'+value[2:] if value.startswith('\\\\') else '\\\\?\\'+value
    path=Path(value)
    path.parent.mkdir(parents=True,exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent,delete=False) as f:
        name=Path(f.name);f.write(data);f.flush();os.fsync(f.fileno())
    os.replace(name,path)

def read_member(path,member,max_size=512*1024*1024,sevenzip=None):
    if zipfile.is_zipfile(path):
        with zipfile.ZipFile(path) as z:
            infos=[i for i in z.infolist() if i.filename==member]
            if len(infos)!=1 or infos[0].file_size>max_size:raise ValueError('Ambiguous/oversized source entry')
            return z.read(infos[0])
    # 7-Zip writes into a pipe, never extracts archive-controlled paths to disk.
    if os.name!='nt':
        from catalog_sources import archive_members
        for name,size,blocks in archive_members(path):
            if name==member:
                if size>max_size:raise ValueError('Oversized source entry')
                return b''.join(blocks)
        raise ValueError('Source entry missing')
    if not sevenzip or not Path(sevenzip).is_file():raise ValueError('Bundled 7-Zip is required for RAR/7z')
    with tempfile.TemporaryFile() as f:
        run=subprocess.run([str(sevenzip),'e','-so','-y',str(path),'--',member],stdout=f,stderr=subprocess.PIPE,timeout=180)
        if run.returncode:raise ValueError('Archive extraction failed: '+run.stderr.decode(errors='replace')[-500:])
        size=f.tell()
        if size>max_size:raise ValueError('Oversized source entry')
        f.seek(0);return f.read()

def identify(package,inputs,sevenzip=None):
    catalog=json.loads((package/'source_catalog.json').read_text(encoding="utf-8-sig"))
    known={a['sha256']:a for a in catalog};available={};unknown=[]
    for path in sorted(inputs.iterdir()):
        if not path.is_file() or path.suffix.lower() not in {'.zip','.rar','.7z'}:continue
        h=sha(path)
        if h in known:available[known[h]['archive']]=path;continue
        # Repacked ZIPs are identified by the PMX/FBX/DAE content, not filename.
        if zipfile.is_zipfile(path):
            with zipfile.ZipFile(path) as z:
                hashes={hashlib.sha256(z.read(x)).hexdigest() for x in z.infolist() if Path(x.filename).suffix.lower() in {'.pmx','.fbx','.dae'} and x.file_size<150*1024*1024}
                matched=[a for a in catalog if any(f.get('model_cache') and f['sha256'] in hashes for f in a['files'])]
                # A repack must retain canonical member paths. No texture guessing.
                names=set(z.namelist())
                for a in matched:
                    if all(f['member'] in names for f in a['files'] if f.get('model_cache')):available[a['archive']]=path
                if matched:continue
        unknown.append(path.name)
    return available,unknown

def _prepare_group(package,inputs,stage,sevenzip=None,identified=None):
    available,unknown=identified if identified is not None else identify(package,inputs,sevenzip)
    recipes=[(f,json.loads(f.read_text(encoding="utf-8-sig"))) for folder in ('recipes','fbx_recipes','dae_recipes') for f in sorted((package/folder).glob('*.json'))]
    textures=[(f,json.loads(f.read_text(encoding="utf-8-sig"))) for f in sorted((package/'texture_recipes').glob('*.json'))]
    direct=json.loads((package/'direct_imports.json').read_text(encoding="utf-8-sig"))
    result={'models':[],'unknown_archives':unknown,'missing_archives':[],'mode':'prepare-only','game_modified':False}
    selected=[(p,r) for p,r in recipes if r['archive'] in available]
    # Each source archive is scanned once; solid archives are not reopened per texture.
    wanted={}
    def need(archive,member,h):wanted.setdefault(archive,{})[member]=h
    for rp,rec in selected:
        need(rec['archive'],rec['source_member'],rec['source_sha256'])
        for tp,tr in textures:
            if tr['asset']==rec['asset'] and tr['format']!='solid-color':need(tr['archive'],tr['member'],tr['source_sha256'])
    chosen=[]
    for rec in direct:
        src=next((x for x in rec['sources'] if x['archive'] in available),None)
        if src:chosen.append((rec,src));need(src['archive'],src['member'],src['sha256'])
    stage.mkdir(parents=True,exist_ok=True)
    old=json.loads((stage/'import_receipt.json').read_text(encoding="utf-8-sig")) if (stage/'import_receipt.json').exists() else {}
    previous=old.get('file_hashes',{});recipe_hashes={};old_recipe_hashes=old.get('recipe_hashes',{})
    def unchanged(rel,fingerprint=None):
        f=stage/rel
        return (fingerprint is None or old_recipe_hashes.get(rel)==fingerprint) and rel in previous and f.is_file() and sha(f)==previous[rel]
    with tempfile.TemporaryDirectory(prefix='laevat-import-') as td:
        td=Path(td);cache={}
        for archive,members in wanted.items():
            src=available[archive]
            if os.name=='nt' and not zipfile.is_zipfile(src):
                # Only trusted recipe member paths are extracted, after path validation.
                ext=td/'extract';ext.mkdir(exist_ok=True)
                for member in members:
                    if Path(member).is_absolute() or '..' in Path(member.replace('\\','/')).parts or ':' in member:raise ValueError('Unsafe archive member')
                cmd=[str(sevenzip),'x','-y','-o'+str(ext),str(src),'--',*members]
                run=subprocess.run(cmd,stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=300)
                if run.returncode:raise ValueError('Archive extraction failed: '+archive)
                stream=((member,(ext/member).stat().st_size,[(ext/member).read_bytes()]) for member in members)
            else:
                from catalog_sources import archive_members
                stream=archive_members(src)
            for member,size,blocks in stream:
                if member not in members:continue
                if size>512*1024*1024:raise ValueError('Oversized source')
                data=b''.join(blocks);h=hashlib.sha256(data).hexdigest()
                if h!=members[member]:raise ValueError('Source member changed: '+archive+'/'+member)
                out=td/(h+Path(member).suffix.lower());out.write_bytes(data);cache[(archive,member)]=out
            missing=set(members)-{m for a,m in cache if a==archive}
            if missing:raise ValueError('Source members missing: '+str(missing))
            if os.name=='nt' and not zipfile.is_zipfile(src):
                remove_extracted_tree(ext)
            print('Source verified:',archive,flush=True)
        for rp,rec in selected:
            dest=stage/rec['asset']/'model.lvp';reused=dest.is_file() and sha(dest)==rec['target_sha256']
            if not reused:
                src=cache[(rec['archive'],rec['source_member'])]
                if rec['format']=='R419-PMX-XOR-1':raw=model_delta.apply(src,rec,rp.with_suffix('.delta'))
                else:
                    rebuilt=td/'rebuilt.lvp';subprocess.run([sys.executable,'-X','utf8',str(Path(__file__).with_name('mesh_rebuild.py')),str(src),str(rp),str(rebuilt)],check=True,timeout=300,capture_output=True,text=True,encoding='utf-8',errors='replace');raw=rebuilt.read_bytes()
                atomic(dest,raw)
            for tp,tr in textures:
                if tr['asset']!=rec['asset']:continue
                rel=rec['asset']+'/'+tr['output'];output=stage/rel
                if not output.resolve().is_relative_to(stage.resolve()):raise ValueError('Unsafe recipe path')
                fingerprint=sha(tp);recipe_hashes[rel]=fingerprint
                if unchanged(rel,fingerprint):continue
                if tr['format']=='solid-color':
                    from PIL import Image
                    image=Image.new('RGBA',tuple(tr['size']),tuple(tr['rgba']))
                else:image=image_delta.apply(cache[(tr['archive'],tr['member'])].read_bytes(),tr,tp.with_suffix('.delta').read_bytes())
                import io
                b=io.BytesIO();image.convert(tr.get('output_mode','RGBA')).save(b,format='PNG');atomic(output,b.getvalue())
            result['models'].append({'asset':rec['asset'],'model_sha256':rec['target_sha256'],'reused_model':reused})
            print(rec['asset'],'verified (reused)' if reused else 'verified',flush=True)
        for rec,src in chosen:
            fingerprint=hashlib.sha256(json.dumps(rec,sort_keys=True).encode()).hexdigest();recipe_hashes[rec['output']]=fingerprint
            if unchanged(rec['output'],fingerprint):continue
            data=cache[(src['archive'],src['member'])].read_bytes()
            if rec.get('pixels_sha256'):
                if image_delta.sha(image_delta.pixels(data,rec['size']))!=rec['pixels_sha256']:raise ValueError('Shared texture pixel mismatch')
            atomic(stage/rec['output'],data)
    import shutil
    for f in (package/'설치자료/모델보정').rglob('*'):
        if f.is_file():atomic(stage/f.relative_to(package/'설치자료/모델보정'),f.read_bytes())
    required=json.loads((package/'required_textures.json').read_text(encoding="utf-8-sig"))
    for _,rec in recipes:
        if not (stage/rec['asset']/'model.lvp').exists():continue
        if sha(stage/rec['asset']/'model.lvp')!=rec['target_sha256']:raise ValueError('Cached model version mismatch: '+rec['asset'])
        if not any(x['asset']==rec['asset'] for x in result['models']):result['models'].append({'asset':rec['asset'],'model_sha256':rec['target_sha256'],'reused_model':True})
        for rel in required.get(rec['asset'],[]):
            if not (stage/rel).is_file():raise ValueError('Required shader texture missing for '+rec['asset']+': '+rel)
        for rel in model_textures(stage/rec['asset']/'model.lvp'):
            if rel and not (stage/rec['asset']/rel).is_file():raise ValueError('Model texture missing: '+rec['asset']+'/'+rel)
    lin=stage/'lin_pianpian'
    if (lin/'model.lvp').exists():
        import lin_cache
        expected=json.loads((package/'lin_cache_expected.json').read_text(encoding='utf-8'))
        for f in lin.glob('tex_*.png'):
            raw=lin_cache.build(f)
            if hashlib.sha256(raw[80:]).hexdigest()!=expected[f.stem]:raise ValueError('LIN cache pixel mismatch')
            atomic(lin/'texture_cache_r123'/(f.stem+'.raw'),raw)
    result['missing_archives']=sorted({r['archive'] for _,r in recipes if r['archive'] not in available})
    result['recipe_hashes']={**old_recipe_hashes,**recipe_hashes}
    result['file_hashes']={f.relative_to(stage).as_posix():sha(f) for f in stage.rglob('*') if f.is_file() and f.name!='import_receipt.json'}
    atomic(stage/'import_receipt.json',json.dumps(result,ensure_ascii=False,indent=2).encode())
    return result

def model_textures(path):
    import struct
    b=path.read_bytes();magic,nv,ni,nm,nb=struct.unpack_from('<4s4i',b);assert magic==b'LVP1'
    pos=20+64*nv+4*ni;result=[]
    for _ in range(nm):
        n=struct.unpack_from('<i',b,pos)[0];pos+=4+n
        n=struct.unpack_from('<i',b,pos)[0];pos+=4;result.append(b[pos:pos+n].decode());pos+=n+28
    return result

def prepare(package,inputs,stage,sevenzip=None):
    from independent_import import prepare_independent
    return prepare_independent(package,inputs,stage,sevenzip)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--package',type=Path,default=Path(__file__).resolve().parents[1]);p.add_argument('--inputs',type=Path,required=True);p.add_argument('--stage',type=Path,required=True);p.add_argument('--sevenzip',type=Path);a=p.parse_args()
    result=prepare(a.package,a.inputs,a.stage,a.sevenzip)
    print('Prepared',len(result['models']),'models; unknown archives:',result['unknown_archives'])
