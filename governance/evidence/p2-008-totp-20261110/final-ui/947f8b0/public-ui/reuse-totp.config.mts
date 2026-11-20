import original from '../../../../web/playwright.totp.config.ts';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const web=fileURLToPath(new URL('../../../../web/',import.meta.url)),out=fileURLToPath(new URL('./',import.meta.url));
export default {...original,testDir:path.resolve(web,original.testDir!),outputDir:path.join(out,'playwright-output'),reporter:[['list'],['json',{outputFile:path.join(out,'results.json')}]],webServer:(original.webServer as any[]).map(server=>({...server,cwd:web,reuseExistingServer:true}))};
