"""R419 release installer: prepare and verify first; commit with rollback last."""
from pathlib import Path
import argparse,json,sys,os,subprocess,shutil,zipfile,datetime,hashlib
from import_models import prepare,sha,atomic
import transaction
from release_support import customizable,validate_png,loader_conflict
ROOT=Path(__file__).resolve().parents[1]

def game_closed(game):
    if os.name!='nt':return
    # Fail closed if process enumeration itself fails.
    script="[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false); $ErrorActionPreference='Stop'; Get-CimInstance Win32_Process | Where-Object {$_.Name -eq 'ThreeKingdom.exe' -or $_.Name -eq 'LegendOfHeros.exe' -or $_.Name -eq 'TKEditor.exe'} | Select-Object -ExpandProperty ProcessId"
    p=subprocess.run(['powershell.exe','-NoProfile','-NonInteractive','-Command',script],capture_output=True,text=True,encoding='utf-8',errors='replace',timeout=30)
    if p.returncode:raise RuntimeError('Cannot verify game process state: '+p.stderr[-500:])
    if p.stdout.strip():raise RuntimeError('게임과 TKEditor를 종료한 뒤 다시 실행해 주세요.')

def verify_package():
    try:manifest=json.loads((ROOT/'package_manifest.json').read_text(encoding='utf-8-sig'))
    except (OSError,ValueError):
        print('[안내] 파일 목록을 읽지 못해 현재 포함된 설치 자료로 진행합니다.')
        manifest={'files':{f.relative_to(ROOT).as_posix():None for folder in ('payload','설치자료') for f in (ROOT/folder).rglob('*') if f.is_file()},'preserve_existing':[]}
    manifest.setdefault('files',{});manifest.setdefault('preserve_existing',[]);manifest['_invalid_art']=[]
    # Hashes document this release; they are not an installation admission rule.
    for rel in manifest['files']:
        p=transaction.safe(ROOT,rel)
        if customizable(rel) and p.suffix.lower()=='.png' and p.is_file():
            try:validate_png(p)
            except Exception as e:manifest['_invalid_art'].append(rel);print('[CG 건너뜀]',rel,str(e))
    return manifest

def preserve_developer(game):
    old=game/'BepInEx/LaevatPreview';dll=game/'BepInEx/plugins/LaevatPreview';stamp=game/'Laevat_Installer_State/developer_backup.json'
    if stamp.exists() or not old.exists():return
    release=old/'release419_installed.json'
    if release.is_file():
        record=json.loads(release.read_text(encoding='utf-8-sig'))
        if record.get('release')=='R419-Release-Candidate1' and isinstance(record.get('models'),list):
            print('기존 배포판을 갱신합니다. 전체 모델 ZIP 백업은 만들지 않고 변경 파일만 보관합니다.',flush=True)
            return
    dest=game/'Laevat_Development_Archive';dest.mkdir(exist_ok=True)
    path=dest/'ModelChangeMod_BEFORE_INSTALL.zip';tmp=path.with_suffix('.partial')
    if path.exists():
        with zipfile.ZipFile(path) as z:
            records=json.loads(z.read('DEVELOPER_FILES_SHA256.json'))
            for rel,h in records.items():
                if hashlib.sha256(z.read(rel)).hexdigest()!=h:raise ValueError('기존 최초 백업 검증 실패. 원본은 덮어쓰지 않았습니다.')
        atomic(stamp,json.dumps({'archive':str(path),'sha256':sha(path),'files':len(records)},indent=2).encode());return
    sources=[f for folder in (old,dll) if folder.exists() for f in folder.rglob('*') if f.is_file()]
    cfg=game/'BepInEx/config/local.laevat.preview.cfg'
    if cfg.exists():sources.append(cfg)
    needed=sum(f.stat().st_size for f in sources)
    if shutil.disk_usage(game).free<needed+512*1024**2:raise RuntimeError('Not enough space for the developer backup.')
    print('기존 개발 설치를 백업합니다:',path,flush=True)
    records={}
    with zipfile.ZipFile(tmp,'w',zipfile.ZIP_DEFLATED,compresslevel=1,allowZip64=True) as z:
        for index,f in enumerate(sources):
            if index%50==0:print('백업 진행:',index,'/',len(sources),'개 파일',flush=True)
            rel=f.relative_to(game).as_posix();records[rel]=sha(f);z.write(f,rel)
        z.writestr('DEVELOPER_FILES_SHA256.json',json.dumps(records,indent=2))
    with zipfile.ZipFile(tmp) as z:
        for n,h in records.items():
            digest=hashlib.sha256()
            with z.open(n) as src:
                for b in iter(lambda:src.read(1024*1024),b''):digest.update(b)
            if digest.hexdigest()!=h:raise ValueError('Developer backup validation failed')
    os.replace(tmp,path);atomic(stamp,json.dumps({'archive':str(path),'sha256':sha(path),'files':len(records)},indent=2).encode())

