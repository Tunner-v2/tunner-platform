import { useLayoutEffect, useRef } from 'react';
import type { ReactNode } from 'react';

export interface ModalDialogProps {
  labelledBy:string; describedBy?:string; busy?:boolean; className?:string; returnFocusTo?:HTMLElement|null; onRequestClose:()=>void; children:ReactNode;
}
/** Native inert background plus explicit keyboard wrap and owner-trigger focus restoration. */
export function ModalDialog({labelledBy,describedBy,busy=false,className='',returnFocusTo,onRequestClose,children}:ModalDialogProps){
  const element=useRef<HTMLDialogElement>(null);
  useLayoutEffect(()=>{
    const dialog=element.current;if(!dialog)return;
    const previous=returnFocusTo??(document.activeElement instanceof HTMLElement?document.activeElement:null);
    dialog.showModal();
    return()=>{dialog.close();if(previous?.isConnected)previous.focus({preventScroll:true});};
  },[]);
  return <dialog ref={element} className={'tn-modal '+className} aria-modal="true" aria-labelledby={labelledBy} aria-describedby={describedBy} aria-busy={busy}
    onKeyDown={event=>{
      if(event.key!=='Tab')return;
      const dialog=event.currentTarget;
      const fields=Array.from(dialog.querySelectorAll<HTMLElement>('button:not(:disabled),input:not(:disabled),select:not(:disabled),textarea:not(:disabled),a[href],[tabindex]:not([tabindex="-1"])')).filter(field=>field.getClientRects().length>0);
      const first=fields[0],last=fields.at(-1);
      if(!first){event.preventDefault();dialog.focus();return;}
      if(event.shiftKey&&(document.activeElement===first||!fields.includes(document.activeElement as HTMLElement))){event.preventDefault();last?.focus();}
      else if(!event.shiftKey&&document.activeElement===last){event.preventDefault();first.focus();}
    }}
    onCancel={event=>{event.preventDefault();if(!busy)onRequestClose();}}>{children}</dialog>;
}
