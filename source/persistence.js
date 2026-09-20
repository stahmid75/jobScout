  // Local persistence. The executable exposes only fixed read routes and meta.md writes.
  let appRevision = '', savedEntries = {}, deletedKeys = new Set(), ready = false;
  let saveTimer, saving = false, generation = 0, savedGeneration = 0;
  const appToken = '__APP_TOKEN__';
  const marker = '<!-- jobs-dashboard-state-v1 -->';
  function snapshotEntries() {
    const result = {...savedEntries};
    rows.forEach(row => {
      const edits = {};
      Object.keys(row.data).forEach(k => { if (row.data[k] !== row.baseData[k]) edits[k] = row.data[k]; });
      result[row.sourceKey] = {preferred:row.preferred, applied:row.applied, note:row.note,
        removed:row.removed, deleted:false, edits,
        jobId:row.baseData['Job ID'] || '—', link:row.baseData.Link || '—'};
    });
    deletedKeys.forEach(key => { if(result[key]) result[key] = {...result[key], deleted:true}; });
    return result;
  }
  function metadataText(entries) {
    const cell = v => String(v || '—').replace(/&/g,'&amp;').replace(/\|/g,'&#124;').replace(/\r?\n/g,'&#10;');
    const lines = ['# Job dashboard metadata','','| Job ID | Link | Preferred | Applied | Note |','| --- | --- | --- | --- | --- |'];
    Object.values(entries).forEach(e => {
      if(e.preferred || e.applied || e.note) lines.push('| '+[e.jobId,e.link,e.preferred?'Yes':'—',e.applied?'Yes':'—',e.note].map(cell).join(' | ')+' |');
    });
    return lines.join('\n')+'\n\n'+marker+'\n```json\n'+JSON.stringify({version:1,entries},null,2)+'\n```\n';
  }
  function queueSave() {
    if(!ready)return;
    generation++;metaDirty=true;syncStatus.textContent='Saving…';syncStatus.style.color='#b54708';
    clearTimeout(saveTimer);saveTimer=setTimeout(saveLocal,200);
  }
  async function saveLocal() {
    if(!ready || saving)return;
    clearTimeout(saveTimer);saving=true;
    const version=generation, entries=snapshotEntries();
    try {
      const response=await fetch('api/save',{method:'POST',headers:{'Content-Type':'application/json','X-Jobs-Token':appToken},body:JSON.stringify({revision:appRevision,meta:metadataText(entries)})});
      if(!response.ok)throw new Error(await response.text());
      appRevision=(await response.json()).revision;savedEntries=entries;savedGeneration=version;
      if(generation===version)markMetaSynced('Saved to meta.md · '+new Date().toLocaleTimeString());
    } catch(e) {
      metaDirty=true;syncStatus.textContent='NOT SAVED: '+e.message;syncStatus.style.color='#b42318';
      saving=false;return;
    }
    saving=false;if(generation!==savedGeneration)saveLocal();
  }
  async function loadLocal() {
    try {
      const response=await fetch('api/load',{cache:'no-store'});
      if(!response.ok)throw new Error(await response.text());
      const data=await response.json();appRevision=data.revision;
      if(!data.jobs)throw new Error('jobs_raw.md is missing or empty.');
      if(data.meta.includes(marker)) {
        const part=data.meta.split(marker)[1].match(/```json\s*([\s\S]*?)\s*```/);
        if(!part)throw new Error('The saved metadata state is incomplete. Restore meta.md.bak before editing.');
        const state=JSON.parse(part[1]);
        if(state.version!==1 || !state.entries || typeof state.entries!=='object')throw new Error('Unsupported metadata state.');
        savedEntries=state.entries;
      } else if(data.meta.trim()) {
        metaRecords=parseMetaMarkdown(data.meta);
        metaRecords.forEach(rec=> {
          const key=stableJobKey({data:rec});
          savedEntries[key]={jobId:rec['Job ID'],link:rec.Link,preferred:/^(yes|true|1|x)$/i.test(rec.Preferred||''),applied:/^(yes|true|1|x)$/i.test(rec.Applied||''),note:rec.Note==='—'?'':rec.Note||'',edits:{}};
        });
      }
      loadJobsText(data.jobs);
      rows.forEach(row=>{
        row.sourceKey=stableJobKey(row);row.baseData={...row.data};
        const e=savedEntries[row.sourceKey];
        if(e){row.preferred=!!e.preferred;row.applied=!!e.applied;row.note=e.note||'';row.removed=!!e.removed;Object.assign(row.data,e.edits||{});row.edited=Object.keys(e.edits||{}).length>0;if(e.deleted)deletedKeys.add(row.sourceKey);}
      });
      rows=rows.filter(row=>!deletedKeys.has(row.sourceKey));
      const restore=document.createElement('button');restore.textContent='Restore deleted jobs';restore.onclick=()=>{
        if(!deletedKeys.size)return;
        deletedKeys.forEach(key=>{savedEntries[key]={...savedEntries[key],deleted:false,removed:false};});deletedKeys.clear();
        queueSave();saveLocal().then(()=>{if(!metaDirty)location.reload();});
      };document.querySelector('.controls').appendChild(restore);
      const reload=document.createElement('button');reload.textContent='Reload source files';reload.onclick=async()=>{await saveLocal();if(!metaDirty)location.reload();};document.querySelector('.controls').appendChild(reload);
      fileInput.hidden=true;metaInput.hidden=true;
      buildFilters();render();ready=true;markMetaSynced(data.meta?'Loaded saved markings':'Ready · changes save automatically');
      autoLoadStatus.textContent='Local folder: '+data.directory+' · Source files are read-only.';
      autoLoadStatus.style.color='#027a48';
    }catch(e){enableControls(false);fileInput.disabled=true;metaInput.disabled=true;autoLoadStatus.textContent='Could not load: '+e.message;autoLoadStatus.style.color='#b42318';}
  }
