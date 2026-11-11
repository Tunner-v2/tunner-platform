import original from '../../../../../web/playwright.totp.config.ts';
import {fileURLToPath} from 'node:url';
const cwd=fileURLToPath(new URL('../../../../../web/',import.meta.url));
const servers=Array.isArray(original.webServer)?original.webServer:[original.webServer];
export default {...original,testDir:fileURLToPath(new URL('../../../../../web/tests/totp/ui/',import.meta.url)),webServer:servers.map(server=>({...server,cwd,reuseExistingServer:true}))};
