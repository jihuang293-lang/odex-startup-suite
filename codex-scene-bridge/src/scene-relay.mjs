import fs from 'node:fs/promises';
import path from 'node:path';
import {pathToFileURL} from 'node:url';

export function validStreamUrl(value) {
  try {const u=new URL(value);return u.protocol==='http:'&&u.hostname==='127.0.0.1'&&Number(u.port)>1023&&!u.username&&!u.password&&!u.search&&!u.hash&&/^\/[a-f0-9]{48}\/stream\.mp4$/.test(u.pathname);} catch{return false;}
}

// This adapter handles one component-owned media URL. All other requests use
// the exact fetch function already installed by the application.
export function installPageAdapter(config) {
  if(globalThis.location?.origin!=='app://-')return false;
  const key='__CODEX_SCENE_BRIDGE_TRANSPORT__';
  let hub=globalThis[key];
  if(!hub) {
    hub={original:globalThis.fetch,entries:new Map()};
    hub.wrapper=function(input,init) {
      let url;try{url=new URL(typeof input==='string'||input instanceof URL?String(input):input.url,globalThis.location.href).href;}catch{return Reflect.apply(hub.original,this,arguments);}
      const entry=hub.entries.get(url);
      const method=String(init?.method??input?.method??'GET').toUpperCase();
      return entry&&method==='GET'?entry.open(input,init):Reflect.apply(hub.original,this,arguments);
    };
    globalThis[key]=hub;globalThis.fetch=hub.wrapper;
  }
  if(hub.entries.get(config.url)?.owner===config.owner)return true;
  const records=new Map();let sequence=0;
  const send=(op,id)=>globalThis[config.binding](JSON.stringify({op,id,owner:config.owner}));
  const entry={owner:config.owner,records,
    open(input,init) {
      if(records.size>=4)return Promise.reject(new Error('Scene stream limit reached'));
      const signal=init?.signal??input?.signal;
      if(signal?.aborted)return Promise.reject(new DOMException('Scene stream aborted','AbortError'));
      const id=String(++sequence);
      const record={controller:null,pending:null,signal,aborted:null};records.set(id,record);
      const release=()=>{records.delete(id);signal?.removeEventListener('abort',record.aborted);if(record.pending){record.pending();record.pending=null;}};
      record.aborted=()=>{try{record.controller?.error(new DOMException('Scene stream aborted','AbortError'));}catch{}send('cancel',id);release();};
      const body=new ReadableStream({
        start(controller){record.controller=controller;send('open',id);},
        pull(){return new Promise(resolve=>{record.pending=resolve;send('pull',id);});},
        cancel(){send('cancel',id);release();}
      },{highWaterMark:131072,size:chunk=>chunk.byteLength});
      record.receive=(base64,done,error)=>{
        if(!records.has(id))return false;
        try {
          if(error){record.controller.error(new Error(error));release();return true;}
          if(done){record.controller.close();release();return true;}
          const raw=atob(base64);const bytes=new Uint8Array(raw.length);
          for(let i=0;i<raw.length;i++)bytes[i]=raw.charCodeAt(i);
          record.controller.enqueue(bytes);
          if(record.pending){const resolve=record.pending;record.pending=null;resolve();}
          return true;
        }catch{release();return false;}
      };
      signal?.addEventListener('abort',record.aborted,{once:true});
      return Promise.resolve(new Response(body,{status:200,headers:{'Content-Type':'video/mp4','Cache-Control':'no-store'}}));
    },
    receive(id,base64,done,error){return records.get(id)?.receive?.(base64,done,error)??false;},
    close(){for(const [id,record] of [...records]){try{record.controller?.error(new Error('Scene component stopped'));}catch{}send('cancel',id);record.signal?.removeEventListener('abort',record.aborted);record.pending?.();records.delete(id);}}
  };
  hub.entries.set(config.url,entry);return true;
}

export function removePageAdapter(config) {
  const key='__CODEX_SCENE_BRIDGE_TRANSPORT__',hub=globalThis[key];
  const entry=hub?.entries.get(config.url);
  if(entry?.owner===config.owner){entry.close();hub.entries.delete(config.url);}
  if(hub&&hub.entries.size===0){if(globalThis.fetch===hub.wrapper)globalThis.fetch=hub.original;delete globalThis[key];}
}

