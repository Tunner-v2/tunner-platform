/** Shared Account timestamp presentation; persisted values and evaluation remain server-owned. */
export function formatAccountTime(value:string,locale:string){return new Intl.DateTimeFormat(locale,{year:'numeric',month:'short',day:'2-digit',hour:'2-digit',minute:'2-digit',timeZone:'UTC',timeZoneName:'short',hourCycle:'h23'}).format(new Date(value));}
