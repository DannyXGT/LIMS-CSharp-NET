// Execute only the original pure filter/statistics functions, with API rows and a tiny DOM sink.
// The original HTML, event handlers, network setup and storage code are never executed.
const fs=require('node:fs');
const vm=require('node:vm');
const assert=require('node:assert/strict');
const [reference,inputFile,outputFile]=process.argv.slice(2);
const html=fs.readFileSync(reference,'utf8');
const input=JSON.parse(fs.readFileSync(inputFile,'utf8').replace(/^\uFEFF/,''));
function extract(name,single=false){
  const pattern=single?new RegExp('^function '+name+'[^\\n]+','m'):new RegExp('^function '+name+'\\([^\\n]*\\)\\{[\\s\\S]*?^\\}','m');
  const match=html.match(pattern);assert.ok(match,`Missing original function ${name}`);return match[0];
}
const columns=html.match(/const COLUMNS=\[[\s\S]*?\n\];/);assert.ok(columns);
const functions=[columns[0],extract('toISO'),extract('pruebaCompleta',true),extract('estadoVence'),extract('jobEnHold',true),extract('jobEtiqueta',true),extract('applyFilters')].join('\n');
class ReferenceDate extends Date {
  constructor(...args){super(...(args.length?args:[input.today+'T12:00:00-06:00']));}
  static now(){return new Date(input.today+'T12:00:00-06:00').getTime();}
}
const results=[];
for(const test of input.cases){
  const rows=input.rows.filter(r=>(!test.from||r.dueDate&&r.dueDate>=test.from)&&(!test.job||r.job.toUpperCase()===test.job.toUpperCase()));
  // Summary-only rows remain records; they are not invented test executions.
  const real=rows.filter(r=>r.executionId);
  const elements=new Map();
  const context=vm.createContext({Date:ReferenceDate,Set,Map,String,Math,isNaN,
    DB:real.map(r=>({fi:r.inDate||'',d:r.dueDate||'',j:r.job,p:r.originalTest||'',l:r.laboratory||'',h:r.digitizedAt?'hora registrada':'',f:r.testedOn||'',m:r.complete,dp:r.digitizedBy||''})),
    filters:test.filters||{},sortKey:null,sortDir:1,view:[],
    onHold:new Set(real.filter(r=>r.onHold).map(r=>r.job.trim().toUpperCase())),
    etiquetas:new Map(real.filter(r=>r.jobLabel).map(r=>[r.job.trim().toUpperCase(),r.jobLabel])),
    $:selector=>{if(!elements.has(selector))elements.set(selector,{textContent:'',style:{}});return elements.get(selector);},renderViewport:()=>{}});
  vm.runInContext(functions+'\napplyFilters();',context,{timeout:5000});
  const observed={pending:Number(elements.get('#cPend').textContent),overdue:Number(elements.get('#cVenc').textContent),onHoldTests:Number(elements.get('#cHold').textContent),taggedJobs:Number(elements.get('#cEtq').textContent)};
  for(const [key,value] of Object.entries(observed))assert.equal(test.expected[key],value,`${test.name}: ${key}`);
  assert.equal(test.expected.records,context.view.length+rows.filter(r=>!r.executionId&&Object.keys(test.filters||{}).length===0).length,`${test.name}: registros`);
  results.push({name:test.name,passed:true,originalFunctions:['applyFilters','pruebaCompleta','estadoVence','jobEnHold','jobEtiqueta'],observed,expected:test.expected});
}
fs.writeFileSync(outputFile,JSON.stringify({boundary:'Original HTML pure functions against all authenticated local API rows; no original network code executed',referenceSha256:require('node:crypto').createHash('sha256').update(html).digest('hex'),today:input.today,rowsRead:input.rows.length,results},null,2));
console.log(`PASS: ${results.length} conjuntos contrastados con JavaScript original; ${input.rows.length} filas reales de API.`);
