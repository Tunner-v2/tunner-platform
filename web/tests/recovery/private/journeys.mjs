import {budgets, fixedError, requireTrue, bounded, publicMarker, publicBody, registeredJourney, observedMap, journeys} from './contract.mjs';
import {downloadIdentity, sameDownload, inspectSurface} from './surface.mjs';

// No process/report/file export API. Private values must stay in the protected child.
export async function inspectRequestedDownload(download, root, expected, {signal, fault = null} = {}) {
  if (!Buffer.isBuffer(expected) || expected.length === 0 || expected.length > budgets.maximumDownloadBytes) throw fixedError('SIZE_LIMIT');
  const path = await bounded(download.path(), budgets.action); const identity = await downloadIdentity(root, path);
  const stream = await bounded(download.createReadStream(), budgets.action, 'STREAM_FAILED'); if (!stream) throw fixedError('STREAM_FAILED');
  const chunks = []; let size = 0;
  try {
    await bounded((async () => { for await (const chunk of stream) { if (signal?.aborted) throw fixedError('SUPERVISOR_TIMEOUT'); size += chunk.length; if (size > budgets.maximumDownloadBytes) throw fixedError('SIZE_LIMIT'); if (fault === 'stream-error') throw fixedError('STREAM_FAILED'); chunks.push(Buffer.from(chunk)); } })(), budgets.action, 'STREAM_FAILED');
    requireTrue(await sameDownload(root, identity), 'PATH_REJECTED');
    const actual = Buffer.concat(chunks); try { requireTrue(actual.equals(expected), 'BYTES_MISMATCH'); } finally { actual.fill(0); }
    requireTrue((await inspectSurface(root, {admitted: identity, readContents: false})).clean, 'SURFACE_FAILED');
    if (fault === 'delete-error') throw fixedError('DELETE_FAILED');
    await bounded(download.delete(), budgets.action, 'DELETE_FAILED');
    return identity;
  } finally { stream.destroy(); for (const chunk of chunks) chunk.fill(0); }
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

export async function runJourney({control, check}) {
  requireTrue(registeredJourney(control.journeyId), 'INPUT_INVALID');
  // Recovery coordinator, method setup and final compiled owner wire have not been
  // source-frozen for this slice. Do not create accounts or values on assumptions.
  // A later reviewed delta must implement every registered fact before live dispatch.
  const observation = observedMap(journeys[control.journeyId]);
  for (const id of journeys[control.journeyId]) { try { check(id, observation.snapshot()[id]); } catch { /* Flush all fixed false observations. */ } }
  throw fixedError('DEPENDENCY_NOT_READY');
}
