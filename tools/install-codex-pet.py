import json,queue,shutil,subprocess,threading,time,tomllib
from pathlib import Path

root=Path(__file__).resolve().parents[1]
codex=Path.home()/'.codex'
source=root/'output/codex-pet'
destination=codex/'pets/hudi-higgsfield'
destination.mkdir(parents=True,exist_ok=True)
for name in ['pet.json','spritesheet.png']:shutil.copy2(source/name,destination/name)
config=codex/'config.toml'
previous=tomllib.loads(config.read_text('utf-8')).get('desktop',{}).get('selected-avatar-id')
exe=Path.home()/'AppData/Local/Programs/OpenAI/Codex/bin/codex.exe'
proc=subprocess.Popen([str(exe),'app-server','--stdio'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True,encoding='utf-8',creationflags=subprocess.CREATE_NO_WINDOW)
messages=queue.Queue()
def read():
 for line in proc.stdout:
  try:messages.put(json.loads(line))
  except json.JSONDecodeError:pass
threading.Thread(target=read,daemon=True).start()
def call(ident,method,params):
 proc.stdin.write(json.dumps({'id':ident,'method':method,'params':params})+'\n');proc.stdin.flush()
 deadline=time.monotonic()+30
 while time.monotonic()<deadline:
  message=messages.get(timeout=max(.1,deadline-time.monotonic()))
  if message.get('id')==ident:
   if 'error'in message:raise RuntimeError(message['error'])
   return message.get('result')
 raise TimeoutError(method)
try:
 call(1,'initialize',{'clientInfo':{'name':'hudi_pet_install','version':'1.0'},'capabilities':{'explicitGatewayOauth':True}})
 proc.stdin.write(json.dumps({'method':'initialized','params':{}})+'\n');proc.stdin.flush()
 result=call(2,'config/value/write',{'keyPath':'desktop.selected-avatar-id','value':'custom:hudi-higgsfield','mergeStrategy':'upsert'})
 selected=tomllib.loads(config.read_text('utf-8'))['desktop']['selected-avatar-id']
 report={'success':selected=='custom:hudi-higgsfield','id':selected,'directory':str(destination),'previousSelection':previous,'configResult':result}
 (source/'installation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps(report,ensure_ascii=False))
finally:
 proc.stdin.close()
 try:proc.wait(timeout=3)
 except subprocess.TimeoutExpired:proc.terminate();proc.wait(timeout=3)
