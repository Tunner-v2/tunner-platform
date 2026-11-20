import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createHash} from 'node:crypto';
const root=path.dirname(fileURLToPath(import.meta.url)),history='history/20261110-v02-consumed-copy-before';
const hash=async p=>createHash('sha256').update(await fs.readFile(path.join(root,p))).digest('hex');
const pictures=[];for(const width of [1440,390,320]){const p=`rendered/setup-consumed-${width}.png`;pictures.push({width,path:p,previousPath:history+'/'+p,oldSha256:await hash(history+'/'+p),newSha256:await hash(p)});}
const sources=[];for(const p of ['preview.tsx','REVIEW.md','index.html'])sources.push({path:p,previousPath:history+'/'+p,oldSha256:await hash(history+'/'+p),newSha256:await hash(p)});
const before=JSON.parse(await fs.readFile(path.join(root,history,'provenance.json'),'utf8'));const nonTargetPictures=[];
for(const f of before.files.filter(f=>f.path.endsWith('.png')&&!f.path.includes('/setup-consumed-'))){const p=f.path.split('/ui-review/')[1];nonTargetPictures.push({path:p,unchanged:await hash(p)===f.sha256});}
if(nonTargetPictures.some(p=>!p.unchanged))throw new Error('Non-target picture changed');
const checks=JSON.parse(await fs.readFile(path.join(root,'rendered/checks.json'),'utf8')).results.filter(r=>r.state==='setup-consumed');const axe=JSON.parse(await fs.readFile(path.join(root,'rendered/accessibility.json'),'utf8')).results.filter(r=>r.state==='setup-consumed');
await fs.writeFile(path.join(root,'REVISION-V02-CONSUMED-COPY-20261110.json'),JSON.stringify({status:'UNAPPROVED_V02_PUBLIC_COPY_CLARIFICATION_ONLY',recordedAt:new Date().toISOString(),scope:'setup-consumed title/subtitle/section heading and removal of obsolete scan instruction at three viewports only',productConsent:'PENDING; root to notify user before production implementation',productionEdits:false,approvedFigmaEvidenceChanged:false,history,pictures,sources,nonTargetPictureCheck:{count:nonTargetPictures.length,unchanged:nonTargetPictures.every(p=>p.unchanged)},checks:{cases:checks.length,issues:checks.flatMap(r=>r.issues),axeCases:axe.length,axeViolations:axe.reduce((n,r)=>n+r.violations.length,0),boundary:'Only the three public synthetic consumed-state renders and automated structural/Axe checks were refreshed. No production/native/security acceptance.'}},null,2));
console.log(JSON.stringify({pictures,nonTargetPictureCount:nonTargetPictures.length,allOtherPicturesUnchanged:true},null,2));