class Session {
  constructor(target,relay){this.target=target;this.relay=relay;this.pending=new Map();this.sequence=0;this.contexts=new Set();this.closed=false;}
  async open() {
    this.socket=new WebSocket(this.target.webSocketDebuggerUrl);
    this.socket.addEventListener('message',e=>this.message(e));
    this.socket.addEventListener('close',()=>{this.closed=true;this.relay.closeSession(this);});
    this.socket.addEventListener('error',()=>{this.closed=true;this.relay.closeSession(this);});
    await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error('Scene relay attach timed out')),4000);this.socket.addEventListener('open',()=>{clearTimeout(timer);resolve();},{once:true});this.socket.addEventListener('error',()=>{clearTimeout(timer);reject(Error('Scene relay connection failed'));},{once:true});});
    await this.send('Runtime.enable');await this.send('Page.enable');
    await this.send('Runtime.addBinding',{name:this.relay.config.binding});
    const source=`(${installPageAdapter.toString()})(${JSON.stringify(this.relay.config)})`;
    this.early=(await this.send('Page.addScriptToEvaluateOnNewDocument',{source})).identifier;
    await this.send('Runtime.evaluate',{expression:source,returnByValue:true});
  }
  message(event) {
    let message;try{message=JSON.parse(event.data);}catch{return;}
    if(message.id){const wait=this.pending.get(message.id);if(wait){clearTimeout(wait.timer);this.pending.delete(message.id);message.error?wait.reject(Error(message.error.message)):wait.resolve(message.result);}return;}
    const p=message.params??{};
    if(message.method==='Runtime.executionContextCreated'&&p.context?.origin==='app://-')this.contexts.add(p.context.id);
    if(message.method==='Runtime.executionContextDestroyed'){this.contexts.delete(p.executionContextId);this.relay.closeContext(this,p.executionContextId);}
    if(message.method==='Runtime.executionContextsCleared'){this.contexts.clear();this.relay.closeSession(this);}
    if(message.method==='Runtime.bindingCalled'&&p.name===this.relay.config.binding)this.relay.binding(this,p).catch(()=>{});
  }
  send(method,params={}) {
    if(this.closed)return Promise.reject(Error('Scene relay session closed'));
    return new Promise((resolve,reject)=>{const id=++this.sequence;const timer=setTimeout(()=>{this.pending.delete(id);reject(Error('Scene relay operation timed out'));},4000);this.pending.set(id,{resolve,reject,timer});this.socket.send(JSON.stringify({id,method,params}));});
  }
  async deliver(record,base64='',done=false,error=null) {
    const expression=`globalThis.__CODEX_SCENE_BRIDGE_TRANSPORT__?.entries.get(${JSON.stringify(this.relay.config.url)})?.receive(${JSON.stringify(record.id)},${JSON.stringify(base64)},${done},${JSON.stringify(error)})??false`;
    const result=await this.send('Runtime.evaluate',{expression,contextId:record.context,returnByValue:true});
    return result?.result?.value===true;
  }
  async close() {
    if(this.closing)return;this.closing=true;this.relay.closeSession(this);
    if(!this.closed) {
      const expression=`(${removePageAdapter.toString()})(${JSON.stringify(this.relay.config)})`;
      await Promise.allSettled([...this.contexts].map(contextId=>this.send('Runtime.evaluate',{expression,contextId,returnByValue:true})));
      if(this.early)await this.send('Page.removeScriptToEvaluateOnNewDocument',{identifier:this.early}).catch(()=>{});
      await this.send('Runtime.removeBinding',{name:this.relay.config.binding}).catch(()=>{});
    }
    for(const wait of this.pending.values()){clearTimeout(wait.timer);wait.reject(Error('Scene relay closed'));}this.pending.clear();
    this.closed=true;try{this.socket.close();}catch{}
  }
}

