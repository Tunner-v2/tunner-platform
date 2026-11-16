import {z} from '@tunner/ui';
import {durationFields,integerFields,label,type Candidate,type Interval,type Scope,type ConfigurationLabel} from './configuration.api';
export const fieldNames=['version','supersedesVersion','processingReference','retentionReference','passwordAssurance',...integerFields,...durationFields,'requiredAssurance','proofLifetime','configurationVersionReference','qualificationReference','contentScope','variantReference','termsUri','privacyUri','supportUri','from','until','startBoundary','endMode','endBoundary'] as const;
export type FieldName=typeof fieldNames[number];
export interface FormState{fields:Record<FieldName,string>;evidence:string[];methods:{method:string;assurance:string}[]}
export function blankForm():FormState{return {fields:Object.fromEntries(fieldNames.map(name=>[name,''])) as Record<FieldName,string>,evidence:[''],methods:[{method:'',assurance:''}]};}
const ticksPerSecond=10_000_000n,maxTimeSpanTicks=9_223_372_036_854_775_807n;
export function secondsToDuration(raw:string):string{
 if(!/^\d+(?:\.\d{1,7})?$/.test(raw))throw new Error('Invalid duration');const [whole,fraction='']=raw.split('.'),ticks=BigInt(whole!)*ticksPerSecond+BigInt(fraction.padEnd(7,'0'));if(ticks<=0n||ticks>maxTimeSpanTicks)throw new Error('Invalid duration');const seconds=ticks/ticksPerSecond,days=seconds/86400n,hours=(seconds%86400n)/3600n,minutes=(seconds%3600n)/60n,remainder=seconds%60n,part=(ticks%ticksPerSecond).toString().padStart(7,'0').replace(/0+$/,'');return (days?days+'.':'')+hours.toString().padStart(2,'0')+':'+minutes.toString().padStart(2,'0')+':'+remainder.toString().padStart(2,'0')+(part?'.'+part:'');
}
export function durationToSeconds(raw:string):string{const m=/^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?$/.exec(raw);if(!m||Number(m[2])>23||Number(m[3])>59||Number(m[4])>59)throw new Error('Unsupported duration');const whole=BigInt(m[1]??0)*86400n+BigInt(m[2]!)*3600n+BigInt(m[3]!)*60n+BigInt(m[4]!),fraction=(m[5]??'').replace(/0+$/,'');const value=whole+(fraction?'.'+fraction:'');secondsToDuration(value);return value;}
function durationTicks(raw:string){const normalized=durationToSeconds(secondsToDuration(raw)),[whole,fraction='']=normalized.split('.');return BigInt(whole!)*ticksPerSecond+BigInt(fraction.padEnd(7,'0'));}
export function utcTicks(raw:string):bigint{const m=/^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(raw);if(!m||raw.startsWith('0000-'))throw new Error('UTC timestamp required');const time=Date.parse(m[1]+'Z');if(!Number.isFinite(time)||new Date(time).toISOString().slice(0,19)!==m[1])throw new Error('Invalid UTC timestamp');return BigInt(time)*10_000n+BigInt((m[2]??'').padEnd(7,'0'));}
export function flattenForm(form:FormState):Record<string,string>{return {...form.fields,...Object.fromEntries(form.evidence.map((value,index)=>['evidence.'+index,value])),...Object.fromEntries(form.methods.flatMap((row,index)=>[['method.'+index,row.method],['assurance.'+index,row.assurance]]))};}
function interval(fields:Record<string,string>):Interval{return {from:fields.from!,includesFrom:fields.startBoundary==='inclusive',until:fields.endMode==='at'?fields.until!:null,includesUntil:fields.endMode==='at'&&fields.endBoundary==='inclusive'};}
export function candidateFromForm(form:FormState,scope:Scope):Candidate{
 const f=form.fields,effectiveInterval=interval(f);if(scope.family==='AuthenticationContent')return {family:scope.family,value:{authenticationPolicyVersion:scope.authenticationPolicyVersion!,configurationVersionReference:f.configurationVersionReference,qualificationReference:f.qualificationReference,contentScope:f.contentScope,variantReference:f.variantReference,effectiveInterval,termsUri:f.termsUri,privacyUri:f.privacyUri,supportUri:f.supportUri||null}};
 const common={versionId:f.version,status:'ACTIVE' as const,effectiveInterval,supersedesVersionId:f.supersedesVersion||null,approvalEvidenceReferences:form.evidence};
 if(scope.family==='Assurance')return {family:scope.family,value:{metadata:{...common,scope:scope.assuranceScope!},requiredAssurance:f.requiredAssurance,proofLifetime:secondsToDuration(f.proofLifetime),methodAssurances:Object.fromEntries(form.methods.map(row=>[row.method,row.assurance]))}};
 return {family:scope.family,value:{metadata:{...common,scope:'AUTHENTICATION'},values:{version:f.version,processingReference:f.processingReference,retentionReference:f.retentionReference,passwordAssurance:f.passwordAssurance,passwordMaximumLength:Number(f.passwordMaximumLength),passwordHashIterations:Number(f.passwordHashIterations),absoluteLifetime:secondsToDuration(f.absoluteLifetime),idleLifetime:secondsToDuration(f.idleLifetime),rememberAbsoluteLifetime:secondsToDuration(f.rememberAbsoluteLifetime),rememberIdleLifetime:secondsToDuration(f.rememberIdleLifetime),browserContextLifetime:secondsToDuration(f.browserContextLifetime),abuseWindow:secondsToDuration(f.abuseWindow),networkAttemptLimit:Number(f.networkAttemptLimit),identifierAttemptLimit:Number(f.identifierAttemptLimit)}}};
}
export function formFromCandidate(candidate:Candidate):FormState{
 const result=blankForm(),f=result.fields;const effective=candidate.family==='AuthenticationContent'?candidate.value.effectiveInterval:candidate.value.metadata.effectiveInterval;
 f.from=effective.from;f.until=effective.until??'';f.startBoundary=effective.includesFrom?'inclusive':'exclusive';f.endMode=effective.until===null?'none':'at';f.endBoundary=effective.until===null?'':effective.includesUntil?'inclusive':'exclusive';
 if(candidate.family==='AuthenticationContent'){for(const name of ['configurationVersionReference','qualificationReference','contentScope','variantReference','termsUri','privacyUri','supportUri'] as const)f[name]=candidate.value[name]??'';}
 else{f.version=candidate.value.metadata.versionId;f.supersedesVersion=candidate.value.metadata.supersedesVersionId??'';result.evidence=[...candidate.value.metadata.approvalEvidenceReferences];if(candidate.family==='Assurance'){f.requiredAssurance=candidate.value.requiredAssurance;f.proofLifetime=durationToSeconds(candidate.value.proofLifetime);result.methods=Object.entries(candidate.value.methodAssurances).map(([method,assurance])=>({method,assurance}));}else{const values=candidate.value.values;for(const name of ['processingReference','retentionReference','passwordAssurance'] as const)f[name]=values[name];for(const name of integerFields)f[name]=String(values[name]);for(const name of durationFields)f[name]=durationToSeconds(values[name]);}}
 return result;
}
function intervalIssues(values:Record<string,string>,add:(field:string,key:ConfigurationLabel)=>void,prospective:boolean){
 let from:bigint|undefined;try{from=utcTicks(values.from!);if(prospective&&from<=BigInt(Date.now())*10_000n)add('from','prospectiveOnly');}catch{add('from',values.from?'invalidInterval':'required');}
 if(!['inclusive','exclusive'].includes(values.startBoundary!))add('startBoundary','required');if(!['none','at'].includes(values.endMode!))add('endMode','required');
 if(values.endMode==='at'){if(!['inclusive','exclusive'].includes(values.endBoundary!))add('endBoundary','required');try{const until=utcTicks(values.until!);if(from!==undefined&&(until<from||until===from&&!(values.startBoundary==='inclusive'&&values.endBoundary==='inclusive')))add('until','invalidInterval');}catch{add('until',values.until?'invalidInterval':'required');}}
}
export const publicationSchema=z.record(z.string(),z.string()).superRefine((values,ctx)=>intervalIssues(values,(field,key)=>ctx.addIssue({code:'custom',path:[field],message:label(key)}),true));
export function candidateSchema(scope:Scope,form:FormState,maximumBytes:number){return z.record(z.string(),z.string()).superRefine((values,ctx)=>{
 const add=(field:string,key:ConfigurationLabel)=>ctx.addIssue({code:'custom',path:[field],message:label(key)}),required=(field:string)=>{if(!values[field]?.trim())add(field,'required');};
 intervalIssues(values,add,false);
 if(scope.family==='AuthenticationContent'){
  for(const name of ['configurationVersionReference','qualificationReference','contentScope','variantReference','termsUri','privacyUri'])required(name);
  for(const name of ['termsUri','privacyUri','supportUri'])if(values[name])try{const uri=new URL(values[name]!);if(uri.protocol!=='https:'||uri.username||uri.password)throw new Error();}catch{add(name,'invalidUri');}
 }else{
  required('version');if(values.supersedesVersion&&values.version===values.supersedesVersion)add('supersedesVersion','invalidMapping');form.evidence.forEach((_,index)=>required('evidence.'+index));
  if(scope.family==='Authentication'){
   for(const name of ['processingReference','retentionReference','passwordAssurance'])required(name);
   for(const name of integerFields){const value=values[name]!;if(!/^\d+$/.test(value)||!Number.isSafeInteger(Number(value))||Number(value)<1||Number(value)>2147483647||(name==='passwordMaximumLength'&&(Number(value)<64||Number(value)>1048576)))add(name,value?'invalidNumber':'required');}
   for(const name of durationFields)try{secondsToDuration(values[name]!);}catch{add(name,values[name]?'invalidDuration':'required');}
   for(const [idle,absolute] of [['idleLifetime','absoluteLifetime'],['rememberIdleLifetime','rememberAbsoluteLifetime']] as const)try{if(durationTicks(values[idle]!)>durationTicks(values[absolute]!))add(idle,'invalidDuration');}catch{/* Individual duration errors own invalid input. */}
  }else{
   required('requiredAssurance');try{secondsToDuration(values.proofLifetime!);}catch{add('proofLifetime',values.proofLifetime?'invalidDuration':'required');}
   const methods=new Set<string>();form.methods.forEach((_,index)=>{const name='method.'+index;required(name);required('assurance.'+index);if(methods.has(values[name]!))add(name,'invalidMapping');methods.add(values[name]!);});
  }
 }
 if(ctx.issues.length===0){try{const candidate=candidateFromForm({...form,fields:values as Record<FieldName,string>,evidence:form.evidence.map((_,index)=>values['evidence.'+index]!),methods:form.methods.map((_,index)=>({method:values['method.'+index]!,assurance:values['assurance.'+index]!}))},scope);if(new TextEncoder().encode(JSON.stringify(candidate.value)).length>maximumBytes)add(scope.family==='AuthenticationContent'?'configurationVersionReference':'version','payloadExceeded');}catch{add('from','invalidInterval');}}
 });}
