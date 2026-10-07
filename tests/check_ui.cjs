const fs=require('fs'),vm=require('vm'),assert=require('assert/strict'),path=require('path');
const out=path.resolve(__dirname,'../build/runtime');
const source=fs.readFileSync(path.join(out,'shutdown_timer.hta'),'ascii');
const common=fs.readFileSync(path.join(out,'shutdown_timer_common.js'),'ascii');
const script=source.match(/<script language="javascript">([\s\S]*?)<\/script>/i)[1];
assert(!/[^\x00-\x7f]/.test(source+common));
let count=0;
const NOW=1791040000000;
function make(inputValue='',results=[1116,0],options={}) {
  const calls=[],alerts=[],helpers=[],writes=[],renders=[];
  const input={value:inputValue,focus(){this.focused=true;},select(){this.selected=true;}};
  let job=options.job||null, locked=false;
  const store={read(){return job?{...job}:null;},write(j){if(options.writeFail)throw Error('write failure');job={...j};writes.push({...j});},
    acquire(){if(options.locked)return false;assert(!locked);locked=true;return true;},release(){locked=false;}};
  const timers=new Map();let timerId=0;
  const context={window:{setTimeout(fn,ms){timers.set(++timerId,{fn,ms});return timerId;},clearTimeout(id){timers.delete(id);},close(){throw Error('UI should stay available');}},document:{getElementById:()=>input},alert:m=>alerts.push(m),
    ActiveXObject:function(){throw Error('Test must not access COM');}};
  vm.createContext(context);vm.runInContext(common,context);vm.runInContext(script,context);
  context.getShell=()=>({Run:(...args)=>{calls.push(args);return results.shift();}});
  context.getFSO=()=>({FileExists:()=>!options.missingHelper});context.helperPath=()=> 'C:\\QA\\helper.exe';
  context.getStorage=()=>store;context.clockNow=()=>NOW;
  context.startHelper=mode=>{if(options.helperFail)throw Error('helper failed');helpers.push(mode);};
  context.renderStatus=j=>renders.push(j&&{...j});
  return{context,input,calls,alerts,helpers,writes,renders,read:()=>job,isLocked:()=>locked,timers,runHide(){const timer=Array.from(timers.values()).find(t=>t.ms===2000);assert(timer);timers.clear();timer.fn();}};
}
for(const seconds of [1800,3600,5400,7200,10800]) {
  const t=make();t.context.setShutdown(seconds);
  assert.equal(t.calls[0][0],'shutdown.exe -a');assert.equal(t.calls[1][0],`shutdown.exe -s -t ${seconds}`);
  assert.deepEqual(t.calls[1].slice(1),[0,true]);assert.equal(t.read().status,'scheduled');
  assert.equal(t.read().deadline,NOW+seconds*1000);assert.deepEqual(t.helpers,['--ensure']);t.runHide();assert.deepEqual(t.helpers,['--ensure','--hide']);assert(!t.isLocked());count++;
}
for(const [text,seconds] of [['45',2700],[' 90 ',5400],['001',60],['1',60],['5256000',315360000]]) {
  const t=make(text);t.context.setCustomShutdown();assert.equal(t.calls[1][0],`shutdown.exe -s -t ${seconds}`);assert.equal(t.read().status,'scheduled');count++;
}
for(const text of ['', ' ', '0', '00', '-1', '1.5', 'abc', '1e3', '60 & whoami', '5256001', 'Infinity', '999999999999999999999']) {
  const t=make(text);t.context.setCustomShutdown();assert.equal(t.calls.length,0);assert.equal(t.alerts.length,1);assert.equal(t.input.focused,true);count++;
}
for(const code of [0,1116]) {
  const t=make('',[code]);t.context.cancelShutdown();assert.equal(t.calls.length,1);assert.equal(t.read().status,'cancelled');assert(!t.isLocked());count++;
}
for(const results of [[5],[1116,5]]) {
  const t=make('45',results);t.context.setCustomShutdown();assert(!t.read());assert.equal(t.context.busy,false);assert.equal(t.helpers.length,0);assert(!t.isLocked());count++;
}
const previous={version:'1',token:'100-5',status:'scheduled',started:NOW-10000,deadline:NOW+100000,boot:'',heartbeat:1,warn5:0,warnFinal:0,reason:''};
const replaced=make('45',[0,5],{job:previous});replaced.context.setCustomShutdown();assert.equal(replaced.read().status,'cancelled');assert.equal(replaced.helpers.length,0);count++;
const cancelFail=make('',[5],{job:previous});cancelFail.context.cancelShutdown();assert.equal(cancelFail.read().status,'scheduled');count++;
for(const options of [{missingHelper:true},{locked:true}]) {
  const t=make('45',[1116,0],options);t.context.setCustomShutdown();assert.equal(t.calls.length,0);assert.equal(t.alerts.length,1);count++;
}
const storageFail=make('45',[1116,0,0],{writeFail:true});storageFail.context.setCustomShutdown();assert.deepEqual(storageFail.calls.map(x=>x[0]),['shutdown.exe -a','shutdown.exe -s -t 2700','shutdown.exe -a']);assert(!storageFail.isLocked());assert.equal(storageFail.helpers.length,0);count++;
const helperFail=make('45',[1116,0],{helperFail:true});helperFail.context.setCustomShutdown();assert.equal(helperFail.read().status,'scheduled');assert.equal(helperFail.alerts.length,1);count++;
const enter=make('45');enter.context.customKeyDown({keyCode:13,preventDefault(){}});assert.equal(enter.calls[1][0],'shutdown.exe -s -t 2700');count++;
const cancelDelay=make('45',[1116,0,0]);cancelDelay.context.setCustomShutdown();assert.equal(cancelDelay.timers.size,1);cancelDelay.context.cancelShutdown();assert.equal(cancelDelay.timers.size,0);assert.deepEqual(cancelDelay.helpers,['--ensure']);count++;
const manualHide=make('45');manualHide.context.setCustomShutdown();manualHide.context.minimizeToTray();assert.equal(manualHide.timers.size,0);assert.deepEqual(manualHide.helpers,['--ensure','--hide']);count++;
const recovered=make('45');recovered.context.setCustomShutdown();recovered.context.window.onfocus();assert.equal(recovered.timers.size,0);assert.deepEqual(recovered.helpers,['--ensure']);count++;
const initialize=make();assert.equal(initialize.calls.length,0);count++;
const closeIdle=make();closeIdle.context.window.onunload();assert.deepEqual(closeIdle.helpers,['--exit']);assert.equal(closeIdle.calls.length,0);count++;
const closeActive=make('',[1116],{job:previous});closeActive.context.cachedJob=previous;closeActive.context.window.onunload();assert.deepEqual(closeActive.helpers,['--exit']);assert.equal(closeActive.calls.length,0);assert.equal(closeActive.read().status,'scheduled');count++;
const closeFail=make('',[1116],{job:previous,helperFail:true});closeFail.context.cachedJob=previous;closeFail.context.window.onunload();assert.equal(closeFail.calls.length,0);assert.equal(closeFail.read().status,'scheduled');assert.equal(closeFail.alerts.length,1);count++;
const ctx=initialize.context;
assert.equal(ctx.formatCountdown(5400),'01:30:00');count++;
assert.equal(ctx.formatCountdown(315360000),'87600:00:00');count++;
let j={...previous,started:NOW,deadline:NOW+1800000};
assert.equal(ctx.reminderKey(j,NOW+1499000),'');count++;
assert.equal(ctx.reminderKey(j,NOW+1500000),'warn5');count++;
j.warn5=1;assert.equal(ctx.reminderKey(j,NOW+1600000),'');count++;
assert.equal(ctx.reminderKey(j,NOW+1740000),'warnFinal');count++;
j.warnFinal=1;assert.equal(ctx.reminderKey(j,NOW+1750000),'');count++;
j.warn5=0;j.warnFinal=0;assert.equal(ctx.reminderKey(j,NOW+1740000),'warnFinal');count++;
j.status='cancelled';assert.equal(ctx.reminderKey(j,NOW+1740000),'');count++;
j.status='scheduled';assert.equal(ctx.reminderKey(j,NOW+1800000),'');count++;
j.deadline=NOW+60000;assert.equal(ctx.reminderKey(j,NOW+29000),'');assert.equal(ctx.reminderKey(j,NOW+30000),'warnFinal');count++;
const round=ctx.parseState(ctx.serializeState(previous));assert.equal(round.deadline,previous.deadline);assert.equal(round.status,previous.status);count++;
for(const text of ['bad',ctx.serializeState(previous).replace('version=1','version=2'),ctx.serializeState(previous).replace('token=100-5','token=../bad'),ctx.serializeState(previous).replace('status=scheduled','status=bad')]) {assert.equal(ctx.parseState(text),null);count++;}
console.log(`${count} functional checks passed. Native scheduling, cancellation and helper launching were mocked.`);
fs.writeFileSync(path.resolve(__dirname,'functional-checks.txt'),`${count} checks passed`);