def stage_link(src,dest):
    dest.parent.mkdir(parents=True,exist_ok=True)
    if dest.exists():dest.unlink()
    source=str(Path(src).resolve());target=str(Path(dest).resolve())
    if os.name=='nt':
        source=source if source.startswith('\\\\?\\') else '\\\\?\\'+source
        target=target if target.startswith('\\\\?\\') else '\\\\?\\'+target
    try:os.link(source,target)
    except OSError:shutil.copy2(source,target)

def install(game,inputs,prepare_only=False):
    game=game.resolve();manifest=verify_package()
    from update_install458 import protected_paths
    updated_paths=protected_paths(game,ROOT)
    if not (game/'GameAssembly.dll').is_file() or not (game/'ThreeKingdom_Data/il2cpp_data/Metadata/global-metadata.dat').is_file():raise ValueError('Select the LegendOfHeros game folder containing GameAssembly.dll and ThreeKingdom_Data.')
    game_closed(game)
    if prepare_only:
        if (game/'Laevat_Installer_State/pending.json').exists():raise RuntimeError('Interrupted installation requires recovery before prepare-only.')
    else:transaction.clear_stale_lock(game);transaction.recover(game)
    from loader_compat import reusable,is_loader_file
    reuse_loader,loader_notes=reusable(game,ROOT)
    for note in loader_notes:print('[호환성 안내]',note)
    cache=ROOT/'_work/models';stage=ROOT/'_work/stage';stage.mkdir(parents=True,exist_ok=True)
    if any(stage.iterdir()):
        checked=stage.resolve()
        if not checked.is_relative_to((ROOT/'_work').resolve()):raise ValueError('Invalid staging folder')
        target=str(checked)
        if os.name=='nt' and not target.startswith('\\\\?\\'):target='\\\\?\\'+target
        shutil.rmtree(target);stage.mkdir()
    sevenzip=ROOT/'tools/7zip/7z.exe'
    imported=prepare(ROOT,inputs,cache,sevenzip)
    if not imported['models']:print('새로 준비된 모델이 없어 기존 모델을 유지하며 실행 파일과 포함 자료를 갱신합니다.')
    for f in (ROOT/'payload').rglob('*'):
        if f.is_file():
            rel=f.relative_to(ROOT/'payload');dest=game/rel
            if rel.as_posix() in updated_paths:continue
            if 'payload/'+rel.as_posix() in manifest['_invalid_art']:continue
            if customizable('payload/'+rel.as_posix()) and dest.is_file() and sha(dest)!=sha(f):
                print('[사용자 CG 유지]',rel.as_posix());continue
            if reuse_loader and is_loader_file(rel) and dest.exists():continue
            if (rel.as_posix() in manifest['preserve_existing'] or rel.as_posix().startswith('BepInEx/LaevatPreview/calibration/')) and dest.exists():continue
            stage_link(f,stage/rel)
    # Permanent distribution assets are separate from generated model caches.
    for rel in manifest['files']:
        prefix='설치자료/필수효과/'
        if not rel.startswith(prefix) or rel in manifest['_invalid_art']:continue
        game_rel='BepInEx/LaevatPreview/'+rel[len(prefix):]
        if game_rel in updated_paths:continue
        if (game_rel in manifest['preserve_existing'] or game_rel.startswith('BepInEx/LaevatPreview/calibration/')) and (game/game_rel).exists():continue
        if customizable(rel) and (game/game_rel).is_file() and sha(game/game_rel)!=manifest['files'][rel]:
            print('[사용자 CG 유지]',game_rel);continue
        source=transaction.safe(ROOT,rel)
        if not source.is_file():
            print('[일부 기능 안내] 설치 자료가 없습니다. 다른 모델 설치는 계속합니다:',rel);continue
        stage_link(source,transaction.safe(stage,game_rel))
    for rel,expected in imported['file_hashes'].items():
        f=transaction.safe(cache,rel)
        if sha(f)!=expected:raise ValueError('준비 파일이 검증 이후 변경되었습니다. 다시 실행해 주세요: '+rel)
        stage_link(f,stage/'BepInEx/LaevatPreview'/rel)
    for f in (ROOT/'tools/python').iterdir():
        if f.is_file():stage_link(f,stage/'BepInEx/LaevatTools/python'/f.name)
    launcher={'owner':'local.laevat.preview','executable':str(game/'BepInEx/LaevatTools/python/python.exe')}
    atomic(stage/'BepInEx/LaevatPreview/compat166/python.json',json.dumps(launcher).encode())
    previous=game/'BepInEx/LaevatPreview/release419_installed.json'
    old_models=json.loads(previous.read_text(encoding='utf-8-sig')).get('models',[]) if previous.exists() else []
    merged={x['asset']:x for x in old_models};merged.update({x['asset']:x for x in imported['models']})
    atomic(stage/'BepInEx/LaevatPreview/release419_installed.json',json.dumps({'release':'R419-Release-Candidate1','models':list(merged.values())},indent=2).encode())
    from runtime_requirements import validate as validate_runtime
    checks=[];warnings=[]
    try:contract=json.loads((ROOT/'runtime_requirements.json').read_text(encoding='utf-8-sig'))
    except (OSError,ValueError):contract={'files':{}};print('[안내] 부가 자료 목록 없이 설치를 계속합니다.')
    try:checks.append(validate_runtime(stage/'BepInEx/LaevatPreview',[m['asset'] for m in imported['models']],contract,game/'BepInEx/LaevatPreview'))
    except Exception:
        for model in imported['models']:
            try:checks.append(validate_runtime(stage/'BepInEx/LaevatPreview',[model['asset']],contract,game/'BepInEx/LaevatPreview'))
            except Exception as e:
                warnings.append({'model':model['asset'],'reason':str(e)})
                print('[일부 기능 확인 필요]',model['asset'],str(e),'— 나머지 설치는 계속합니다.')
    runtime_check={'checks':checks,'warnings':warnings,'visual_game_test':False}
    imported['runtime_warnings']=warnings
    atomic(ROOT/'실행필수파일_검사결과.txt',('모델별 실행 자료 검사 결과 (warnings 확인)\n'+json.dumps(runtime_check,ensure_ascii=False,indent=2)+'\n파일 검사 결과이며 게임 내 표시 성공을 뜻하지 않습니다.').encode('utf-8-sig'))
    from performance_options429 import stage_mode
    stage_mode(game,stage)
    from update_install458 import changes as update_changes
    for target,data in update_changes(ROOT,game):atomic(stage/target.relative_to(game),data)
    from short_cg456 import stage_short
    stage_short(ROOT,game,stage)
    files={f.relative_to(stage).as_posix():sha(f) for f in stage.rglob('*') if f.is_file()}
    atomic(ROOT/'_work/prepared.json',json.dumps({'game':str(game),'files':files,'models':imported['models'],'unknown':imported['unknown_archives']},indent=2).encode())
    if prepare_only:
        result_summary(imported,game,True)
        print('준비 완료: 게임 파일은 변경하지 않았습니다. 아래 결과를 확인한 뒤 설치하기(고사양).bat 또는 설치하기(저사양).bat을 실행하세요.')
        return
    game_closed(game);preserve_developer(game);game_closed(game)
    receipt=transaction.commit(game,stage,files)
    from short_cg456 import refresh_bindings
    refresh_bindings(ROOT,game)
    atomic(game/'Laevat_Installer_State/last.json',json.dumps(receipt,indent=2).encode())
    try:transaction.prune_history(game)
    except Exception as error:print('설치는 완료됐지만 이전 백업 정리는 보류했습니다:',str(error))
    result_summary(imported,game,False)
    print('파일 설치 완료:',len(imported['models']),'개 모델을 검증했고,',len(receipt['entries']),'개 파일을 갱신했습니다.')
    print('게임의 MOD 관리에서 모델변경 모드 체크를 변경한 뒤 MOD 로드로 적용하세요.')
    print('이 결과는 파일 설치 검사입니다. 실제 모드 시작 여부는 게임 실행 후 CG 선택창에서 확인하세요.')
    print('CG가 없다면 다른 모드를 지우지 말고 문제보고서_모으기.bat으로 실행 상태를 확인하세요.')


