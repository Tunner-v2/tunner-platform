export const contentRoutes={workspace:{id:'A-CNT-01',pathname:'/admin/content'},review:{id:'A-CNT-01-REVIEW',pathname:'/admin/content/review'},history:{id:'A-CNT-01-HISTORY',pathname:'/admin/content/history'}} as const;
export function isContentRoute(path:string){return Object.values(contentRoutes).some(route=>route.pathname===path);}
