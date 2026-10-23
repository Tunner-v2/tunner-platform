# P2-005 exact external design updates for the Product Owner

AI is not allowed to edit Figma or Google Drive. Both remain unchanged. The Product Owner approved these two implementation scopes in the linked consent receipt; this document provides the manual Figma changes required by AMD-0008 section4 before affected production UI acceptance. No new business amendment is requested: FRD17 already authorizes the password/session slice, and Google/Microsoft direction is explicit.

Platform UX file: https://www.figma.com/design/apNbwvAgdn1xIxYH1oiaSC/Tunner-Platform-UX-Master . Approval evidence: governance/evidence/TUN-P2-005-VISUAL-CONSENT-20261021.json . Original reviewed packet: artifacts/p2-005-auth-20261021/visual-review.html . Approved images: login-proposal-1440.png, signed-in-proposal-1440.png and their320px counterparts in that folder. Actual screenshots and test receipts will be linked by the implementation handoff.

## 1. Update Login frame2:4 on page0:1

Preserve the1440×1024 page, #f8fafc background, exact existing indigo/violet glow layers2:5/2:6, brand3:2 (Inter Bold24, text-only Tunner), header/footer and all unrelated layouts.

| Node | Required change | Reason |
|---|---|---|
| 2:12 status group | Hide when no authoritative status feed is configured | Do not assert All Operational without a source |
| 2:21 subtitle | Enter your credentials to access your account | The current slice does not implement a dashboard |
| 2:28 /2:31 provider slot/label | Microsoft; retain the existing slot and approved text-only Microsoft pattern; hide old GitHub icon2:29 | Google and Microsoft only; GitHub deferred |
| 2:24 and2:28 provider buttons | Approved disabled background #f8fafc, text #616975, border #e2e8f0; preserve44px height and8px radius | Federation belongs to P2-010 |
| 2:52 Forgot password | Approved disabled #616975 | Recovery belongs to P2-011 |
| 2:39 and2:43 idle inputs | Border #616975;44px height,8px radius,14px horizontal padding; existing shared focus/error/pending variants | Approved accessible slate treatment |
| 2:40 and2:44 placeholders | you@example.com / Enter your password; #616975 | Remove sample personal data and prefilled password representation |
| 2:49 checkbox /2:75 check | Show the unchecked default, retain Remember me behavior | Actual initial form state |
| 2:34 separator | Retain placement and approved packet typography; #616975 | Accessible approved contrast |
| 2:60 /2:62 /2:64 footer and2:21 /2:51 /2:57 body | #475569 where shown in the approved packet | Shared approved slate text |

Approved rendered card reference:440×583 atx500,y220.5. CSS border+40px padding gives358px content width (x541). Equivalent Figma layout must account for the1px border. Groups: intro62px;32px gap; providers/separator74px;32px gap; fields/options191px;32px gap; actions78px. Preserve heading Geist Bold28/36 and body Geist14/18. Shared error presentation reuses70:1257; pending/error expansion may grow the card. Provider buttons remain disabled until their owner milestone is implemented; this does not approve federation early.

## 2. Add one editable signed-in confirmation frame

Clone approved Account Ready frame70:630 into clear space beside the existing screens, preserving the original frame. Name the new frame Signed-in confirmation and annotate its canonical route /signed-in, screen U-AUTH-SIGNED-IN and FRD17/P2-005. Return the new node URL/ID so the repository inventory can reference it. Do not replace registration completion or create dashboard/account-editing features.

Desktop page1440×1024; same Login background/glows/header/footer. Card500×516 atx470,y254; white, border #e2e8f0,16px radius, approved shadow,40px padding plus1px border. Use editable related-content auto-layout with28px gaps and418px content width.

| Element | Exact approved content / geometry |
|---|---|
| Existing approved check/circle |72×72, centered; reuse the exact approved vector/check and pale-indigo circle |
| Heading | You’re signed in — Geist Bold28/36, left aligned |
| Subtitle | You are signed in to Tunner. —14/18,8px below heading |
| Note | #f1f5f9,16px radius,20px padding; title Your Tunner account (15/22, semibold),10px gap, body For your security, sign out when you finish on a shared device. (14/18) |
| Primary button | Sign out;418×44,8px radius, #4f46e5, white14px semibold, existing approved shadow/states |
| Caption | Signing out ends your Tunner session. Connected products manage their own sessions. —12/18, #616975 |

Content top positions in the1440px approved rendering: check295; heading/intro395; note485; Sign out621; caption693. No status badge, Product grants, Billing, dashboard or account editing is introduced.

## 3. Responsive/state reference and completion evidence

Reuse the explicitly approved shared responsive/interaction patterns: atwidth600px and below,24px horizontal/28px vertical card padding, header20px side padding, stage96px top/112px bottom, wrapped footer and functional controls. Keep the theme, glows, labels and error association. The approved320px packet and actual320/390/768/1024/1440 renders provide comparison evidence. No new visual pattern is proposed.

After the owner update, provide the new signed-in Figma node reference. AI may then read and compare the current designs against committed rendered screens, update local traceability/evidence and run native acceptance checks. UI_VISUAL_APPROVAL and DEF-TUN-P2-005-UI-001 remain open until verified. No Google Drive FRD or amendment edit is currently necessary for these already-authorized functional requirements.