def rollback(game):
    # 구버전 --rollback 호출도 이제 모드 제거로 처리합니다.
    from remove_mod import uninstall
    result = uninstall(game)
    if not result['ok']:
        raise RuntimeError('모드 제거 후 남은 파일이 있습니다. 제거결과.txt를 확인해 주세요.')

def result_summary(imported,game,prepare_only):
    lines=['모델 준비 결과' if prepare_only else '게임 설치 결과', '']
    label='준비 성공' if prepare_only else '설치 성공'
    for row in imported.get('success_archives',[]):lines.append('['+label+'] '+row['file']+' → '+', '.join(row['assets']))
    for row in imported.get('runtime_warnings',[]):lines.append('[기능 확인 필요] '+row['model']+' — '+row['reason'])
    for row in imported.get('failed_archives',[]):lines.append('[실패/건너뜀] '+row['file']+' — '+row['reason'])
    for name in imported.get('unknown_archives',[]):lines.append('[미인식/건너뜀] '+name)
    for row in imported.get('unused_archives',[]):lines.append('[원본 안내] '+row['file']+' — '+row['reason'])
    for name in imported.get('retained_cache_models',[]):lines.append('[기존 준비 모델 유지] '+name)
    failures={x.get('archive') for x in imported.get('failed_archives',[])}
    for folder in ('recipes','fbx_recipes','dae_recipes'):
        for path in (ROOT/folder).glob('*.json'):
            rec=json.loads(path.read_text(encoding='utf-8-sig'))
            if rec['archive'] in failures and (game/'BepInEx/LaevatPreview'/rec['asset']/'model.lvp').exists():
                lines.append('[기존 게임 모델 보존] '+rec['asset']+' — 이번 실패로 기존 파일을 삭제하지 않았습니다. 이번 검증 대상에서는 제외될 수 있습니다.')
    if imported.get('failed_archives') or imported.get('unknown_archives'):
        lines += ['', '일부 원본은 건너뛰었습니다. 성공한 모델은 사용할 수 있습니다.' if not prepare_only else '일부 원본은 건너뛰었습니다. 설치하기(고사양).bat 또는 설치하기(저사양).bat은 성공한 모델만 적용합니다.', '실패한 원본을 보완한 뒤 같은 BAT를 다시 실행하세요.']
    lines += ['', '자세한 결과: '+str(ROOT/'원본모델_처리결과.txt'), '기술 보고서 폴더: '+str(ROOT/'_work/import_details')]
    atomic(ROOT/'설치결과.txt', ('\n'.join(lines)+'\n').encode('utf-8-sig'))
    print('\n'.join(lines),flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--game',type=Path);p.add_argument('--inputs',type=Path,default=ROOT/'원본모델_넣는곳');p.add_argument('--prepare-only',action='store_true');p.add_argument('--rollback',action='store_true');a=p.parse_args()
    try:
        print('모델변경 모드 설치 도우미 — 원본별 독립 처리')
        print('게임과 TKEditor를 종료해 주세요. 원본 압축파일 이름은 바꿔도 됩니다.')
        print('원본 ZIP/RAR/7z는 압축을 풀지 않고 원본모델_넣는곳 폴더에 넣어 주세요.')
        if a.game is None:
            from game_location import select_game
            a.game=select_game(ROOT)
        work=ROOT/'_work';work.mkdir(exist_ok=True);lock=work/'runner.lock'
        if lock.exists():
            if transaction.pid_alive(int(lock.read_text(encoding='utf-8-sig'))):raise RuntimeError('다른 설치기가 실행 중입니다. 해당 창을 종료한 뒤 다시 실행해 주세요.')
            lock.unlink()
        fd=os.open(lock,os.O_CREAT|os.O_EXCL|os.O_WRONLY);os.write(fd,str(os.getpid()).encode());os.fsync(fd)
        try:
            if a.rollback:rollback(a.game)
            else:install(a.game,a.inputs,a.prepare_only)
        finally:os.close(fd);lock.unlink(missing_ok=True)
    except Exception as e:
        import traceback
        log=ROOT/'오류상세_문의용.txt'
        try:log.write_text(traceback.format_exc(),encoding='utf-8-sig')
        except OSError:pass
        message=str(e)
        if not any('가'<=c<='힣' for c in message):
            from independent_import import explain
            if 'Package file damaged' in message:message='설치 패키지 파일이 누락되었거나 변경되었습니다. 패키지를 다시 풀거나 수정 패치를 다시 적용해 주세요.'
            elif 'loader differs' in message:message='기존 BepInEx 버전이 이 패키지와 다릅니다. 기존 설치를 백업하고 신규 설치 테스트 절차를 따라 주세요.'
            elif 'Select the LegendOfHeros' in message:message='게임 경로가 올바르지 않습니다. GameAssembly.dll과 ThreeKingdom_Data가 있는 폴더를 선택해 주세요.'
            else:message='설치를 완료하지 못했습니다. 원인을 확인할 수 있도록 아래 보고서를 전달해 주세요.'
        print('\n[설치 중단] '+message)
        print('자세한 오류는 화면 대신 보고서에 저장했습니다: '+str(log))
        print('도움이 필요하면 오류상세_문의용.txt와 원본모델_처리결과.txt를 함께 전달해 주세요.')
        sys.exit(1)