export class Relay {
  constructor(url,state) {this.state=state;this.config={url,owner:url.split('/')[3],binding:'__codex_scene_'+url.split('/')[3].slice(0,20)};this.sessions=new Map();this.records=new Map();this.stopping=false;}
  async json(resource){const response=await fetch('http://127.0.0.1:9335'+resource,{signal:AbortSignal.timeout(2500),redirect:'error'});if(!response.ok)throw Error('Managed skin endpoint unavailable');return response.json();}
  async scan() {
    const version=await this.json('/json/version');
    if(!version.webSocketDebuggerUrl?.endsWith('/'+this.state.browserId))throw Error('Managed skin browser identity changed');
    const targets=await this.json('/json/list');const eligible=targets.filter(t=>{
      try{const u=new URL(t.webSocketDebuggerUrl);return t.type==='page'&&t.url?.startsWith('app://-/')&&u.protocol==='ws:'&&u.hostname==='127.0.0.1'&&u.port==='9335'&&u.pathname==='/devtools/page/'+t.id&&!u.search&&!u.hash;}catch{return false;}
    }).slice(0,8);
    const live=new Set(eligible.map(t=>t.id));
    for(const [id,session] of this.sessions)if(!live.has(id)||session.closed){await session.close();this.sessions.delete(id);}
    for(const target of eligible)if(!this.sessions.has(target.id)) {
      const session=new Session(target,this);try{await session.open();this.sessions.set(target.id,session);}catch{await session.close();}
    }
    if(!this.sessions.size)throw Error('No managed Codex renderer available for scene transport');
  }
  key(session,context,id){return session.target.id+':'+context+':'+id;}
  closeRecord(record){if(record.closed)return;record.closed=true;this.records.delete(record.key);record.abort.abort();record.reader?.cancel().catch(()=>{});}
  closeContext(session,context){for(const record of this.records.values())if(record.session===session&&record.context===context)this.closeRecord(record);}
  closeSession(session){for(const record of this.records.values())if(record.session===session)this.closeRecord(record);}
  async binding(session,params) {
    if(this.stopping||params.payload?.length>4096)return;
    let message;try{message=JSON.parse(params.payload);}catch{return;}
    if(message.owner!==this.config.owner||!/^\d{1,12}$/.test(message.id)||!Number.isSafeInteger(params.executionContextId))return;
    const key=this.key(session,params.executionContextId,message.id);let record=this.records.get(key);
    if(message.op==='cancel'){if(record)this.closeRecord(record);return;}
    if(message.op==='open') {
      if(record||this.records.size>=4)return;
      record={key,id:message.id,context:params.executionContextId,session,abort:new AbortController(),closed:false,busy:false,pending:null};this.records.set(key,record);
      record.opening=fetch(this.config.url,{redirect:'error',signal:record.abort.signal,cache:'no-store'}).then(response=>{if(!response.ok||!response.body)throw Error('Local scene stream unavailable');record.reader=response.body.getReader();return record.reader;});
      record.opening.catch(async()=>{if(!record.closed){await session.deliver(record,'',false,'Local scene stream unavailable').catch(()=>{});this.closeRecord(record);}});return;
    }
    if(message.op!=='pull'||!record||record.closed)return;
    if(record.busy){record.wantsPull=true;return;}
    record.busy=true;
    try {
      await record.opening;if(record.closed)return;
      let part=record.pending;if(!part){const result=await record.reader.read();if(result.done){await session.deliver(record,'',true);this.closeRecord(record);return;}part=result.value;}
      if(part.byteLength>1024*1024)throw Error('Scene chunk exceeds transport limit');
      const chunk=part.subarray(0,131072);record.pending=part.byteLength>chunk.byteLength?part.subarray(chunk.byteLength):null;
      if(!await session.deliver(record,Buffer.from(chunk).toString('base64')))this.closeRecord(record);
    }catch{if(!record.closed){await session.deliver(record,'',false,'Scene transport interrupted').catch(()=>{});this.closeRecord(record);}}
    finally{record.busy=false;if(record.wantsPull&&!record.closed){record.wantsPull=false;queueMicrotask(()=>this.binding(session,params).catch(()=>{}));}}
  }
  async close(){if(this.stopping)return;this.stopping=true;clearInterval(this.timer);for(const record of this.records.values())this.closeRecord(record);await Promise.allSettled([...this.sessions.values()].map(session=>session.close()));this.sessions.clear();}
}

async function main() {
  if(process.argv.length!==4||process.argv[2]!=='--url'||!validStreamUrl(process.argv[3]))throw Error('A valid component-owned scene stream URL is required');
  const statePath=path.join(process.env.LOCALAPPDATA??'','CodexDreamSkin','state.json');
  if((await fs.stat(statePath)).size>1024*1024)throw Error('Managed skin state is too large');
  const state=JSON.parse(await fs.readFile(statePath,'utf8'));
  if(state.port!==9335||!/^[a-f0-9-]{32,36}$/i.test(state.browserId))throw Error('Unexpected managed skin session');
  const relay=new Relay(process.argv[3],state);let ended=false;
  const close=async(code=0)=>{if(ended)return;ended=true;await relay.close();process.exit(code);};
  process.stdin.setEncoding('utf8');let input='';
  process.stdin.on('data',part=>{input+=part;if(input.length>4096){void close(1);return;}if(input.includes('\n')){try{if(JSON.parse(input.trim()).command==='shutdown')void close();}catch{void close(1);}}});
  process.stdin.on('end',()=>void close());process.on('SIGTERM',()=>void close());process.on('SIGINT',()=>void close());
  try{await relay.scan();console.log('scene-relay: ready');}catch{await relay.close();throw Error('Scene relay could not attach to the managed Codex session');}
  let scanning=false;
  relay.timer=setInterval(async()=>{if(scanning||ended)return;scanning=true;try{await relay.scan();}catch{console.error('scene-relay: managed session ended');await close(1);}finally{scanning=false;}},1000);
}
if(process.argv[1]&&pathToFileURL(path.resolve(process.argv[1])).href===import.meta.url)main().catch(error=>{console.error('scene-relay: '+error.message);process.exit(1);});
