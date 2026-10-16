# P2-003 — Account and Identity persistence foundation

Authority: FRD17, ADR0006, PDR0007, architecture document02, testing document09, contract catalog32 and privacy FRD29. Existing Account eligibility remains in the framework-free Accounts project. Identity.Persistence owns EF/framework adapters and migrations; external framework IDs and objects are not cross-domain Account authority.

The bounded foundation persists stable ACC-* Account identifiers; canonical Account state and explicit optimistic versions with append-only version evidence; contacts with a database-enforced unique normalized verified email; and immutable provider/subject links without email-based linking. Derived age outcome/time/policy/evidence is stored without DOB. Contact normalization is server-owner supplied; this storage slice does not invent a public input/normalization policy or activate proof workflows.

ASP.NET Core Identity EF 10.0.13 and OpenIddict EF 7.7.2 provide credential/protocol storage. Identity schema v3 is explicitly identical at design time and runtime. Only stores and OpenIddict Core are registered. No UserManager, SignInManager, cookie scheme, token server, signing key, endpoint, token/session lifetime or password/lockout policy is activated. The operator supplies a connection string; migration commands have no persisted connection or credential default.

Identity tables and migration history are separately owned. The existing platform outbox mapping/transaction interface is reused and excluded from these migrations. Apply platform migrations before the Identity migration. Rehearse empty rollback/reapply; populated downgrade must refuse data loss and require a governed forward correction. No production migration is run by this work.

Coverage: canonical Account/contact/link resources and storage are implemented here. Authentication methods, sessions/devices/trust, Product grants, recovery, restrictions and verification operations remain in P2-004 through P2-017 as already mapped by the frozen backlog; framework table availability does not establish those behaviors.

Validation must include real PostgreSQL migrations, uniqueness races, concurrency, immutable identifiers/history, immediate store CRUD and owner/outbox atomicity; framework/domain dependency checks; current NuGet vulnerability audit; and exact committed ownership for this work plus the overlapping active P2-002 solution reference. Freeze ownership evidence in the candidate before testing. Record failed attempts and all test executions truthfully.

New personal-data inventory is reviewed as local, inactive storage. Production activation requires the approved processing/retention and release gates; no legal conclusion or retention duration is chosen. P1-RV001 remains an unwaived release blocker. Product acceptance is separate from local engineering validation.
