"""전체 설치 버튼의 재실행: 모델을 재변환하지 않고 성능 설정만 바꾼다."""
from pathlib import Path
import io,json,zipfile
from performance_options429 import REL,profile_bytes
from mod_removal.safe_update import atomic,commit,recover,lock
from mod_removal.recovery_io import game_closed
from mod_removal.remove_mod import anchored,pid_alive
from extract_resources import extract

DLL='BepInEx/plugins/LaevatPreview/Laevat.Preview.dll'
INPUT_STATE='BepInEx/LaevatPreview/install_inputs430.json'

def input_snapshot(inputs):
    root=Path(inputs)
    if not root.is_dir():return {}
    found={}
    for p in root.rglob('*'):
        if p.is_file() and p.suffix.lower() in ('.zip','.rar','.7z'):
            s=p.stat();found[p.relative_to(root).as_posix()]=[s.st_size,s.st_mtime_ns]
    return found

def new_inputs(game,inputs):
    current=input_snapshot(inputs)
    if not current:return False
    try:previous=json.loads((Path(game)/INPUT_STATE).read_text(encoding='utf-8-sig'))
    except (OSError,ValueError):previous={}
    return any(previous.get(name)!=stamp for name,stamp in current.items())

def remember_inputs(game,inputs):
    atomic(anchored(Path(game),INPUT_STATE),json.dumps(input_snapshot(inputs),ensure_ascii=False).encode('utf-8'))

def supports_profiles(blob):
    # 고사양/저사양 계약은 공통 런타임에 있다. 사용자의 원본/CG 해시는 읽지 않는다.
    with zipfile.ZipFile(io.BytesIO(blob)) as z:
        runtime=z.read('Laevat.Preview.Runtime.dll')
    return b'Performance429\x00' in runtime and 'performance_mode429.json'.encode('utf-16-le') in runtime

def runtime_revision(blob):
    with zipfile.ZipFile(io.BytesIO(blob)) as z:runtime=z.read('Laevat.Preview.Runtime.dll')
    # Public compatibility markers remain across patch releases. No user model hash gate.
    return 442 if b'TextureCache442\x00' in runtime else 441 if b'PortraitFilter441\x00' in runtime else 440 if b'ShaderReuse440\x00' in runtime else 439 if b'LowTexture439\x00' in runtime else 438 if b'ComponentCache438\x00' in runtime else 437 if b'NativeTime437\x00' in runtime else 436 if b'NativeMath436\x00' in runtime else 435 if b'NativeTransforms435\x00' in runtime else 434 if b'OutlineColor434\x00' in runtime else 431 if b'FrameSync431\x00' in runtime else (430 if b'MaterialAccess430\x00' in runtime else (429 if b'Performance429\x00' in runtime else 0))

def diagnostic_runtime(blob):
    with zipfile.ZipFile(io.BytesIO(blob)) as z:runtime=z.read('Laevat.Preview.Runtime.dll')
    return 'runtime_mvid='.encode('utf-16-le') in runtime and 'dropped_or_depth_errors'.encode('utf-16-le') in runtime

