import original from '../../../../../web/playwright.registration.config.ts';
import {fileURLToPath} from 'node:url';
const cwd=fileURLToPath(new URL('../../../../../web/',import.meta.url));
const servers=Array.isArray(original.webServer)?original.webServer:[original.webServer];
export default {...original,testDir:fileURLToPath(new URL('../../../../../web/tests/registration/',import.meta.url)),webServer:servers.map(server=>({...server,cwd,reuseExistingServer:true}))};
