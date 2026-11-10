import {createServer} from '../../../web/node_modules/vite/dist/node/index.js';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
const artifact=path.dirname(fileURLToPath(import.meta.url));
const repo=path.resolve(artifact,'../../..');
const server=await createServer({configFile:false,root:artifact,resolve:{alias:[{find:'@tunner/ui/styles.css',replacement:path.join(repo,'web/packages/ui/styles/index.css')},{find:'@tunner/ui',replacement:path.join(repo,'web/packages/ui/src/index.ts')},{find:'/web',replacement:path.join(repo,'web')},{find:'react-dom/client',replacement:path.join(repo,'web/node_modules/react-dom/client.js')},{find:'react/jsx-dev-runtime',replacement:path.join(repo,'web/node_modules/react/jsx-dev-runtime.js')},{find:'react/jsx-runtime',replacement:path.join(repo,'web/node_modules/react/jsx-runtime.js')},{find:/^react$/,replacement:path.join(repo,'web/node_modules/react/index.js')}]},server:{host:'127.0.0.1',port:5198,strictPort:true,fs:{allow:[repo]}},plugins:[{name:'proposal-assets',configureServer(s){s.middlewares.use((req,_res,next)=>{if(req.url?.startsWith('/registration/assets/')){const asset=req.url.slice('/registration/'.length).replace('bg-glow-indigo.svg','login-bg-indigo.svg').replace('bg-glow-violet.svg','login-bg-violet.svg');req.url='/@fs/'+path.join(repo,'web/apps/user/public',asset).replaceAll('\\','/');}next();});}}]});
await server.listen();
console.log('Synthetic artifact preview ready at http://127.0.0.1:5198/preview.html');
for(const signal of ['SIGINT','SIGTERM'])process.on(signal,async()=>{await server.close();process.exit(0);});
