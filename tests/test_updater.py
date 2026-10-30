from pathlib import Path
import base64,hashlib,io,json,sys,tempfile,unittest,zipfile,time,os
from cryptography.hazmat.primitives.asymmetric import rsa,padding
from cryptography.hazmat.primitives import hashes
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'updater'))
import auto_update as u
from transaction458 import atomic,commit,recover,digest,sha

class Updates(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  cls.key=rsa.generate_private_key(public_exponent=65537,key_size=3072);n=cls.key.public_key().public_numbers();cls.public={'n':format(n.n,'x'),'e':n.e}
 def setUp(self):
  base=Path.cwd()/'work'/'tests458';base.mkdir(parents=True,exist_ok=True)
  location=('\\\\?\\'+str(base.resolve())) if os.name=='nt' else str(base)
  self.temp=tempfile.TemporaryDirectory(dir=location);self.game=Path(self.temp.name);self.root=u.root_for(self.game);self.root.mkdir(parents=True)
  self.cfg={'enabled':True,'repository':'dpo393366-tech/ModelChangeMod','installed_sequence':458,'public_key':self.public}
  atomic(self.game/'BepInEx/LaevatPreview/update_config420.json',json.dumps(self.cfg).encode())
 def tearDown(self):self.temp.cleanup()
 def bundle(self,seq,files=None,before=None,remove=None):
  files={'runtime/Laevat.Compat166.zip':('runtime-'+str(seq)).encode(),'runtime/Laevat.Compat166.manifest.json':b'{}',**(files or {})}
  data=io.BytesIO()
  with zipfile.ZipFile(data,'w') as z:
   for name,blob in files.items():z.writestr(name,blob)
  blob=data.getvalue();m={'schema':2,'product':'ModelChangeMod','sequence':seq,'tag':'modelchange-'+str(seq),'zip_sha256':digest(blob),'files':{k:digest(v) for k,v in files.items()},'before':before or {},'remove':remove or []}
  raw=json.dumps(m).encode();sig=base64.b64encode(self.key.sign(raw,padding.PKCS1v15(),hashes.SHA256()));return raw,sig,blob
 def pending(self,bundle):
  raw,sig,blob=bundle;p=self.root/'pending458';p.mkdir(exist_ok=True)
  for name,data in [('manifest.json',raw),('manifest.sig',sig),('update.zip',blob)]:atomic(p/name,data)
 def test_add_modify_remove_and_keep_custom(self):
  old='BepInEx/LaevatPreview/test/old.json';edit='BepInEx/LaevatPreview/test/edit.json';art='BepInEx/LaevatPreview/Test_CG.png';new='BepInEx/LaevatPreview/newchar/new.json'
  for rel,data in [(old,b'old'),(edit,b'v1'),(art,b'custom')]:atomic(self.game/rel,data)
  self.pending(self.bundle(459,{edit:b'v2',new:b'new',art:b'default'},{old:[digest(b'old')],edit:[digest(b'v1')],art:[digest(b'previous default')]},[old]))
  result=u.boot(self.game,network=False)
  self.assertEqual(result['sequence'],459);self.assertFalse((self.game/old).exists());self.assertEqual((self.game/edit).read_bytes(),b'v2');self.assertEqual((self.game/new).read_bytes(),b'new');self.assertEqual((self.game/art).read_bytes(),b'custom')
 def test_same_start_network_download_and_activation(self):
  bundle=self.bundle(459,{'BepInEx/LaevatPreview/newchar/info.json':b'{"new":true}'})
  old=u.urllib.request.urlopen
  class Response(io.BytesIO):
   def geturl(self):return 'https://github.com/test'
  def urlopen(request,**kwargs):
   url=request.full_url
   return Response(bundle[0] if url.endswith('.json') else bundle[1] if url.endswith('.sig') else bundle[2])
  try:
   u.urllib.request.urlopen=urlopen;active=u.boot(self.game)
   self.assertEqual(active['sequence'],459);self.assertEqual(Path(active['runtime'],'Laevat.Compat166.zip').read_bytes(),b'runtime-459')
   self.assertTrue((self.game/'BepInEx/LaevatPreview/newchar/info.json').exists())
  finally:u.urllib.request.urlopen=old
 def test_offline_continues_existing(self):
  self.pending(self.bundle(459));u.boot(self.game,network=False);old=u.fetch
  try:
   u.fetch=lambda *_:(_ for _ in ()).throw(TimeoutError('offline'));self.assertEqual(u.boot(self.game)['sequence'],459)
  finally:u.fetch=old
 def test_missing_optional_existing_files_do_not_block(self):
  self.pending(self.bundle(459,{'BepInEx/LaevatPreview/missing/add.json':b'x'},remove=['BepInEx/LaevatPreview/missing/remove.json']))
  self.assertEqual(u.boot(self.game,network=False)['sequence'],459)
 def test_signature_corruption_keeps_existing(self):
  self.pending(self.bundle(459));(self.root/'pending458/manifest.sig').write_bytes(base64.b64encode(b'bad'))
  self.assertEqual(u.boot(self.game,network=False)['sequence'],0)
 def test_incomplete_download_keeps_existing(self):
  self.pending(self.bundle(459));(self.root/'pending458/update.zip').write_bytes(b'truncated')
  self.assertEqual(u.boot(self.game,network=False)['sequence'],0)
 def test_rollback_restores_deleted_and_modified_and_removes_added(self):
  a=self.game/'a';b=self.game/'b';c=self.game/'c';a.write_bytes(b'a');b.write_bytes(b'b')
  with self.assertRaises(IOError):commit(self.root/'test_tx',[(a,None),(b,b'changed'),(c,b'new')],fail_after=3)
  self.assertEqual(a.read_bytes(),b'a');self.assertEqual(b.read_bytes(),b'b');self.assertFalse(c.exists())
 def test_failed_runtime_rolls_back_next_start(self):
  rel='BepInEx/LaevatPreview/test.json';rel='BepInEx/LaevatPreview/test/data.json';atomic(self.game/rel,b'v1')
  self.pending(self.bundle(459,{rel:b'v2'},{rel:[digest(b'v1')]}));u.boot(self.game,network=False)
  atomic(self.game/'BepInEx/LaevatPreview/runtime_start420.json',json.dumps({'state':'failed','utc':u.datetime.datetime.now(u.datetime.timezone.utc).isoformat()}).encode())
  self.assertEqual(u.boot(self.game,network=False)['sequence'],0);self.assertEqual((self.game/rel).read_bytes(),b'v1')
 def test_user_edit_after_update_survives_rollback(self):
  p=self.game/'custom';p.write_bytes(b'old');commit(self.root/'tx',[(p,b'new')]);p.write_bytes(b'user');recover(self.root/'tx',explicit=True);self.assertEqual(p.read_bytes(),b'user')
 def test_protected_paths(self):
  for path in ['../escape','BepInEx/LaevatPreview/../../plugins/other.dll','BepInEx/LaevatPreview/a/model.lvp','BepInEx/LaevatPreview/calibration/user.json','BepInEx/core/loader.dll','BepInEx/LaevatPreview/new/source.zip','BepInEx/LaevatTools/model_catalog458/model.lvp']:
   self.assertFalse(u.allowed(path),path)
  for path in ['BepInEx/LaevatPreview/new/sidecar.json','BepInEx/LaevatPreview/animatedcg/new.cgframes','BepInEx/LaevatTools/model_catalog458/recipes/new.json']:self.assertTrue(u.allowed(path),path)
 def test_new_model_without_original(self):
  rel='BepInEx/LaevatTools/model_catalog458/recipes/new.json';self.pending(self.bundle(459,{rel:b'{"requires_original":true}'}));u.boot(self.game,network=False)
  self.assertTrue((self.game/rel).exists());self.assertEqual(list(self.game.rglob('*.lvp')),[])
 def test_long_windows_update_paths(self):
  if os.name=='nt':self.game=Path(str(self.game).removeprefix('\\\\?\\'));self.root=u.root_for(self.game)
  rel='BepInEx/LaevatTools/model_catalog458/texture_recipes/'+('character_'*10)+'.png.delta'
  self.pending(self.bundle(459,{rel:b'long recipe'}));self.assertEqual(u.boot(self.game,network=False)['sequence'],459)
  self.assertEqual(sha(self.game/rel),digest(b'long recipe'))
  self.pending(self.bundle(460,{},before={rel:[digest(b'long recipe')]},remove=[rel]))
  self.assertEqual(u.boot(self.game,network=False)['sequence'],460);self.assertIsNone(sha(self.game/rel))
if __name__=='__main__':unittest.main(verbosity=2)
