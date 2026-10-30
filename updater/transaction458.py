"""Atomic mod-owned writes/removals with crash recovery and per-file user preservation."""
from pathlib import Path
import os,json,hashlib,tempfile,shutil,contextlib,re

def digest(data):return hashlib.sha256(data).hexdigest()
def native(path):
 p=str(Path(path).resolve())
 if os.name=='nt' and not p.startswith('\\\\?\\'):p='\\\\?\\UNC\\'+p[2:] if p.startswith('\\\\') else '\\\\?\\'+p
 return Path(p)
def sha(path):
 p=native(path)
 if not p.is_file():return None
 h=hashlib.sha256()
 with p.open('rb') as f:
  for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
 return h.hexdigest()
def atomic(path,data):
 p=native(path);p.parent.mkdir(parents=True,exist_ok=True)
 with tempfile.NamedTemporaryFile(dir=p.parent,delete=False) as f:
  temp=Path(f.name);f.write(data);f.flush();os.fsync(f.fileno())
 try:os.replace(temp,p)
 finally:temp.unlink(missing_ok=True)
def write_value(path,data):
 if not isinstance(data,Path):return atomic(path,data)
 p=native(path);p.parent.mkdir(parents=True,exist_ok=True)
 with tempfile.NamedTemporaryFile(dir=p.parent,delete=False) as f:
  temp=Path(f.name)
  with native(data).open('rb') as source:shutil.copyfileobj(source,f,1024*1024)
  f.flush();os.fsync(f.fileno())
 try:os.replace(temp,p)
 finally:temp.unlink(missing_ok=True)
def value_hash(data):return None if data is None else sha(data) if isinstance(data,Path) else digest(data)
def remove_tree(path,anchor):
 p=Path(path).resolve();root=Path(anchor).resolve()
 if p==root or not p.is_relative_to(root):raise ValueError('Invalid updater temporary folder')
 name=str(p)
 if os.name=='nt' and not name.startswith('\\\\?\\'):name='\\\\?\\'+name
 shutil.rmtree(native(p))
def inside(root,rel):
 root=Path(root).resolve()
 if not isinstance(rel,str) or not rel or '\\' in rel or ':' in rel or rel.startswith('/') or '..' in rel.split('/'):raise ValueError('Invalid update path')
 if any(not s or s.rstrip(' .')!=s or re.match(r'^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)',s,re.I) or any(ord(c)<32 or c in '<>"|?*' for c in s) for s in rel.split('/')):raise ValueError('Invalid Windows path')
 p=(root/rel).resolve()
 if p==root or not p.is_relative_to(root):raise ValueError('Path escapes mod root')
 return p
def read_json(path,default):
 try:return json.loads(Path(path).read_bytes())
 except (OSError,ValueError):return default
def recover(state,explicit=False):
 state=Path(state);record=state/('receipt.json' if explicit else 'pending.json')
 if not record.is_file():return []
 entries=json.loads(record.read_bytes())['entries'];skipped=[]
 # Validate backups before changing any files. Only our own transaction data is checked.
 for e in entries:
  if e['before'] is not None and sha(state/'before'/e['backup'])!=e['before']:raise IOError('Recovery backup unavailable')
 for e in reversed(entries):
  p=native(e['path']);current=sha(p)
  if current not in (e['before'],e['after']):skipped.append(str(p));continue
  if e['before'] is None:p.unlink(missing_ok=True)
  else:write_value(p,state/'before'/e['backup'])
 record.unlink();return skipped
def commit(state,changes,fail_after=None):
 state=Path(state);state.mkdir(parents=True,exist_ok=True);recover(state)
 changes=[(Path(p).resolve(),data) for p,data in changes if sha(p)!=value_hash(data)]
 if not changes:return False
 backup=state/'before'
 if backup.exists():remove_tree(backup,state)
 entries=[]
 for i,(p,data) in enumerate(changes):
  old=sha(p)
  if old is not None:write_value(backup/str(i),p)
  entries.append({'path':str(p),'before':old,'after':value_hash(data),'backup':str(i)})
 journal=json.dumps({'entries':entries}).encode();atomic(state/'pending.json',journal)
 try:
  for i,((p,data),entry) in enumerate(zip(changes,entries),1):
   if sha(p)!=entry['before']:raise IOError('File changed while updating')
   if data is None:native(p).unlink(missing_ok=True)
   else:write_value(p,data)
   if i==fail_after:raise IOError('injected failure')
  for e in entries:
   if sha(e['path'])!=e['after']:raise IOError('Write did not complete')
  atomic(state/'receipt.json',journal);(state/'pending.json').unlink()
 except BaseException:recover(state);raise
 return True
@contextlib.contextmanager
def lock(path):
 # Kernel lock is automatically released on process exit, including a crash.
 path=Path(path);path.parent.mkdir(parents=True,exist_ok=True)
 f=path.open('a+b')
 try:
  if f.tell()==0:f.write(b'0');f.flush()
  f.seek(0)
  if os.name=='nt':
   import msvcrt;msvcrt.locking(f.fileno(),msvcrt.LK_NBLCK,1)
  else:
   import fcntl;fcntl.flock(f.fileno(),fcntl.LOCK_EX|fcntl.LOCK_NB)
  yield
 finally:f.close()