def switch_installed(package,game,mode):
    package=Path(package).resolve();game=Path(game).resolve()
    target=anchored(game,DLL)
    if not target.is_file() or not (game/'BepInEx/LaevatPreview').is_dir():return False
    required_loader=('winhttp.dll','BepInEx/core/BepInEx.Core.dll','BepInEx/core/BepInEx.Unity.IL2CPP.dll')
    if any(not (game/rel).is_file() for rel in required_loader):
        print('[로더 보충] 빠진 구성은 전체 설치 경로에서 보충합니다. 기존 로더는 유지합니다.')
        return False
    game_closed(game)
    running=package/'_work/runner.lock'
    if running.is_file():
        try:pid=int(running.read_text(encoding='utf-8-sig'))
        except (OSError,ValueError):pid=0
        if pid_alive(pid):raise ValueError('모델 설치가 진행 중입니다. 설치가 끝난 뒤 성능을 전환해 주세요.')
    # 전체 설치/제거와 같은 잠금을 사용한다. 단일 설정 교체에는 백업을 쌓지 않는다.
    with lock(anchored(game,'Laevat_Installer_State/lock')):
        if (game/'Laevat_Installer_State/pending.json').exists():return False
        state=anchored(game,'ModelChangeMod_Maintenance/performance_upgrade')
        recover(state)
        try:resources=extract(target.read_bytes())
        except Exception:
            print('[안내] 이전 실행 파일 형식이 달라 전체 설치 경로로 갱신합니다.');return False
        import hashlib
        try:
            data=(package/'payload'/DLL).read_bytes();incoming=extract(data)
        except (OSError,ValueError,KeyError):
            print('[안내] 새 실행 파일을 읽지 못해 기존 실행 파일과 성능 설정을 사용합니다.')
            data=target.read_bytes();incoming=resources
        incoming_revision=max(int(json.loads(incoming['Laevat.Compat166.manifest.json']).get('runtime_revision',0)),runtime_revision(incoming['Laevat.Compat166.zip']))
        current_revision=max(int(json.loads(resources['Laevat.Compat166.manifest.json']).get('runtime_revision',0)),runtime_revision(resources['Laevat.Compat166.zip']))
        diagnostics=diagnostic_runtime(resources['Laevat.Compat166.zip'])
        profile_ready=supports_profiles(resources['Laevat.Compat166.zip']) and (current_revision>=incoming_revision or diagnostics)
        if diagnostics and current_revision<incoming_revision:print('[진단본 유지] 성능 설정만 변경합니다. 코드 수정 적용에는 최신 통합패치와 진단본을 사용하세요.')
        updates=anchored(game,'BepInEx/LaevatPreview/updates420')
        with lock(updates/'activate.lock'):
            active_path=updates/'active.json';active=None;active_ready=True
            if active_path.is_file():
                active=json.loads(active_path.read_text(encoding='utf-8-sig'))
                if active.get('sequence',0):
                    runtime=Path(active['runtime']).resolve()
                    if not runtime.is_relative_to((updates/'runtime').resolve()):
                        raise ValueError('자동 업데이트 런타임 경로를 확인할 수 없습니다. 기존 파일을 보존했습니다.')
                    active_blob=(runtime/'Laevat.Compat166.zip').read_bytes()
                    try:active_revision=int(json.loads((runtime/'Laevat.Compat166.manifest.json').read_bytes()).get('runtime_revision',0))
                    except (OSError,ValueError):active_revision=0
                    active_ready=supports_profiles(active_blob) and max(active_revision,runtime_revision(active_blob))>=incoming_revision
            # 현재 배포판보다 새롭거나 같은 자동 갱신본은 보존한다. 이전 공통 코드만 한 번 갱신한다.
            if not profile_ready or not active_ready:
                if not supports_profiles(incoming['Laevat.Compat166.zip']):
                    raise ValueError('이 배포판에 고사양/저사양 공통 런타임이 없습니다. 최신 배포판으로 실행해 주세요.')
                changes=[]
                if not profile_ready:changes.append((target,data))
                if not active_ready:
                    # 이전 공통 런타임은 새 배포판으로 이행한다.
                    # 기존 sequence를 기록하여 이미 적용한 옛 업데이트를 다시 받지 않는다.
                    changes.append((target,data))
                    cfg_path=anchored(game,'BepInEx/LaevatPreview/update_config420.json')
                    cfg=json.loads(cfg_path.read_text(encoding='utf-8-sig')) if cfg_path.is_file() else {}
                    cfg['installed_sequence']=max(int(cfg.get('installed_sequence',0)),int(active['sequence']))
                    changes.extend([(cfg_path,json.dumps(cfg,ensure_ascii=False).encode('utf-8')),
                                    (active_path,json.dumps({'sequence':0,'profile_migration_from':active['sequence']}).encode())])
                # 경로별 한 항목만 커밋하고 이전 플러그인 백업은 한 벌로 제한한다.
                game_closed(game)
                commit(state,list(dict(changes).items()))
                print('이전 설치의 공통 실행 파일을 갱신했습니다. 기존 모델과 사용자 설정은 유지했습니다.')
            game_closed(game)
            from update_install458 import changes as update_changes
            commit(anchored(game,'ModelChangeMod_Maintenance/startup458'),update_changes(package,game))
            from short_cg456 import changes as cg_changes
            commit(anchored(game,'ModelChangeMod_Maintenance/cg456'),cg_changes(package,game)+[(anchored(game,REL),profile_bytes(mode))])
    print('[전환 완료] '+('고사양' if mode=='standard' else '저사양')+' — 다음 게임 실행부터 적용됩니다.')
    print('모델 재변환·재다운로드 없이 설정을 변경했습니다. CG·오라 설정을 유지하고 이후 자동 업데이트도 선택한 성능 설정을 유지합니다.')
    return True
