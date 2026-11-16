import {z} from '@tunner/ui';
import {label} from './content.api';
export function draftSchema(maximum:number){return z.object({text:z.string().trim().min(1,label('required')).max(maximum,label('maxLength')),changeReason:z.string().trim().min(1,label('required')).max(maximum,label('maxLength'))});}
export function searchSchema(maximum:number){return z.object({search:z.string().max(maximum,label('maxLength'))});}
export const publicationSchema=z.object({from:z.string().min(1,label('required')),startBoundary:z.enum(['inclusive','exclusive'],{error:label('required')}),endMode:z.enum(['none','at'],{error:label('required')}),until:z.string(),endBoundary:z.string()}).superRefine((value,context)=>{
 const from=Date.parse(value.from+'Z');if(!Number.isFinite(from)||from<=Date.now())context.addIssue({code:'custom',path:['from'],message:label('prospectiveOnly')});
 if(value.endMode==='at'){const until=Date.parse(value.until+'Z');if(!value.until||!Number.isFinite(until)||until<=from)context.addIssue({code:'custom',path:['until'],message:label('invalidTime')});if(value.endBoundary!=='inclusive'&&value.endBoundary!=='exclusive')context.addIssue({code:'custom',path:['endBoundary'],message:label('required')});}
});
