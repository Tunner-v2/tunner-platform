// Public-only entry for the actual production controller/Blob path. No account or native owner.
import {createRoot} from 'react-dom/client';
import {useRef} from 'react';
import {ContentProvider,ThemeProvider} from '../../packages/ui/src/index';
import type {PublishedContent} from '../../packages/ui/src/index';
import '../../packages/ui/styles/index.css';
import '../../apps/user/passkeys.css';
import {RecoveryCodesSection} from '../../apps/user/recovery-codes';
import proposal from '../../apps/user/passkeys.content.proposal.json';
const content:Record<string,PublishedContent>=Object.fromEntries(Object.entries(proposal.content).map(([key,text])=>[key,{key,text,locale:'en',state:'PUBLISHED',variantRef:'PUBLIC-ONLY',versionRef:'PUBLIC-ONLY',publicationRef:'NOT-PRODUCTION'}]));
function Fixture(){const heading=useRef<HTMLHeadingElement>(null);return <main><h1 ref={heading} tabIndex={-1}>Public production-controller rehearsal</h1><RecoveryCodesSection disabled={false} onBlockedChange={()=>{}} fallbackFocus={()=>heading.current}/></main>;}
createRoot(document.getElementById('root')!).render(<ThemeProvider surface="user" mode="light"><ContentProvider locale="en" audience="recovery-public-controller" resolve={key=>content[key]}><Fixture/></ContentProvider></ThemeProvider>);
