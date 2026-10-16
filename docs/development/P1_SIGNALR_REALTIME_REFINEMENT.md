# P1 SignalR realtime foundation

## Governing authority

P1-010 implements the existing decision, not a new Product Decision. ADR-0004 selects ASP.NET Core SignalR only for Tunner-owned browser realtime and prohibits it as a Connected Product contract or business authority. ADR-0002 permits Redis only as a non-authoritative scale-out backplane when multiple Tunner web nodes require it.

Documents 01, 07, 08, 09, and 15 further require REST/BFF/read models to remain authoritative, a session-cookie BFF browser boundary, authenticated and authorized hub access, safe browser origin and rate/resource controls, redacted telemetry, and recovery by authoritative reads after reconnect or missed delivery.

## P1 technical boundary

The reusable Platform adapter defines only a typed, payload-minimal `resynchronize` hint and the server-side contracts needed to deliver it to an already authorized browser audience. It does not define business event categories, Product/account/resource subscription APIs, full domain-object messages, client reconnect timing, replay/retention, or any Product-facing SignalR capability.

A BFF that later maps the adapter must supply named browser-origin and rate-limit policies and authenticate the connection through the same session boundary as its REST endpoints. The mapped endpoint requires the technical authenticated-connect policy. The hub has no client subscription methods; an injected server-side audience resolver derives membership from authenticated Tunner scope and is responsible for resource authorization. A safe default resolver grants no audience.

Domain owners remain responsible for defining any later domain hint and for re-reading their authoritative BFF/API/read model after a reconnect, duplicate, stale, reordered, or unavailable hint. Security-sensitive state remains enforced through authoritative APIs. Identity/session owners must invalidate or reconnect affected realtime connections when their authoritative access changes.

Redis is deliberately not configured here. It may be added later only for a multi-node Tunner web host as the SignalR scale-out backplane; it is never identity, authorization, business-event, or other durable authority.

## Required P1 checks

- An unauthenticated connection is denied by the authenticated-connect policy.
- No hub operation accepts client-supplied Product, Account, resource, or group subscription input.
- The foundation hint has no sensitive identity, security, KYC, payment, financial, provider, or Product fields.
- A mapped BFF must provide explicit browser-origin and rate-limit policy names.
- The existing API/read-model remains the recovery route; a realtime outage is recoverable by reconnect, refresh, poll, or resynchronization.

## Primary sources

- [ASP.NET Core SignalR configuration](https://learn.microsoft.com/aspnet/core/signalr/configuration)
- [ASP.NET Core SignalR JavaScript client](https://learn.microsoft.com/aspnet/core/signalr/javascript-client)
- [SignalR authentication and authorization](https://learn.microsoft.com/aspnet/core/signalr/authn-and-authz)
- [Security considerations in ASP.NET Core SignalR](https://learn.microsoft.com/aspnet/core/signalr/security)
- [ASP.NET Core SignalR Redis backplane](https://learn.microsoft.com/aspnet/core/signalr/redis-backplane)