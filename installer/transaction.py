"""Install only fully verified payloads; recover interrupted commits on restart."""
from pathlib import Path
import json,os,hashlib,shutil,uuid,re
from import_models import atomic,sha

def safe(root,rel):
    p=root/rel
    if not p.resolve().is_relative_to(root.resolve()) or p.resolve()==root.resolve():raise ValueError('Unsafe install path')
    return p

def pid_alive(pid):
    if pid<=0:return False
    if os.name=='nt':
        import ctypes
        k=ctypes.windll.kernel32;k.OpenProcess.restype=ctypes.c_void_p
        h=k.OpenProcess(0x1000,False,pid)
        if not h:return k.GetLastError()==5
        k.CloseHandle(ctypes.c_void_p(h));return True
    try:os.kill(pid,0);return True
    except ProcessLookupError:return False
    except PermissionError:return True

def clear_stale_lock(game):
    lock=game/'Laevat_Installer_State/lock'
    if not lock.exists():return
    try:pid=int(lock.read_text(encoding="utf-8-sig"))
    except Exception:raise RuntimeError('Installer lock is unreadable; preserve it for review')
    if pid_alive(pid):raise RuntimeError('Another installer is running')
    lock.unlink()

def commit(game,stage,files,fail_after=None):
    game=game.resolve();stage=stage.resolve();work=game/'Laevat_Installer_State';work.mkdir(exist_ok=True)
    lock=work/'lock'
    clear_stale_lock(game)
    fd=os.open(lock,os.O_CREAT|os.O_EXCL|os.O_WRONLY)
    os.write(fd,str(os.getpid()).encode());os.fsync(fd)
    try:
        if (work/'pending.json').exists():raise RuntimeError('Interrupted install must be recovered first')
        for rel,expected in files.items():
            src=safe(stage,rel)
            if not src.is_file() or sha(src)!=expected:raise ValueError('Payload hash mismatch: '+rel)
        tx=work/uuid.uuid4().hex;tx.mkdir();entries=[]
        for rel,expected in files.items():
            dest=safe(game,rel);before=sha(dest) if dest.is_file() else None
            if before==expected:continue
            if dest.exists() and not dest.is_file():raise ValueError('Destination is not a file')
            backup=safe(tx/'before',rel)
            if before is not None:backup.parent.mkdir(parents=True,exist_ok=True);atomic(backup,dest.read_bytes())
            entries.append({'path':rel,'before':before,'after':expected})
        previous=work/'last.json'
        if not entries and previous.is_file():
            old=json.loads(previous.read_text(encoding='utf-8-sig'))
            if old.get('complete') and safe(game,old['transaction']).is_dir():
                tx.rmdir();return old
        journal={'transaction':str(tx.relative_to(game)),'entries':entries,'complete':False}
        atomic(tx/'receipt.json',json.dumps(journal).encode())
        atomic(work/'pending.json',json.dumps(journal).encode())
        try:
            for i,e in enumerate(entries):
                dest=safe(game,e['path']);src=safe(stage,e['path'])
                if (sha(dest) if dest.is_file() else None)!=e['before']:raise RuntimeError('Concurrent modification: '+e['path'])
                atomic(dest,src.read_bytes())
                if fail_after is not None and i+1==fail_after:raise RuntimeError('Injected interruption')
            journal['complete']=True;atomic(tx/'receipt.json',json.dumps(journal,indent=2).encode());(work/'pending.json').unlink()
        except BaseException:
            recover(game);raise
        return journal
    finally:os.close(fd);lock.unlink(missing_ok=True)

def recover(game):
    game=game.resolve();pending=game/'Laevat_Installer_State/pending.json'
    if not pending.exists():return
    d=json.loads(pending.read_text(encoding="utf-8-sig"));tx=safe(game,d['transaction'])
    if (game/'BepInEx/LaevatPreview/updates420/active.json').exists():raise RuntimeError('자동 업데이트가 적용된 설치입니다. 혼합 버전 복원을 막기 위해 중단했습니다. 문제보고서를 전달해 주세요.')
    # Validate every collision before restoring anything.
    for e in d['entries']:
        dest=safe(game,e['path']);h=sha(dest) if dest.is_file() else None
        if h not in {e['before'],e['after']} and not known_release_patch(e['path'], e['after'], h):raise RuntimeError('직접 수정되었거나 확인되지 않은 파일을 보존했습니다: '+e['path'])
        if e['before'] is not None and sha(safe(tx/'before',e['path']))!=e['before']:raise RuntimeError('Backup hash mismatch')
    for e in reversed(d['entries']):
        dest=safe(game,e['path'])
        if e['before'] is None:dest.unlink(missing_ok=True)
        else:atomic(dest,safe(tx/'before',e['path']).read_bytes())
    pending.unlink()


def prune_history(game):
    """Keep latest committed rollback; only remove recognized installer transactions."""
    game=Path(game).resolve();work=game/'Laevat_Installer_State'
    if (work/'pending.json').exists() or not (work/'last.json').is_file():return
    last=json.loads((work/'last.json').read_text(encoding='utf-8-sig'))
    keep=safe(game,last['transaction'])
    if not last.get('complete') or not (keep/'receipt.json').is_file():return
    # The current rollback must still be usable before any older backup is removed.
    for e in last['entries']:
        if e['before'] is not None and sha(safe(keep/'before',e['path']))!=e['before']:
            raise RuntimeError('최신 복구본 검증에 실패하여 이전 백업을 보존했습니다.')
    for old in work.iterdir():
        if old==keep or old.is_symlink() or not old.is_dir() or not re.fullmatch(r'[0-9a-f]{32}',old.name):continue
        receipt=old/'receipt.json'
        if not receipt.is_file():continue
        try:
            row=json.loads(receipt.read_text(encoding='utf-8-sig'))
            if row.get('transaction')!=old.relative_to(game).as_posix() or not isinstance(row.get('entries'),list):continue
            for e in row['entries']:safe(old/'before',e['path'])
            if any(x.name not in {'before','receipt.json'} for x in old.iterdir()):continue
            shutil.rmtree(old)
        except (OSError,ValueError,KeyError,TypeError):
            print('이전 복구본 일부를 정리하지 못했습니다. 다음 설치 때 다시 시도합니다:',old)


def known_release_patch(rel, installed_hash, current_hash):
    # 경로와 기존 설치 해시, 현재 패치 해시를 모두 대조합니다.
    p=Path(__file__).with_name('rollback_known.json')
    if not p.is_file():return False
    known=json.loads(p.read_text(encoding='utf-8-sig')).get(rel,[])
    return bool(installed_hash and current_hash and installed_hash in known and current_hash in known)