export interface DiffField{key:string;label:ConfigurationLabel;detail?:string;value:string}
export function candidateFields(candidate:Candidate):DiffField[]{
 const rows:DiffField[]=[];const add=(key:string,name:ConfigurationLabel,value:unknown,detail?:string)=>rows.push({key,label:name,value:value===null?'—':String(value),...(detail?{detail}:{})});
 const intervalRows=(v:Interval)=>{add('interval.from','from',v.from);add('interval.includesFrom','startBoundary',v.includesFrom);add('interval.until','until',v.until);add('interval.includesUntil','endBoundary',v.includesUntil);};
 if(candidate.family==='AuthenticationContent'){const value=candidate.value;for(const field of ['authenticationPolicyVersion','configurationVersionReference','qualificationReference','contentScope','variantReference','termsUri','privacyUri','supportUri'] as const)add(field,field,value[field]);intervalRows(value.effectiveInterval);}
 else{
  const metadata=candidate.value.metadata;add('metadata.version','version',metadata.versionId);add('metadata.status','metadataStatus',metadata.status);add('metadata.supersedes','supersedesVersion',metadata.supersedesVersionId);metadata.approvalEvidenceReferences.forEach((value,index)=>add('evidence.'+index,'evidence',value,String(index+1)));intervalRows(metadata.effectiveInterval);
  if(candidate.family==='Authentication'){add('metadata.scope','scope',candidate.value.metadata.scope);const values=candidate.value.values;add('values.version','version',values.version);for(const field of ['processingReference','retentionReference','passwordAssurance',...integerFields,...durationFields] as const)add('values.'+field,field,values[field]);}
  else{add('metadata.operation','scopeOperation',candidate.value.metadata.scope.operation);add('metadata.context','scopeContext',candidate.value.metadata.scope.context);add('requiredAssurance','requiredAssurance',candidate.value.requiredAssurance);add('proofLifetime','proofLifetime',candidate.value.proofLifetime);for(const [method,value] of Object.entries(candidate.value.methodAssurances).sort(([a],[b])=>a.localeCompare(b)))add('method.'+method,'methodAssurance',value,method);}
 }return rows;
}
