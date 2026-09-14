const $ = s => document.querySelector(s);
let currentPlan = null;
let currentWorkspace = '';

async function api(path, opts={}) {
  const res = await fetch(path, {headers:{'Content-Type':'application/json', ...(opts.headers||{})}, ...opts});
  const text = await res.text();
  let data = {};
  try { data = text ? JSON.parse(text) : {}; } catch { data = {error:text}; }
  if (!res.ok) throw new Error(data.error || `HTTP ${res.status}`);
  return data;
}
function esc(v=''){return String(v).replace(/[&<>'"]/g,m=>({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[m]));}
function toast(msg){const t=$('#toast');t.textContent=msg;t.classList.remove('hidden');setTimeout(()=>t.classList.add('hidden'),3200)}
function modal(id,show=true){$('#'+id).classList.toggle('hidden',!show)}
function busy(on,msg='جاري العمل...'){document.body.style.cursor=on?'progress':'';$('#statusText').textContent=on?msg:'جاهز.';$('#statusDot').style.background=on?'#d0a24b':'#28a36f'}
function appendMessage(role,text){const div=document.createElement('div');div.className=`message ${role}`;div.innerHTML=`<div class="avatar">${role==='user'?'أنت':'AI'}</div><div class="bubble">${esc(text).replace(/\n/g,'<br>')}</div>`;$('#chat').appendChild(div);$('#chat').scrollTop=$('#chat').scrollHeight}
function setLog(text,append=false){const el=$('#log');el.textContent=append?(el.textContent+'\n\n'+text):text;el.scrollTop=el.scrollHeight}

async function loadStatus(){
  const s=await api('/api/status');currentWorkspace=s.workspace||'';$('#workspaceName').textContent=currentWorkspace?currentWorkspace.split(/[\\/]/).filter(Boolean).pop():'لم يتم اختيار مشروع';$('#workspaceName').title=currentWorkspace;$('#modelName').textContent=s.model;$('#model').value=s.model;$('#approvalRequired').checked=s.approvalRequired;
  if(currentWorkspace) await loadTree();
}
async function loadTree(){
  try{const r=await api('/api/project/tree');$('#tree').innerHTML=r.tree.map(x=>`<div class="tree-item" title="${esc(x.path)}">${esc(x.path)}</div>`).join('')||'<div class="muted">المجلد فارغ.</div>'}catch(e){$('#tree').innerHTML=`<div class="muted">${esc(e.message)}</div>`}
}
function renderPlan(plan){
  currentPlan=plan;$('#planStatus').textContent=plan.status==='done'?'جاهز للتنفيذ':plan.status==='blocked'?'يحتاج معلومات':'خطة مرحلية';
  let html=`<div class="plan-summary"><strong>${esc(plan.summary)}</strong><span>${esc(plan.next||'')}</span></div>`;
  html+=(plan.actions||[]).map((a,i)=>`<div class="action-card"><div class="action-head"><b>الإجراء ${i+1}</b><span class="action-type">${esc(a.type)}</span></div><div class="action-target">${esc(a.path||a.command||'')}</div>${a.reason?`<p>${esc(a.reason)}</p>`:''}</div>`).join('');
  $('#planArea').className='plan-area';$('#planArea').innerHTML=html||'<div class="empty-state">الخطة لا تحتوي إجراءات.</div>';$('#applyBar').classList.toggle('hidden',!plan.actions?.length);
}
function renderApply(result){
  let lines=[`Checkpoint: ${result.checkpoint}`,`Success: ${result.success}`,`Status: ${result.status}`,''];
  for(const l of result.logs||[]) lines.push(`${l.success?'[OK]':'[FAIL]'} ${l.type} ${l.target}\n${l.message}`);
  setLog(lines.join('\n\n'));toast(result.success?'تم تنفيذ الخطة بنجاح':'اكتمل التنفيذ مع أخطاء');
}

$('#openProjectBtn').onclick=()=>modal('pathModal');
$('#confirmOpenBtn').onclick=async()=>{try{busy(true,'جاري تحليل مجلد المشروع...');const r=await api('/api/project/open',{method:'POST',body:JSON.stringify({path:$('#projectPath').value})});currentWorkspace=r.workspace;$('#workspaceName').textContent=currentWorkspace.split(/[\\/]/).filter(Boolean).pop();$('#workspaceName').title=currentWorkspace;modal('pathModal',false);await loadTree();toast('تم فتح المشروع')}catch(e){toast(e.message)}finally{busy(false)}};
$('#stageBtn').onclick=async()=>{try{busy(true,'جاري إنشاء نسخة تطوير آمنة...');const r=await api('/api/project/stage',{method:'POST',body:'{}'});currentWorkspace=r.workspace;$('#workspaceName').textContent=currentWorkspace.split(/[\\/]/).filter(Boolean).pop();$('#workspaceName').title=currentWorkspace;await loadTree();appendMessage('assistant',r.message);toast('تم الانتقال إلى نسخة Staging')}catch(e){toast(e.message)}finally{busy(false)}};

$('#settingsBtn').onclick=()=>modal('settingsModal');
$('#saveSettingsBtn').onclick=async()=>{try{const r=await api('/api/settings',{method:'POST',body:JSON.stringify({apiKey:$('#apiKey').value,model:$('#model').value,baseUrl:$('#baseUrl').value,approvalRequired:$('#approvalRequired').checked})});$('#modelName').textContent=r.model;$('#apiKey').value='';modal('settingsModal',false);toast('تم حفظ الإعدادات للجلسة الحالية')}catch(e){toast(e.message)}};
$('#approvalRequired').onchange=async()=>{try{await api('/api/settings',{method:'POST',body:JSON.stringify({apiKey:'',model:$('#modelName').textContent,baseUrl:$('#baseUrl').value,approvalRequired:$('#approvalRequired').checked})})}catch{}};

$('#planBtn').onclick=async()=>{const msg=$('#prompt').value.trim();if(!msg)return toast('اكتب المهمة أولاً');appendMessage('user',msg);try{busy(true,'الذكاء الاصطناعي يحلل المشروع ويجهز الخطة...');const p=await api('/api/chat',{method:'POST',body:JSON.stringify({message:msg})});renderPlan(p);appendMessage('assistant',`${p.summary}\n${p.next||''}`);setLog('تم إنشاء الخطة. لم يتم تعديل أي ملف بعد.')}catch(e){appendMessage('assistant','تعذر إنشاء الخطة: '+e.message);toast(e.message)}finally{busy(false)}};
$('#applyBtn').onclick=async()=>{if(!currentPlan)return;try{busy(true,'جاري إنشاء نقطة استعادة وتنفيذ الخطة...');const r=await api('/api/apply',{method:'POST',body:JSON.stringify({planId:currentPlan.id})});renderApply(r);await loadTree()}catch(e){setLog('ERROR: '+e.message);toast(e.message)}finally{busy(false)}};
$('#discardBtn').onclick=()=>{currentPlan=null;$('#applyBar').classList.add('hidden');$('#planArea').className='plan-area empty-state';$('#planArea').innerHTML='<div class="empty-icon">⌘</div><strong>تم إلغاء الخطة</strong><span>يمكنك كتابة طلب جديد.</span>';$('#planStatus').textContent='لا توجد خطة'};

$('#autoBtn').onclick=async()=>{const msg=$('#prompt').value.trim();if(!msg)return toast('اكتب المهمة أولاً');if(!confirm('الوضع التلقائي سيطبق التعديلات ويشغّل أوامر البناء المسموحة داخل المشروع. سيتم إنشاء نقاط استعادة. هل تريد المتابعة؟'))return;appendMessage('user',msg);try{busy(true,'جاري التطوير التلقائي واختبار النتيجة...');setLog('بدأت دورة التطوير التلقائي...');const r=await api('/api/auto',{method:'POST',body:JSON.stringify({message:msg,maxCycles:Number($('#cycles').value||3)})});let log=[r.message];for(const c of r.cycles||[]){log.push(`\n=== CYCLE ${c.cycle} ===\n${c.plan.summary}`);for(const l of c.apply.logs||[])log.push(`${l.success?'[OK]':'[FAIL]'} ${l.type} ${l.target}\n${l.message}`)}setLog(log.join('\n'));appendMessage('assistant',r.message);if(r.cycles?.length)renderPlan(r.cycles[r.cycles.length-1].plan);await loadTree();toast(r.success?'اكتملت المهمة':'توقفت بعد الحد الأقصى للدورات')}catch(e){appendMessage('assistant','فشل التطوير التلقائي: '+e.message);setLog('ERROR: '+e.message);toast(e.message)}finally{busy(false)}};

$('#memoryBtn').onclick=async()=>{try{const list=await api('/api/memory');$('#memoryList').innerHTML=list.length?list.slice().reverse().map(x=>`<div class="memory-item"><strong>${esc(x.task)}</strong><p>${esc(x.result)}</p><small>${new Date(x.when).toLocaleString('ar')}</small></div>`).join(''):'لا توجد ذاكرة بعد.';modal('memoryModal')}catch(e){toast(e.message)}};
$('#clearLogBtn').onclick=()=>setLog('تم مسح السجل من الشاشة.');
document.querySelectorAll('[data-close]').forEach(b=>b.onclick=()=>modal(b.dataset.close,false));
document.querySelectorAll('.modal').forEach(m=>m.addEventListener('click',e=>{if(e.target===m)m.classList.add('hidden')}));
$('#prompt').addEventListener('keydown',e=>{if(e.ctrlKey&&e.key==='Enter')$('#planBtn').click()});
loadStatus().catch(e=>toast(e.message));
