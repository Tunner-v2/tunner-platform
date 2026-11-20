import {budgets, fixedError, requireTrue, bounded, publicMarker, publicBody, registeredJourney, observedMap} from './contract.mjs';
import {downloadIdentity, sameDownload, inspectSurface} from './surface.mjs';
import {runPreparedJourney} from './prepared-journeys.mjs';

export {runPreparedJourney};

// No process/report/file export API. Private values must stay in the protected child.
function waitAtPublicGate(signal){
 if(signal?.aborted)return Promise.reject(fixedError('SUPERVISOR_TIMEOUT'));
 return new Promise((_,reject)=>signal.addEventListener('abort',()=>reject(fixedError('SUPERVISOR_TIMEOUT')),{once:true}));
}
export async function inspectRequestedDownload(download,root,expected,{signal,fault=null,publicFacts,onPublicStage}={}){
 if(!Buffer.isBuffer(expected)||expected.length===0||expected.length>budgets.maximumDownloadBytes)throw fixedError('SIZE_LIMIT');
 const path=await bounded(download.path(),budgets.action),identity=await downloadIdentity(root,path);
 const stream=await bounded(download.createReadStream(),budgets.action,'STREAM_FAILED');if(!stream)throw fixedError('STREAM_FAILED');
 const chunks=[];let size=0;
 try{
  const readAbort=new AbortController(),readSignal=signal?AbortSignal.any([signal,readAbort.signal]):readAbort.signal;
  const reading=(async()=>{for await(const chunk of stream){
   if(readSignal.aborted)throw fixedError('SUPERVISOR_TIMEOUT');
   if(fault==='stalled-partial-read'||fault==='cancel-stream-read'){
    // Public fault holds a genuinely read prefix before the remainder is inspected.
    requireTrue(chunk.length>1,'STREAM_FAILED');const prefix=chunk.subarray(0,Math.floor(chunk.length/2));chunks.push(Buffer.from(prefix));size+=prefix.length;
    if(publicFacts)publicFacts.partialRead=true;onPublicStage?.('PARTIAL_READ');await waitAtPublicGate(readSignal);
   }
   size+=chunk.length;if(size>budgets.maximumDownloadBytes)throw fixedError('SIZE_LIMIT');
   if(fault==='stream-error')throw fixedError('STREAM_FAILED');chunks.push(Buffer.from(chunk));
  }})();
  try{await bounded(reading,budgets.action,'STREAM_FAILED');}
  finally{
   readAbort.abort();stream.destroy();
   await bounded(reading.catch(()=>{}),budgets.close,'CLEANUP_FAILED');
   if(publicFacts)publicFacts.readSettled=true;
  }
  requireTrue(await sameDownload(root,identity),'PATH_REJECTED');
  const actual=Buffer.concat(chunks);try{requireTrue(actual.equals(expected),'BYTES_MISMATCH');}finally{actual.fill(0);}
  requireTrue((await inspectSurface(root,{admitted:identity,readContents:false})).clean,'SURFACE_FAILED');
  if(fault==='delete-error')throw fixedError('DELETE_FAILED');
  const deleteAbort=new AbortController(),deleteSignal=signal?AbortSignal.any([signal,deleteAbort.signal]):deleteAbort.signal;
  let nativeDelete;
  const deleting=(async()=>{
   if(publicFacts)publicFacts.deleteStarted=true;
   nativeDelete=download.delete();nativeDelete.catch(()=>{});onPublicStage?.('DELETE_STARTED');
   if(fault==='stalled-delete'||fault==='cancel-delete')await Promise.all([nativeDelete,waitAtPublicGate(deleteSignal)]);
   else await nativeDelete;
  })();
  try{await bounded(deleting,budgets.action,'DELETE_FAILED');}
  finally{
   deleteAbort.abort();
   await bounded(Promise.all([deleting.catch(()=>{}),nativeDelete?.catch(()=>{})]),budgets.close,'CLEANUP_FAILED');
   if(publicFacts){publicFacts.deleteSettled=nativeDelete!==undefined;publicFacts.deleteWaitSettled=true;}
  }
  return identity;
 }finally{stream.destroy();for(const chunk of chunks)chunk.fill(0);}
}

export async function publicEntry(page, contents = publicBody) {
  await page.setContent('<main><h1>Public private-boundary rehearsal</h1><p id="marker"></p><input id="enabled"><input id="disabled" disabled><button id="download">Request public fixture</button></main>');
  await page.evaluate(({marker, body}) => {
    document.querySelector('#marker').textContent = marker;
    document.querySelector('#download').addEventListener('click', () => {
      const capability = URL.createObjectURL(new Blob([body], {type: 'text/plain;charset=utf-8'}));
      const anchor = document.createElement('a'); anchor.href = capability; anchor.download = 'public-nonfunctional-fixture.txt'; anchor.click(); setTimeout(() => URL.revokeObjectURL(capability), 1000);
    });
  }, {marker: publicMarker, body: contents});
  requireTrue(await page.locator('#marker').textContent() === publicMarker);
}
export async function publicJourneyEntry(page) {
  await page.locator('#enabled').fill(publicMarker); requireTrue(await page.locator('#enabled').inputValue() === publicMarker); await page.locator('#enabled').fill('');
  let contained = false; try { await page.locator('#disabled').fill(publicMarker, {timeout: 200}); } catch { contained = true; }
  requireTrue(contained);
  const map = observedMap(['FIRST_FALSE', 'LATER_TRUE']); map.observe('FIRST_FALSE', false); map.observe('LATER_TRUE', true);
  const emitted = []; let rejected = false;
  try { map.finish((id, flag) => { emitted.push([id, flag]); if (!flag) throw fixedError(); }); } catch { rejected = true; }
  requireTrue(rejected && emitted.length === 2 && emitted[0][1] === false && emitted[1][1] === true);
}

export async function runJourney(input) {
  requireTrue(registeredJourney(input?.control?.journeyId), 'INPUT_INVALID');
  // Only the fixed protected child supplies these capabilities. The coordinator
  // requires current native protocol/resource and browser-public qualifications
  // before private resources; source presence is not execution or acceptance.
  return runPreparedJourney(input);
}
