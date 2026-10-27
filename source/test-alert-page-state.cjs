const fs=require('fs'),vm=require('vm'),assert=require('assert');
const source=fs.readFileSync(__dirname+'/Importer.cs','utf8');
const script=source.match(/const string AlertPageStateScript = @"([\s\S]*?)";/)[1].replace(/""/g,'"');
function node(innerText='',visible=true,href=''){return {innerText,href,getClientRects:()=>visible?[{}]:[],visible};}
function state({title='Results for Job Alert',heading='Results for Job Alert "Biomed ME All"',text='Results 1 - 25 of 1,987',password=false,hidden=false,signedIn=true,jobs=true,readyState='complete'}={}){
 const anchors=[...(signedIn?[node('Sign Out')]:[]),...(jobs?[node('Research position',true,'https://www.higheredjobs.com/faculty/details.cfm?JobCode=179123456')]:[])];
 const document={title,readyState,body:{innerText:text},querySelectorAll:s=>s==='h1,h2'?[node(heading)]:s==='input[type=password]'?(password?[node('',!hidden)]:[]):anchors};
 return vm.runInNewContext(script,{document,getComputedStyle:e=>({visibility:e.visible?'visible':'hidden',display:e.visible?'block':'none'})});
}
for(const [label,input,expected] of [
 ['signed-in results with hidden password field',{password:true,hidden:true},'ready'],
 ['results take precedence over unrelated visible login form',{password:true},'ready'],
 ['actual login',{heading:'Sign in',text:'Sign in',password:true,signedIn:false,jobs:false},'login'],
 ['verification screen',{title:'Just a moment',heading:'',text:'',signedIn:false,jobs:false},'verification'],
 ['loading document',{readyState:'loading'},'loading'],
 ['empty alert',{text:'No matching jobs',jobs:false},'ready'],
 ['unrecognized page',{heading:'',text:'',jobs:false},'loading']
]){assert.equal(state(input),expected,label);console.log('PASS '+label);}