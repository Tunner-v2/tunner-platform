# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: configuration.spec.ts >> RecoveryCodes typed policy preserves exact Int64 validation retry and immutable history at 1440
- Location: tests\configuration-admin\configuration.spec.ts:330:31

# Error details

```
Error: expect(locator).toHaveCount(expected) failed

Locator:  locator('[data-field="values.generationAdmission"]').locator('td').nth(1).locator('[data-content-publication="fixture-chrome-p1"]')
Expected: 1
Received: 2
Timeout:  5000ms

Call log:
  - Expect "toHaveCount" with timeout 5000ms
  - waiting for locator('[data-field="values.generationAdmission"]').locator('td').nth(1).locator('[data-content-publication="fixture-chrome-p1"]')
    14 × locator resolved to 2 elements
       - unexpected value "2"

```

# Page snapshot

```yaml
- generic [ref=e4]:
  - link "Skip to content" [ref=e5] [cursor=pointer]:
    - /url: "#configuration-main"
  - generic [ref=e6]:
    - complementary [ref=e7]:
      - generic [ref=e8]:
        - generic [ref=e9]: Tunner
        - paragraph [ref=e10]: Admin operations
      - navigation "Admin operations" [ref=e11]:
        - link "Content" [ref=e12] [cursor=pointer]:
          - /url: /admin/content
        - link "Identity settings" [ref=e13] [cursor=pointer]:
          - /url: /identity/settings/recovery-codes
      - generic [ref=e14]:
        - strong [ref=e15]: Identity settings
        - paragraph [ref=e16]: Identity security authority is separate from Content editing.
    - generic [ref=e17]:
      - banner [ref=e18]:
        - generic [ref=e19]:
          - strong [ref=e20]: Identity settings
          - paragraph [ref=e21]: Typed, versioned runtime configuration
      - region "Identity security authority is separate from Content editing." [ref=e22]
      - main [ref=e23]:
        - generic [ref=e24]:
          - generic [ref=e25]:
            - paragraph [ref=e26]: Identity settings
            - heading "Review configuration" [active] [level=1] [ref=e27]
            - paragraph [ref=e28]: Typed, versioned runtime configuration
          - navigation "Review configuration" [ref=e29]:
            - link "Edit draft" [ref=e30] [cursor=pointer]:
              - /url: /identity/settings/recovery-codes
            - link "Review configuration" [ref=e31] [cursor=pointer]:
              - /url: /identity/settings/recovery-codes/review
            - link "Publication history" [ref=e32] [cursor=pointer]:
              - /url: /identity/settings/recovery-codes/history
        - status [ref=e33]:
          - generic [ref=e34]: The request was applied. Current state has been reloaded.
          - button "Refresh" [ref=e35] [cursor=pointer]
        - generic [ref=e37]:
          - generic [ref=e38]:
            - generic [ref=e39]: Exact scope
            - combobox "Exact scope" [ref=e40]:
              - option "Choose a server-qualified scope"
              - option "RECOVERY_CODES" [selected]
          - generic [ref=e41]:
            - generic [ref=e42]:
              - generic [ref=e43]: Search candidates
              - textbox "Search candidates" [ref=e44]
              - generic [ref=e45]: Find an exact candidate policy version.
            - button "Search candidates" [ref=e46] [cursor=pointer]
          - generic [ref=e48]:
            - generic [ref=e49]: Saved candidate
            - combobox "Saved candidate" [ref=e50]:
              - option "New typed draft"
              - option "fixture-v2 · Validated · 3" [selected]
        - generic [ref=e51]:
          - generic [ref=e52]:
            - heading "Current vs proposed" [level=2] [ref=e53]
            - generic [ref=e54]:
              - generic [ref=e55]: Compare with published version
              - combobox "Compare with published version" [ref=e56]:
                - option "Choose"
                - option "fixture-v1 · 00000001" [selected]
            - table [ref=e57]:
              - rowgroup [ref=e58]:
                - row [ref=e59]:
                  - columnheader "Setting" [ref=e60]
                  - columnheader "Selected publication" [ref=e61]
                  - columnheader "Saved candidate" [ref=e62]
              - rowgroup [ref=e63]:
                - row [ref=e64]:
                  - rowheader "Policy version reference" [ref=e65]
                  - cell "fixture-v1" [ref=e66]
                  - cell "fixture-v2" [ref=e67]
                - row [ref=e68]:
                  - rowheader "Candidate metadata selection status" [ref=e69]
                  - cell "ACTIVE" [ref=e70]
                  - cell "ACTIVE" [ref=e71]
                - row [ref=e72]:
                  - rowheader "Previous policy version reference" [ref=e73]
                  - cell "—" [ref=e74]
                  - cell "fixture-v1" [ref=e75]
                - row [ref=e76]:
                  - rowheader "Candidate source evidence 1" [ref=e77]:
                    - text: Candidate source evidence
                    - generic [ref=e78]: "1"
                  - cell "fixture-source-evidence" [ref=e79]
                  - cell "fixture-source-evidence" [ref=e80]
                - row [ref=e81]:
                  - rowheader "Available from · UTC" [ref=e82]
                  - cell "2026-01-01T00:00:00.0000001Z" [ref=e83]
                  - cell "2026-01-01T00:00:00.0000001Z" [ref=e84]
                - row [ref=e85]:
                  - rowheader "Start boundary" [ref=e86]
                  - cell "true" [ref=e87]
                  - cell "true" [ref=e88]
                - row [ref=e89]:
                  - rowheader "Available until · UTC" [ref=e90]
                  - cell "—" [ref=e91]
                  - cell "—" [ref=e92]
                - row [ref=e93]:
                  - rowheader "End boundary" [ref=e94]
                  - cell "false" [ref=e95]
                  - cell "false" [ref=e96]
                - row [ref=e97]:
                  - rowheader "Exact scope" [ref=e98]
                  - cell "RECOVERY_CODES" [ref=e99]
                  - cell "RECOVERY_CODES" [ref=e100]
                - row [ref=e101]:
                  - rowheader "Policy version reference" [ref=e102]
                  - cell "fixture-v1" [ref=e103]
                  - cell "fixture-v2" [ref=e104]
                - row [ref=e105]:
                  - rowheader "Origin" [ref=e106]
                  - cell "https://recovery.fixture.example.test" [ref=e107]
                  - cell "https://recovery.fixture.example.test" [ref=e108]
                - row [ref=e109]:
                  - rowheader "Delivery profile version" [ref=e110]
                  - cell "fixture-delivery-v1" [ref=e111]
                  - cell "fixture-reviewed-delivery-v3" [ref=e112]
                - row [ref=e113]:
                  - rowheader "Generate assurance policy version" [ref=e114]
                  - cell "fixture-generate-policy" [ref=e115]
                  - cell "fixture-generate-policy" [ref=e116]
                - row [ref=e117]:
                  - rowheader "Replace assurance policy version" [ref=e118]
                  - cell "fixture-replace-policy" [ref=e119]
                  - cell "fixture-replace-policy" [ref=e120]
                - row [ref=e121]:
                  - rowheader "Processing reference" [ref=e122]
                  - cell "fixture-processing" [ref=e123]
                  - cell "fixture-processing" [ref=e124]
                - row [ref=e125]:
                  - rowheader "Retention reference" [ref=e126]
                  - cell "fixture-retention" [ref=e127]
                  - cell "fixture-retention" [ref=e128]
                - row [ref=e129]:
                  - rowheader "Maximum active sets" [ref=e130]
                  - cell "2" [ref=e131]
                  - cell "2" [ref=e132]
                - row [ref=e133]:
                  - rowheader "Maximum pending operations" [ref=e134]
                  - cell "4" [ref=e135]
                  - cell "4" [ref=e136]
                - row [ref=e137]:
                  - rowheader "Account consumption attempt limit" [ref=e138]
                  - cell "2" [ref=e139]
                  - cell "2" [ref=e140]
                - row [ref=e141]:
                  - rowheader "Network consumption attempt limit" [ref=e142]
                  - cell "5" [ref=e143]
                  - cell "5" [ref=e144]
                - row [ref=e145]:
                  - rowheader "Operation lifetime" [ref=e146]
                  - cell "00:05:00.0000001" [ref=e147]
                  - cell "00:05:00.0000001" [ref=e148]
                - row [ref=e149]:
                  - rowheader "Result recovery lifetime" [ref=e150]
                  - cell "00:05:00" [ref=e151]
                  - cell "00:05:00" [ref=e152]
                - row [ref=e153]:
                  - rowheader "Format profile" [ref=e154]
                  - cell "NATIVE_HEX32_V1" [ref=e155]
                  - cell "NATIVE_HEX32_V1" [ref=e156]
                - row [ref=e157]:
                  - rowheader "Generation admission" [ref=e158]
                  - cell "Within approved active-set capacity (1)" [ref=e159]
                  - cell "Within approved active-set capacity (1)" [ref=e160]
                - row [ref=e161]:
                  - rowheader "Replacement mechanism" [ref=e162]
                  - cell "Retire exact expected sets at issue commit (1)" [ref=e163]
                  - cell "Retire exact expected sets at issue commit (1)" [ref=e164]
                - row [ref=e165]:
                  - rowheader "Session effect" [ref=e166]
                  - cell "No session mutation (1)" [ref=e167]
                  - cell "No session mutation (1)" [ref=e168]
                - row [ref=e169]:
                  - rowheader "Delivery mechanism" [ref=e170]
                  - cell "One volatile view with optional single local download (1)" [ref=e171]
                  - cell "One volatile view with optional single local download (1)" [ref=e172]
                - row [ref=e173]:
                  - rowheader "Crypto profile version" [ref=e174]
                  - cell "fixture-crypto-v1" [ref=e175]
                  - cell "fixture-crypto-v2" [ref=e176]
                - row [ref=e177]:
                  - rowheader "Native Identity version" [ref=e178]
                  - cell "10.0.13" [ref=e179]
                  - cell "10.0.13" [ref=e180]
                - row [ref=e181]:
                  - rowheader "Codes per set" [ref=e182]
                  - cell "5" [ref=e183]
                  - cell "5" [ref=e184]
                - row [ref=e185]:
                  - rowheader "Maximum codes per set" [ref=e186]
                  - cell "10" [ref=e187]
                  - cell "10" [ref=e188]
                - row [ref=e189]:
                  - rowheader "Hash iterations" [ref=e190]
                  - cell "10" [ref=e191]
                  - cell "10" [ref=e192]
                - row [ref=e193]:
                  - rowheader "Maximum generation attempts" [ref=e194]
                  - cell "10" [ref=e195]
                  - cell "10" [ref=e196]
                - row [ref=e197]:
                  - rowheader "Maximum concurrent work" [ref=e198]
                  - cell "2" [ref=e199]
                  - cell "2" [ref=e200]
                - row [ref=e201]:
                  - rowheader "Maximum hash work · exact Int64" [ref=e202]
                  - cell "9007199254740993" [ref=e203]
                  - cell "9223372036854775807" [ref=e204]
                - row [ref=e205]:
                  - rowheader "Maximum work duration" [ref=e206]
                  - cell "00:00:10.0000001" [ref=e207]
                  - cell "00:00:10.0000001" [ref=e208]
          - generic [ref=e209]:
            - heading "Identity approval" [level=2] [ref=e210]
            - generic [ref=e211]:
              - generic [ref=e212]:
                - term [ref=e213]: Saved candidate
                - definition [ref=e214]: fixture-v2
              - generic [ref=e215]:
                - term [ref=e216]: State
                - definition [ref=e217]: Validated
              - generic [ref=e218]:
                - term [ref=e219]: Candidate revision
                - definition [ref=e220]: "3"
              - generic [ref=e221]:
                - term [ref=e222]: Candidate digest
                - definition [ref=e223]: fixture-digest-3
              - generic [ref=e224]:
                - term [ref=e225]: Governance version
                - definition [ref=e226]: fixture-governance-v1
            - paragraph [ref=e227]:
              - generic [ref=e228]: Configured author, approver and publisher separation is checked independently.
            - generic [ref=e229]:
              - button "Validate candidate" [disabled] [ref=e230]
              - button "Approve candidate" [ref=e232] [cursor=pointer]
              - button "Publish configuration" [disabled] [ref=e234]
        - generic [ref=e236]:
          - heading "Publication history" [level=2] [ref=e237]
          - article [ref=e238]:
            - generic [ref=e239]:
              - generic [ref=e240]:
                - term [ref=e241]: Policy version reference
                - definition [ref=e242]: fixture-v1
              - generic [ref=e243]:
                - term [ref=e244]: Publication reference
                - definition [ref=e245]: 43000000-0000-0000-0000-000000000001
              - generic [ref=e246]:
                - term [ref=e247]: Candidate digest
                - definition [ref=e248]: fixture-historical-digest
              - generic [ref=e249]:
                - term [ref=e250]: Available from · UTC
                - definition [ref=e251]: 2026-01-01T00:00:00.0000001Z
              - generic [ref=e252]:
                - term [ref=e253]: Available until · UTC
                - definition [ref=e254]: No scheduled end
              - generic [ref=e255]:
                - term [ref=e256]: Start boundary
                - definition [ref=e257]: Inclusive
              - generic [ref=e258]:
                - term [ref=e259]: Approver reference
                - definition [ref=e260]: fixture-other-approver
              - generic [ref=e261]:
                - term [ref=e262]: Approval evidence reference
                - definition [ref=e263]: fixture-actual-approval
              - generic [ref=e264]:
                - term [ref=e265]: Publisher reference
                - definition [ref=e266]: fixture-publisher
              - generic [ref=e267]:
                - term [ref=e268]: Governance version
                - definition [ref=e269]: fixture-governance-v1
            - button "Republish approved version" [ref=e270] [cursor=pointer]
```

# Test source

```ts
  229 |   await page.setViewportSize({width,height:width===390?844:1024});await select(page,'Authentication',false);const search=page.getByLabel('Search candidates',{exact:true}),submit=page.getByRole('button',{name:'Search candidates',exact:true}),error=page.locator('#configuration-search-error');await expect(search).toBeEnabled();await expect(search).toHaveAttribute('maxlength','64');await search.evaluate(input=>input.removeAttribute('maxlength'));
  230 |   const beforeQueries=queries.length,beforeRequests=requests.length;await search.fill('x'.repeat(65));await expect(search).toHaveValue('x'.repeat(65));await search.press('Tab');await expect(search).toHaveAttribute('aria-invalid','true');await expect(search).toHaveAttribute('aria-describedby',/(?:^| )configuration-search-error(?: |$)/);await expect(error).toBeVisible();await expect(error).toHaveText(labels.validationSummary);await expect(error).not.toContainText(labels.invalidNumber);await submit.focus();await expect(submit).toBeFocused();await page.keyboard.press('Enter');await expect(search).toBeFocused();expect(queries).toHaveLength(beforeQueries);expect(requests).toHaveLength(beforeRequests);expect(server.writes).toHaveLength(0);await expect(page.locator(':invalid')).toHaveCount(0);
  231 |   expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`configuration-search-error-${width}.png`),fullPage:true});
  232 |   const bounded='s'.repeat(64);await search.fill(bounded);await search.press('Tab');await expect(search).not.toHaveAttribute('aria-invalid','true');await expect(error).toHaveCount(0);await submit.focus();await page.keyboard.press('Enter');await expect.poll(()=>queries.length).toBe(beforeQueries+1);expect(queries.at(-1)).toEqual({scope:scopes.Authentication,search:bounded,pageSize:5,offset:0});await expect(search).toBeEnabled();await expect(search).toHaveValue(bounded);
  233 |   const unavailable='fixture-unavailable-'+width;await search.fill(unavailable);await submit.focus();await page.keyboard.press('Enter');await expect.poll(()=>queries.length).toBe(beforeQueries+2);expect(queries.at(-1)).toEqual({scope:scopes.Authentication,search:unavailable,pageSize:5,offset:0});await expect(page.getByText(labels.unavailable,{exact:true})).toBeVisible();await expect(search).toHaveValue(unavailable);await expect(page.getByText('fixture-private-server-detail',{exact:true})).toHaveCount(0);expect(server.writes).toHaveLength(0);expect(new URL(page.url()).search).toBe('');expect(new URL(page.url()).hash).toBe('');expect(page.url()).not.toContain(unavailable);
  234 |  });
  235 | });
  236 | function passkeyCandidate(baseline=false):Extract<Candidate,{family:'Passkey'}>{const value=candidate('Passkey',baseline);if(value.family!=='Passkey')throw new Error('Wrong synthetic Passkey family');return value;}
  237 | function passkeyFields(value:ReturnType<typeof passkeyCandidate>):Record<string,string>{
  238 |  const m=value.value.metadata,v=value.value.values,fields:Record<string,string>={'metadata.version':m.versionId,'metadata.status':m.status,'metadata.supersedes':m.supersedesVersionId??'—','metadata.scope':m.scope,'evidence.0':m.approvalEvidenceReferences[0]!,'interval.from':m.effectiveInterval.from,'interval.includesFrom':String(m.effectiveInterval.includesFrom),'interval.until':m.effectiveInterval.until??'—','interval.includesUntil':String(m.effectiveInterval.includesUntil)};
  239 |  for(const [key,item] of Object.entries(v))if(key!=='allowedAlgorithms'&&key!=='assuranceMappings')fields['values.'+key]=String(item);v.allowedAlgorithms.forEach((item,index)=>{fields['algorithm.'+index]=String(item);});for(const [key,item] of Object.entries(v.assuranceMappings))fields['method.'+key]=item;return fields;
  240 | }
  241 | async function assertPasskeyDiff(page:Page,value:ReturnType<typeof passkeyCandidate>,column:'current'|'proposed',other?:ReturnType<typeof passkeyCandidate>){
  242 |  const fields=passkeyFields(value),keys=[...new Set([...Object.keys(fields),...Object.keys(other?passkeyFields(other):{})])].sort();expect((await page.locator('.configuration-diff [data-field]').evaluateAll(rows=>rows.map(row=>row.getAttribute('data-field')))).sort()).toEqual(keys);
  243 |  for(const [key,item] of Object.entries(fields))await expect(page.locator('[data-field="'+key+'"]').locator('td').nth(column==='current'?0:1)).toContainText(item);
  244 |  const effect=page.locator('[data-field="values.revocationSessionEffect"]').locator('td').nth(column==='current'?0:1),effectLabel=value.value.values.revocationSessionEffect===1?'preserveExistingSessions':'revokeCredentialSessions';await expect(effect).toContainText(labels[effectLabel]);await expect(effect.locator('[data-content-key="admin.identity.configuration.'+effectLabel+'"][data-content-publication="fixture-chrome-p1"]')).toHaveCount(1);
  245 |  // Historical as well as current/proposed previews must disclose the three constructor invariants.
  246 |  for(const [key,item] of Object.entries({residentKeyRequirement:'required',userVerificationRequirement:'required',attestationConveyancePreference:'none'}))await expect(page.locator('[data-field="values.'+key+'"]').locator('td').nth(column==='current'?0:1)).toContainText(item);
  247 | }
  248 | for(const width of [1440,390])test(`Passkey typed policy validates ordered inputs, retries exact intent and reviews immutable history at ${width}`,async({page},info)=>{
  249 |  test.setTimeout(90_000);await page.setViewportSize({width,height:width===1440?1024:844});const server=await fixture(page,{unknownOnce:true});await select(page,'Passkey');const save=page.getByRole('button',{name:'Save draft',exact:true}),panel=page.locator('.configuration-fixed');await expect(panel).toHaveCSS('background-color','rgb(247, 249, 250)');await expect(page.locator('.configuration-fixed + .configuration-fields #configuration-version')).toHaveCount(1);
  250 |  for(const [key,item] of Object.entries({residentKeyRequirement:'required',userVerificationRequirement:'required',attestationConveyancePreference:'none'})){await expect(panel.locator('dt').filter({hasText:labels[key as ConfigurationLabel]}).locator('..').locator('dd')).toHaveText(item);await expect(page.locator('[name="'+key+'"]')).toHaveCount(0);}
  251 |  const invalid=async(name:string,bad:string,corrected:string)=>{const field=page.locator('[name="'+name+'"]');await field.fill(bad);await field.press('Tab');await expect(field).toHaveAttribute('aria-invalid','true');await save.focus();await page.keyboard.press('Enter');await expect(field).toBeFocused();expect(server.writes).toHaveLength(0);await save.evaluate(button=>(button as HTMLButtonElement).form!.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})));expect(server.writes).toHaveLength(0);await expect(page.getByRole('alert')).toContainText(labels.validationSummary);await expect(page.locator(':invalid')).toHaveCount(0);await field.fill(corrected);await field.press('Tab');await expect(field).not.toHaveAttribute('aria-invalid','true');};
  252 |  await invalid('origin','https://fixture-user:fixture-password@login.fixture.example.test','https://login.fixture.example.test');await invalid('origin','https://unrelated.example.test','https://login.fixture.example.test');await invalid('authenticatorTimeout','121','30.0000001');await invalid('challengeBytes','15','32');await invalid('algorithm.0','-7.5','-257');await invalid('algorithm.0','2147483648','-257');await invalid('assurance.0','AAL3','fixture-passkey-assurance');
  253 |  const secondKey=page.locator('[name="method.1"]');await secondKey.selectOption(passkeyFactKeys[0]);await secondKey.press('Tab');await expect(secondKey).toHaveAttribute('aria-invalid','true');await save.focus();await page.keyboard.press('Enter');await expect(secondKey).toBeFocused();expect(server.writes).toHaveLength(0);await secondKey.selectOption(passkeyFactKeys[1]);await secondKey.press('Tab');await expect(secondKey).not.toHaveAttribute('aria-invalid','true');
  254 |  await page.locator('[name="algorithm.0"]').fill('-999');await page.locator('[name="algorithm.0"]').press('Tab');await expect(page.locator('[name="algorithm.1"]')).toHaveValue('-7');await expect(page.locator('[name="authenticatorTimeout"]')).toHaveValue('30.0000001');expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`passkey-edit-${width}.png`),fullPage:true});
  255 |  const expected=passkeyCandidate();expected.value.values.allowedAlgorithms=[-999,-7];await save.dblclick();await expect(page.getByRole('button',{name:'Retry same request',exact:true})).toBeVisible();expect(server.writes).toHaveLength(1);expect(server.writes[0]).toMatchObject({path:'/passkey/draft',body:{proposalReference:proposalId,expectedRevision:1}});expect(server.writes[0]!.body.candidate).toEqual(expected.value);expect(Object.keys((server.writes[0]!.body.candidate as typeof expected.value).values)).toHaveLength(20);await expect(page.locator('[name="algorithm.0"]')).toHaveValue('-999');await page.getByRole('button',{name:'Retry same request',exact:true}).click();await expect(page.getByText(labels.replayed,{exact:true})).toBeVisible();expect(server.writes[1]!.body).toEqual(server.writes[0]!.body);expect(server.revision).toBe(2);
  256 |  await page.getByRole('button',{name:'Validate candidate',exact:true}).click();await page.getByRole('button',{name:'Review configuration',exact:true}).click();await expect(page.getByRole('button',{name:'Approve candidate',exact:true})).toBeEnabled();await page.getByLabel('Compare with published version',{exact:true}).selectOption(publicationId);const baseline=passkeyCandidate(true);await assertPasskeyDiff(page,expected,'proposed',baseline);await assertPasskeyDiff(page,baseline,'current',expected);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`passkey-review-${width}.png`),fullPage:true});
  257 |  await page.getByRole('button',{name:'Approve candidate',exact:true}).click();await expect(page.getByRole('button',{name:'Publish configuration',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Publish configuration',exact:true}).click();await fillPublication(page);await page.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);expect(server.writes[4]).toMatchObject({path:'/proposals/publish',body:{proposalReference:proposalId,expectedRevision:4,supersedesPublicationReference:publicationId}});
  258 |  await page.getByRole('link',{name:'Publication history',exact:true}).click();await expect(page.locator('main[aria-busy="false"] .configuration-history')).toHaveCount(1);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`passkey-history-${width}.png`),fullPage:true});await page.getByRole('button',{name:'Republish approved version',exact:true}).click();const dialog=page.getByRole('dialog');await expect(dialog).toBeVisible();await dialog.locator('summary').click();await assertPasskeyDiff(page,baseline,'proposed');await dialog.getByRole('button',{name:'Confirm publication',exact:true}).focus();await page.keyboard.press('Enter');await expect(dialog.getByLabel('Available from · UTC',{exact:true})).toBeFocused();await fillPublication(page);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.keyboard.press('Escape');await expect(dialog).toHaveCount(0);await expect(page.getByRole('button',{name:'Republish approved version',exact:true})).toBeFocused();expect(server.writes).toHaveLength(5);await page.getByRole('button',{name:'Republish approved version',exact:true}).click();await fillPublication(page);await page.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();await expect.poll(()=>server.writes.length).toBe(6);expect(server.writes.map(write=>write.path)).toEqual(['/passkey/draft','/passkey/draft','/proposals/validate','/proposals/approve','/proposals/publish','/rollback']);expect(server.writes[5]).toMatchObject({path:'/rollback',body:{approvedVersionReference:versionId,supersedesPublicationReference:publicationId}});
  259 |  expect(new URL(page.url()).search).toBe('');expect(new URL(page.url()).hash).toBe('');expect(page.url()).not.toContain(proposalId);expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
  260 | });
  261 | test('Passkey backend validation preserves owner inputs and shared error presentation',async({page},info)=>{
  262 |  for(const width of [1440,390]){
  263 |   await page.setViewportSize({width,height:width===1440?1024:844});await page.unrouteAll();const server=await fixture(page);const submitted:unknown[]=[];
  264 |   await page.route('**/identity/configuration/passkey/draft',async route=>{submitted.push(route.request().postDataJSON());await route.fulfill({status:400,contentType:'application/json',body:JSON.stringify({error_code:'VALIDATION_ERROR',detail:'fixture-private-validation-detail'})});});
  265 |   await select(page,'Passkey');const version=page.locator('[name="version"]'),save=page.getByRole('button',{name:'Save draft',exact:true});
  266 |   await version.fill('');await version.press('Tab');await save.focus();await page.keyboard.press('Enter');await expect(version).toBeFocused();await expect(page.getByRole('alert')).toContainText(labels.validationSummary);expect(submitted).toHaveLength(0);await page.screenshot({path:info.outputPath(`passkey-required-error-${width}.png`),fullPage:true});
  267 |   await version.fill(passkeyCandidate().value.metadata.versionId);await version.press('Tab');const inputs=()=>page.locator('main form input,main form select').evaluateAll(elements=>elements.map(element=>[(element as HTMLInputElement).name,(element as HTMLInputElement).value]));const entered=await inputs();await save.click();
  268 |   await expect(page.getByRole('alert')).toContainText(labels.validationSummary);await expect(save).toBeEnabled();expect(submitted).toHaveLength(1);expect((submitted[0] as {candidate:unknown}).candidate).toEqual(passkeyCandidate().value);expect(await inputs()).toEqual(entered);expect(server.writes).toHaveLength(0);
  269 |   await expect(page.getByRole('button',{name:'Retry same request',exact:true})).toHaveCount(0);await expect(page.getByText('fixture-private-validation-detail',{exact:true})).toHaveCount(0);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.screenshot({path:info.outputPath(`passkey-backend-validation-${width}.png`),fullPage:true});
  270 |  }
  271 | });
  272 | test('Passkey canonical routes guard direct entry refresh history and server capabilities',async({page})=>{
  273 |  test.setTimeout(90_000);const server=await fixture(page,{denyPublish:true});for(const view of ['edit','review','history'] as const){const path=configurationPath('Passkey',view),before=server.guards;await page.goto(path+'?candidate=discard#discard');await expect(page).toHaveURL('http://127.0.0.1:5298'+path);await expect.poll(()=>server.guards).toBeGreaterThan(before);await expect(page.locator('[data-tunner-screen="A-ID-SETTINGS-Passkey-'+view+'"]')).toBeVisible();await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');await page.getByLabel('Exact scope',{exact:true}).selectOption(scopeKey(scopes.Passkey));if(view!=='history'){await page.getByLabel('Saved candidate',{exact:true}).selectOption(proposalId);if(view==='edit')await expect(page.getByRole('button',{name:'Save draft',exact:true})).toBeEnabled();else await expect(page.getByRole('button',{name:'Publish configuration',exact:true})).toBeDisabled();}else await expect(page.locator('.configuration-history')).toHaveCount(1);const previous=server.guards;await page.reload();await expect.poll(()=>server.guards).toBeGreaterThan(previous);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');await expect(page.locator('.configuration-diff')).toHaveCount(0);const alternate=view==='edit'?'review':'edit';await page.getByRole('link',{name:labels[alternate],exact:true}).click();await expect(page).toHaveURL('http://127.0.0.1:5298'+configurationPath('Passkey',alternate));const beforeBack=server.guards;await page.goBack();await expect(page).toHaveURL('http://127.0.0.1:5298'+path);await expect.poll(()=>server.guards).toBeGreaterThan(beforeBack);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');const beforeForward=server.guards;await page.goForward();await expect(page).toHaveURL('http://127.0.0.1:5298'+configurationPath('Passkey',alternate));await expect.poll(()=>server.guards).toBeGreaterThan(beforeForward);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');}expect(server.writes).toHaveLength(0);expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
  274 | });
  275 | test('Passkey laptop tablet and narrow form reflows with shared keyboard error correction',async({page},info)=>{
  276 |  test.setTimeout(120_000);const server=await fixture(page);for(const viewport of [{width:1024,height:768},{width:768,height:1024},{width:320,height:800}]){await page.setViewportSize(viewport);await select(page,'Passkey');const field=page.locator('[name="algorithm.0"]');await field.fill('');await field.press('Tab');await expect(field).toHaveAttribute('aria-invalid','true');await page.getByRole('button',{name:'Save draft',exact:true}).focus();await page.keyboard.press('Enter');await expect(field).toBeFocused();await expect(page.getByRole('alert')).toContainText(labels.validationSummary);expect(server.writes).toHaveLength(0);await field.fill('-257');await field.press('Tab');await expect(field).not.toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveCount(0);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);expect(await page.locator('input:visible,select:visible,button:visible').evaluateAll(elements=>elements.every(element=>{const b=element.getBoundingClientRect();return b.width>0&&b.left>=-0.5&&b.right<=innerWidth+0.5;}))).toBe(true);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);if(viewport.width!==1024)await page.screenshot({path:info.outputPath(`passkey-edit-${viewport.width}.png`),fullPage:true});}expect(server.writes).toHaveLength(0);
  277 | });
  278 | test('Passkey conditional publication preserves all six earlier families without new chrome',async({page})=>{
  279 |  const server=await fixture(page,{sixOnly:true});for(const family of ['Authentication','Assurance','AuthenticationContent','Registration','Totp','RegistrationContent'] as const){await select(page,family);await expect(page.getByRole('button',{name:'Save draft',exact:true})).toBeEnabled();await expect(page.getByRole('link',{name:labels.Passkey,exact:true})).toHaveCount(0);await expect(page.locator('[data-content-key="admin.identity.configuration.title"]').first()).toHaveAttribute('data-content-publication','fixture-chrome-p1');}await page.goto(configurationPath('Passkey'));await expect(page.getByRole('alert')).toContainText('The request could not be completed.');await expect(page.getByRole('button',{name:'Save draft',exact:true})).toHaveCount(0);expect(server.writes).toHaveLength(0);await page.unrouteAll();await fixture(page);await select(page,'Passkey',false);for(const input of await page.locator('main form input').all())await expect(input).toHaveValue('');for(const selectInput of await page.locator('main form select').all())await expect(selectInput).toHaveValue('');await page.getByRole('button',{name:'Save draft',exact:true}).click();await expect(page.locator('[name="version"]')).toBeFocused();
  280 | });
  281 | type FactorContentFamily='TotpContent'|'PasskeyContent';
  282 | function factorCandidate(family:FactorContentFamily,baseline=false):Extract<Candidate,{family:FactorContentFamily}>{const value=candidate(family,baseline);if(value.family!=='TotpContent'&&value.family!=='PasskeyContent')throw new Error('Wrong synthetic factor binding');return value;}
  283 | function factorFields(value:ReturnType<typeof factorCandidate>):Record<string,string>{const fields:Record<string,string>={};for(const [key,item] of Object.entries(value.value))if(key!=='effectiveInterval')fields[key]=String(item);const i=value.value.effectiveInterval;return {...fields,'interval.from':i.from,'interval.includesFrom':String(i.includesFrom),'interval.until':i.until??'—','interval.includesUntil':String(i.includesUntil)};}
  284 | async function assertFactorDiff(page:Page,value:ReturnType<typeof factorCandidate>,column:'current'|'proposed'){const fields=factorFields(value),rows=page.locator('.configuration-diff [data-field]');await expect(rows).toHaveCount(9);expect((await rows.evaluateAll(items=>items.map(item=>item.getAttribute('data-field')))).sort()).toEqual(Object.keys(fields).sort());for(const [key,text] of Object.entries(fields))await expect(page.locator('[data-field="'+key+'"]').locator('td').nth(column==='current'?0:1)).toContainText(text);}
  285 | for(const family of ['TotpContent','PasskeyContent'] as const)for(const width of [1440,390])test(`${family} content binding validates preserves rejected input retries and reviews exact history at ${width}`,async({page},info)=>{
  286 |  test.setTimeout(90_000);await page.setViewportSize({width,height:width===1440?1024:844});const server=await fixture(page,{unknownOnce:true}),path=family==='TotpContent'?'/totp-content/draft':'/passkey-content/draft',policyKey=family==='TotpContent'?'totpPolicyVersion':'passkeyPolicyVersion',policyVersion=family==='TotpContent'?scopes.TotpContent.totpPolicyVersion!:scopes.PasskeyContent.passkeyPolicyVersion!;let reject=true;const rejected:Record<string,unknown>[]=[];
  287 |  await page.route('**/identity/configuration'+path,async route=>{if(!reject)return route.fallback();reject=false;rejected.push(route.request().postDataJSON() as Record<string,unknown>);expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-configuration-csrf');return route.fulfill({status:400,contentType:'application/json',body:JSON.stringify({error_code:'VALIDATION_ERROR',detail:'fixture-private-factor-detail'})});});
  288 |  await select(page,family);const save=page.getByRole('button',{name:'Save draft',exact:true}),panel=page.locator('.configuration-fixed'),version=page.locator('[name="configurationVersionReference"]');await expect(panel).toHaveCount(1);await expect(panel).toHaveCSS('background-color','rgb(247, 249, 250)');await expect(panel.getByText(policyVersion,{exact:true})).toBeVisible();await expect(panel.getByText(labels[policyKey],{exact:true})).toBeVisible();await expect(page.locator('.configuration-fixed + .configuration-fields #configuration-configurationVersionReference')).toHaveCount(1);
  289 |  await expect(page.locator('[name="'+policyKey+'"],input[name="version"],input[name="evidence.0"],input[name="termsUri"],input[name="privacyUri"],input[name="supportUri"],input[name="termsVersion"],input[name="privacyVersion"]')).toHaveCount(0);const original=await version.inputValue();await version.fill('');await version.press('Tab');await expect(version).toHaveAttribute('aria-invalid','true');await expect(version).toHaveAttribute('aria-describedby',/(?:^| )configuration-configurationVersionReference-error(?: |$)/);await save.focus();await page.keyboard.press('Enter');await expect(version).toBeFocused();await expect(page.getByRole('alert')).toContainText(labels.validationSummary);expect(rejected).toHaveLength(0);expect(server.writes).toHaveLength(0);await save.evaluate(button=>(button as HTMLButtonElement).form!.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})));expect(rejected).toHaveLength(0);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-required-error-${width}.png`),fullPage:true});
  290 |  await version.fill(original);await version.press('Tab');await expect(version).not.toHaveAttribute('aria-invalid','true');await expect(page.locator(':invalid')).toHaveCount(0);const content=page.locator('[name="contentScope"]');await content.fill('fixture-reviewed-factor-scope');const inputs=()=>page.locator('main form input,main form select').evaluateAll(items=>items.map(item=>[(item as HTMLInputElement).name,(item as HTMLInputElement).value]));const entered=await inputs();await save.click();await expect(page.getByRole('alert')).toContainText(labels.validationSummary);await expect(save).toBeEnabled();expect(rejected).toHaveLength(1);expect(server.writes).toHaveLength(0);expect(await inputs()).toEqual(entered);await expect(page.getByRole('button',{name:'Retry same request',exact:true})).toHaveCount(0);await expect(page.getByText('fixture-private-factor-detail',{exact:true})).toHaveCount(0);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);
  291 |  await page.locator('[name="variantReference"]').fill('fixture-reviewed-factor-en');await page.locator('[name="variantReference"]').press('Tab');expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-edit-${width}.png`),fullPage:true});const expected=factorCandidate(family);expected.value.contentScope='fixture-reviewed-factor-scope';expected.value.variantReference='fixture-reviewed-factor-en';await save.dblclick();await expect(page.getByRole('button',{name:'Retry same request',exact:true})).toBeVisible();expect(server.writes).toHaveLength(1);expect(server.writes[0]).toMatchObject({path,body:{proposalReference:proposalId,expectedRevision:1}});expect(server.writes[0]!.body.candidate).toEqual(expected.value);expect(Object.keys(server.writes[0]!.body.candidate as object).sort()).toEqual([policyKey,'configurationVersionReference','qualificationReference','contentScope','variantReference','effectiveInterval'].sort());expect(server.writes[0]!.body.idempotencyKey).not.toBe(rejected[0]!.idempotencyKey);await expect(content).toHaveValue('fixture-reviewed-factor-scope');await page.getByRole('button',{name:'Retry same request',exact:true}).click();await expect(page.getByText(labels.replayed,{exact:true})).toBeVisible();expect(server.writes[1]!.body).toEqual(server.writes[0]!.body);expect(server.revision).toBe(2);
  292 |  await page.getByRole('button',{name:'Validate candidate',exact:true}).click();await page.getByRole('button',{name:'Review configuration',exact:true}).click();await expect(page.getByRole('button',{name:'Approve candidate',exact:true})).toBeEnabled();await page.getByLabel('Compare with published version',{exact:true}).selectOption(publicationId);const baseline=factorCandidate(family,true);await assertFactorDiff(page,expected,'proposed');await assertFactorDiff(page,baseline,'current');expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-review-${width}.png`),fullPage:true});await page.getByRole('button',{name:'Approve candidate',exact:true}).click();await expect(page.getByRole('button',{name:'Publish configuration',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Publish configuration',exact:true}).click();await fillPublication(page);await page.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);expect(server.writes[4]).toMatchObject({path:'/proposals/publish',body:{proposalReference:proposalId,expectedRevision:4,supersedesPublicationReference:publicationId,effectiveInterval:{from:'2099-01-01T00:00:00.0000001Z',includesFrom:false,until:null,includesUntil:false}}});
  293 |  await page.getByRole('link',{name:'Publication history',exact:true}).click();await expect(page.locator('main[aria-busy="false"] .configuration-history')).toHaveCount(1);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-history-${width}.png`),fullPage:true});await page.getByRole('button',{name:'Republish approved version',exact:true}).click();const dialog=page.getByRole('dialog');await expect(dialog).toBeVisible();await dialog.locator('summary').click();await assertFactorDiff(page,baseline,'proposed');await dialog.getByRole('button',{name:'Confirm publication',exact:true}).focus();await page.keyboard.press('Enter');await expect(dialog.getByLabel('Available from · UTC',{exact:true})).toBeFocused();await fillPublication(page);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.keyboard.press('Escape');await expect(dialog).toHaveCount(0);await expect(page.getByRole('button',{name:'Republish approved version',exact:true})).toBeFocused();expect(server.writes).toHaveLength(5);await page.getByRole('button',{name:'Republish approved version',exact:true}).click();await fillPublication(page);await page.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();await expect.poll(()=>server.writes.length).toBe(6);expect(server.writes.map(write=>write.path)).toEqual([path,path,'/proposals/validate','/proposals/approve','/proposals/publish','/rollback']);expect(server.writes[5]).toMatchObject({path:'/rollback',body:{approvedVersionReference:versionId,supersedesPublicationReference:publicationId,effectiveInterval:{from:'2099-01-01T00:00:00.0000001Z',includesFrom:false,until:null,includesUntil:false}}});
  294 |  expect(new URL(page.url()).search).toBe('');expect(new URL(page.url()).hash).toBe('');expect(page.url()).not.toContain(proposalId);expect(page.url()).not.toContain(policyVersion);expect(page.url()).not.toContain('fixture-reviewed-factor-scope');expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);await expect(page.locator('[data-content-key="admin.identity.configuration.title"]').first()).toHaveAttribute('data-content-publication','fixture-chrome-p1');
  295 | });
  296 | test('factor content six canonical routes independently guard direct entry reload and browser history',async({page})=>{
  297 |  test.setTimeout(120_000);const server=await fixture(page,{denyPublish:true});for(const family of ['TotpContent','PasskeyContent'] as const)for(const view of ['edit','review','history'] as const){const path=configurationPath(family,view),before=server.guards;await page.goto(path+'?candidate=discard#discard');await expect(page).toHaveURL('http://127.0.0.1:5298'+path);await expect.poll(()=>server.guards).toBeGreaterThan(before);await expect(page.locator('[data-tunner-screen="A-ID-SETTINGS-'+family+'-'+view+'"]')).toBeVisible();await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');await page.getByLabel('Exact scope',{exact:true}).selectOption(scopeKey(scopes[family]));if(view!=='history'){await page.getByLabel('Saved candidate',{exact:true}).selectOption(proposalId);if(view==='edit')await expect(page.getByRole('button',{name:'Save draft',exact:true})).toBeEnabled();else await expect(page.getByRole('button',{name:'Publish configuration',exact:true})).toBeDisabled();}else await expect(page.locator('.configuration-history')).toHaveCount(1);const beforeReload=server.guards;await page.reload();await expect.poll(()=>server.guards).toBeGreaterThan(beforeReload);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');await expect(page.locator('.configuration-diff')).toHaveCount(0);const alternate=view==='edit'?'review':'edit';await page.getByRole('link',{name:labels[alternate],exact:true}).click();await expect(page).toHaveURL('http://127.0.0.1:5298'+configurationPath(family,alternate));const beforeBack=server.guards;await page.goBack();await expect(page).toHaveURL('http://127.0.0.1:5298'+path);await expect.poll(()=>server.guards).toBeGreaterThan(beforeBack);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');const beforeForward=server.guards;await page.goForward();await expect(page).toHaveURL('http://127.0.0.1:5298'+configurationPath(family,alternate));await expect.poll(()=>server.guards).toBeGreaterThan(beforeForward);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');expect(new URL(page.url()).search).toBe('');expect(new URL(page.url()).hash).toBe('');}expect(server.writes).toHaveLength(0);expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
  298 | });
  299 | test('factor content laptop tablet and narrow forms preserve keyboard correction and reflow without writes',async({page},info)=>{
  300 |  test.setTimeout(120_000);const server=await fixture(page);for(const viewport of [{width:1024,height:768},{width:768,height:1024},{width:320,height:800}])for(const family of ['TotpContent','PasskeyContent'] as const){await page.setViewportSize(viewport);await select(page,family);const field=page.locator('[name="qualificationReference"]'),original=await field.inputValue();await field.fill('');await field.press('Tab');await expect(field).toHaveAttribute('aria-invalid','true');await page.getByRole('button',{name:'Save draft',exact:true}).focus();await page.keyboard.press('Enter');await expect(field).toBeFocused();await expect(page.getByRole('alert')).toContainText(labels.validationSummary);expect(server.writes).toHaveLength(0);await field.fill(original);await field.press('Tab');await expect(field).not.toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveCount(0);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);expect(await page.locator('input:visible,select:visible,button:visible').evaluateAll(items=>items.every(item=>{const bounds=item.getBoundingClientRect();return bounds.width>0&&bounds.left>=-0.5&&bounds.right<=innerWidth+0.5;}))).toBe(true);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);if(viewport.width!==1024)await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-edit-${viewport.width}.png`),fullPage:true});}expect(server.writes).toHaveLength(0);
  301 | });
  302 | test('factor content conditional chrome preserves earlier seven family publications and blank owner input',async({page})=>{
  303 |  const server=await fixture(page,{sevenOnly:true});for(const family of ['Authentication','Assurance','AuthenticationContent','Registration','Totp','RegistrationContent','Passkey'] as const){await select(page,family);await expect(page.getByRole('button',{name:'Save draft',exact:true})).toBeEnabled();for(const title of [labels.TotpContent,labels.PasskeyContent])await expect(page.getByRole('link',{name:title,exact:true})).toHaveCount(0);}for(const family of ['TotpContent','PasskeyContent'] as const){await page.goto(configurationPath(family));await expect(page.getByRole('alert')).toContainText('The request could not be completed.');await expect(page.getByRole('button',{name:'Save draft',exact:true})).toHaveCount(0);}expect(server.writes).toHaveLength(0);await page.unrouteAll();const qualified=await fixture(page);for(const family of ['TotpContent','PasskeyContent'] as const){await select(page,family,false);for(const input of await page.locator('main form input').all())await expect(input).toHaveValue('');for(const select of await page.locator('main form select').all())await expect(select).toHaveValue('');await page.getByRole('button',{name:'Save draft',exact:true}).click();await expect(page.locator('[name="configurationVersionReference"]')).toBeFocused();}expect(qualified.writes).toHaveLength(0);
  304 | });
  305 | function stepUpCandidate(baseline=false):Extract<Candidate,{family:'StepUpContent'}>{const value=candidate('StepUpContent',baseline);if(value.family!=='StepUpContent')throw new Error('Wrong synthetic StepUp binding');return value;}
  306 | async function assertStepUpDiff(page:Page,value:ReturnType<typeof stepUpCandidate>,column:'current'|'proposed'){const v=value.value,i=v.effectiveInterval,fields:Record<string,string>={'assuranceScope.operation':v.assuranceScope.operation,'assuranceScope.context':v.assuranceScope.context,assurancePolicyVersion:v.assurancePolicyVersion,configurationVersionReference:v.configurationVersionReference,qualificationReference:v.qualificationReference,contentScope:v.contentScope,variantReference:v.variantReference,'interval.from':i.from,'interval.includesFrom':String(i.includesFrom),'interval.until':i.until??'—','interval.includesUntil':String(i.includesUntil)};const rows=page.locator('.configuration-diff [data-field]');await expect(rows).toHaveCount(11);expect((await rows.evaluateAll(items=>items.map(item=>item.getAttribute('data-field')))).sort()).toEqual(Object.keys(fields).sort());for(const [key,text] of Object.entries(fields))await expect(page.locator('[data-field="'+key+'"]').locator('td').nth(column==='current'?0:1)).toContainText(text);}
  307 | for(const width of [1440,390])test(`StepUpContent binding validates preserves rejected input retries and reviews exact history at ${width}`,async({page},info)=>{
  308 |  test.setTimeout(90_000);const family='StepUpContent' as const;await page.setViewportSize({width,height:width===1440?1024:844});const server=await fixture(page,{unknownOnce:true}),path='/step-up-content/draft',policyKey='assurancePolicyVersion',policyVersion=scopes.StepUpContent.assurancePolicyVersion!;let reject=true;const rejected:Record<string,unknown>[]=[];
  309 |  await page.route('**/identity/configuration'+path,async route=>{if(!reject)return route.fallback();reject=false;rejected.push(route.request().postDataJSON() as Record<string,unknown>);expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-configuration-csrf');return route.fulfill({status:400,contentType:'application/json',body:JSON.stringify({error_code:'VALIDATION_ERROR',detail:'fixture-private-step-up-detail'})});});
  310 |  await select(page,family);const save=page.getByRole('button',{name:'Save draft',exact:true}),panel=page.locator('.configuration-fixed'),version=page.locator('[name="configurationVersionReference"]');await expect(panel).toHaveCount(1);await expect(panel).toHaveCSS('background-color','rgb(247, 249, 250)');await expect(panel.getByText(policyVersion,{exact:true})).toBeVisible();await expect(panel.getByText(scopes.StepUpContent.assuranceScope!.operation,{exact:true})).toBeVisible();await expect(panel.getByText(scopes.StepUpContent.assuranceScope!.context,{exact:true})).toBeVisible();await expect(page.locator('[name="assuranceScope.operation"],[name="assuranceScope.context"],[name="method.0"],[name="requiredAssurance"]')).toHaveCount(0);await expect(panel.getByText(labels[policyKey],{exact:true})).toBeVisible();await expect(page.locator('.configuration-fixed + .configuration-fields #configuration-configurationVersionReference')).toHaveCount(1);
  311 |  await expect(page.locator('[name="'+policyKey+'"],input[name="version"],input[name="evidence.0"],input[name="termsUri"],input[name="privacyUri"],input[name="supportUri"],input[name="termsVersion"],input[name="privacyVersion"]')).toHaveCount(0);const original=await version.inputValue();await version.fill('');await version.press('Tab');await expect(version).toHaveAttribute('aria-invalid','true');await expect(version).toHaveAttribute('aria-describedby',/(?:^| )configuration-configurationVersionReference-error(?: |$)/);await save.focus();await page.keyboard.press('Enter');await expect(version).toBeFocused();await expect(page.getByRole('alert')).toContainText(labels.validationSummary);expect(rejected).toHaveLength(0);expect(server.writes).toHaveLength(0);await save.evaluate(button=>(button as HTMLButtonElement).form!.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})));expect(rejected).toHaveLength(0);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-required-error-${width}.png`),fullPage:true});
  312 |  await version.fill(original);await version.press('Tab');await expect(version).not.toHaveAttribute('aria-invalid','true');await expect(page.locator(':invalid')).toHaveCount(0);const content=page.locator('[name="contentScope"]');await content.fill('fixture-reviewed-step-up-scope');const inputs=()=>page.locator('main form input,main form select').evaluateAll(items=>items.map(item=>[(item as HTMLInputElement).name,(item as HTMLInputElement).value]));const entered=await inputs();await save.click();await expect(page.getByRole('alert')).toContainText(labels.validationSummary);await expect(save).toBeEnabled();expect(rejected).toHaveLength(1);expect(server.writes).toHaveLength(0);expect(await inputs()).toEqual(entered);await expect(page.getByRole('button',{name:'Retry same request',exact:true})).toHaveCount(0);await expect(page.getByText('fixture-private-step-up-detail',{exact:true})).toHaveCount(0);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);
  313 |  await page.locator('[name="variantReference"]').fill('fixture-reviewed-step-up-en');await page.locator('[name="variantReference"]').press('Tab');expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-edit-${width}.png`),fullPage:true});const expected=stepUpCandidate();expected.value.contentScope='fixture-reviewed-step-up-scope';expected.value.variantReference='fixture-reviewed-step-up-en';await save.dblclick();await expect(page.getByRole('button',{name:'Retry same request',exact:true})).toBeVisible();expect(server.writes).toHaveLength(1);expect(server.writes[0]).toMatchObject({path,body:{proposalReference:proposalId,expectedRevision:1}});expect(server.writes[0]!.body.candidate).toEqual(expected.value);expect(Object.keys(server.writes[0]!.body.candidate as object).sort()).toEqual(['assuranceScope',policyKey,'configurationVersionReference','qualificationReference','contentScope','variantReference','effectiveInterval'].sort());expect(server.writes[0]!.body.idempotencyKey).not.toBe(rejected[0]!.idempotencyKey);await expect(content).toHaveValue('fixture-reviewed-step-up-scope');await page.getByRole('button',{name:'Retry same request',exact:true}).click();await expect(page.getByText(labels.replayed,{exact:true})).toBeVisible();expect(server.writes[1]!.body).toEqual(server.writes[0]!.body);expect(server.revision).toBe(2);
  314 |  await page.getByRole('button',{name:'Validate candidate',exact:true}).click();await page.getByRole('button',{name:'Review configuration',exact:true}).click();await expect(page.getByRole('button',{name:'Approve candidate',exact:true})).toBeEnabled();await page.getByLabel('Compare with published version',{exact:true}).selectOption(publicationId);const baseline=stepUpCandidate(true);await assertStepUpDiff(page,expected,'proposed');await assertStepUpDiff(page,baseline,'current');expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-review-${width}.png`),fullPage:true});await page.getByRole('button',{name:'Approve candidate',exact:true}).click();await expect(page.getByRole('button',{name:'Publish configuration',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Publish configuration',exact:true}).click();await fillPublication(page);await page.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);expect(server.writes[4]).toMatchObject({path:'/proposals/publish',body:{proposalReference:proposalId,expectedRevision:4,supersedesPublicationReference:publicationId,effectiveInterval:{from:'2099-01-01T00:00:00.0000001Z',includesFrom:false,until:null,includesUntil:false}}});
  315 |  await page.getByRole('link',{name:'Publication history',exact:true}).click();await expect(page.locator('main[aria-busy="false"] .configuration-history')).toHaveCount(1);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-history-${width}.png`),fullPage:true});await page.getByRole('button',{name:'Republish approved version',exact:true}).click();const dialog=page.getByRole('dialog');await expect(dialog).toBeVisible();await dialog.locator('summary').click();await assertStepUpDiff(page,baseline,'proposed');await dialog.getByRole('button',{name:'Confirm publication',exact:true}).focus();await page.keyboard.press('Enter');await expect(dialog.getByLabel('Available from · UTC',{exact:true})).toBeFocused();await fillPublication(page);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.keyboard.press('Escape');await expect(dialog).toHaveCount(0);await expect(page.getByRole('button',{name:'Republish approved version',exact:true})).toBeFocused();expect(server.writes).toHaveLength(5);await page.getByRole('button',{name:'Republish approved version',exact:true}).click();await fillPublication(page);await page.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();await expect.poll(()=>server.writes.length).toBe(6);expect(server.writes.map(write=>write.path)).toEqual([path,path,'/proposals/validate','/proposals/approve','/proposals/publish','/rollback']);expect(server.writes[5]).toMatchObject({path:'/rollback',body:{approvedVersionReference:versionId,supersedesPublicationReference:publicationId,effectiveInterval:{from:'2099-01-01T00:00:00.0000001Z',includesFrom:false,until:null,includesUntil:false}}});
  316 |  expect(new URL(page.url()).search).toBe('');expect(new URL(page.url()).hash).toBe('');expect(page.url()).not.toContain(proposalId);expect(page.url()).not.toContain(policyVersion);expect(page.url()).not.toContain(scopes.StepUpContent.assuranceScope!.operation);expect(page.url()).not.toContain(scopes.StepUpContent.assuranceScope!.context);expect(page.url()).not.toContain('fixture-reviewed-step-up-scope');expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);await expect(page.locator('[data-content-key="admin.identity.configuration.title"]').first()).toHaveAttribute('data-content-publication','fixture-chrome-p1');
  317 | });
  318 | test('StepUpContent canonical routes guard direct entry reload and browser history',async({page})=>{
  319 |  test.setTimeout(120_000);const server=await fixture(page,{denyPublish:true}),family='StepUpContent' as const;for(const view of ['edit','review','history'] as const){const path=configurationPath(family,view),before=server.guards;await page.goto(path+'?candidate=discard#discard');await expect(page).toHaveURL('http://127.0.0.1:5298'+path);await expect.poll(()=>server.guards).toBeGreaterThan(before);await expect(page.locator('[data-tunner-screen="A-ID-SETTINGS-'+family+'-'+view+'"]')).toBeVisible();await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');await page.getByLabel('Exact scope',{exact:true}).selectOption(scopeKey(scopes[family]));if(view!=='history'){await page.getByLabel('Saved candidate',{exact:true}).selectOption(proposalId);if(view==='edit')await expect(page.getByRole('button',{name:'Save draft',exact:true})).toBeEnabled();else await expect(page.getByRole('button',{name:'Publish configuration',exact:true})).toBeDisabled();}else await expect(page.locator('.configuration-history')).toHaveCount(1);const beforeReload=server.guards;await page.reload();await expect.poll(()=>server.guards).toBeGreaterThan(beforeReload);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');await expect(page.locator('.configuration-diff')).toHaveCount(0);const alternate=view==='edit'?'review':'edit';await page.getByRole('link',{name:labels[alternate],exact:true}).click();await expect(page).toHaveURL('http://127.0.0.1:5298'+configurationPath(family,alternate));const beforeBack=server.guards;await page.goBack();await expect(page).toHaveURL('http://127.0.0.1:5298'+path);await expect.poll(()=>server.guards).toBeGreaterThan(beforeBack);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');const beforeForward=server.guards;await page.goForward();await expect(page).toHaveURL('http://127.0.0.1:5298'+configurationPath(family,alternate));await expect.poll(()=>server.guards).toBeGreaterThan(beforeForward);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');expect(new URL(page.url()).search).toBe('');expect(new URL(page.url()).hash).toBe('');}expect(server.writes).toHaveLength(0);expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
  320 | });
  321 | test('StepUpContent laptop tablet and narrow form preserves keyboard correction and reflow without writes',async({page},info)=>{
  322 |  test.setTimeout(120_000);const server=await fixture(page),family='StepUpContent' as const;for(const viewport of [{width:1024,height:768},{width:768,height:1024},{width:320,height:800}]){await page.setViewportSize(viewport);await select(page,family);const field=page.locator('[name="qualificationReference"]'),original=await field.inputValue();await field.fill('');await field.press('Tab');await expect(field).toHaveAttribute('aria-invalid','true');await page.getByRole('button',{name:'Save draft',exact:true}).focus();await page.keyboard.press('Enter');await expect(field).toBeFocused();await expect(page.getByRole('alert')).toContainText(labels.validationSummary);expect(server.writes).toHaveLength(0);await field.fill(original);await field.press('Tab');await expect(field).not.toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveCount(0);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);expect(await page.locator('input:visible,select:visible,button:visible').evaluateAll(items=>items.every(item=>{const bounds=item.getBoundingClientRect();return bounds.width>0&&bounds.left>=-0.5&&bounds.right<=innerWidth+0.5;}))).toBe(true);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);if(viewport.width!==1024)await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-edit-${viewport.width}.png`),fullPage:true});}expect(server.writes).toHaveLength(0);
  323 | });
  324 | test('StepUpContent conditional chrome preserves earlier nine family publications and blank owner input',async({page})=>{
  325 |  const server=await fixture(page,{nineOnly:true});for(const family of ['Authentication','Assurance','AuthenticationContent','Registration','Totp','RegistrationContent','Passkey','TotpContent','PasskeyContent'] as const){await select(page,family);await expect(page.getByRole('button',{name:'Save draft',exact:true})).toBeEnabled();await expect(page.getByRole('link',{name:labels.StepUpContent,exact:true})).toHaveCount(0);}await page.goto(configurationPath('StepUpContent'));await expect(page.getByRole('alert')).toContainText('The request could not be completed.');await expect(page.getByRole('button',{name:'Save draft',exact:true})).toHaveCount(0);expect(server.writes).toHaveLength(0);await page.unrouteAll();const qualified=await fixture(page);await select(page,'StepUpContent',false);for(const input of await page.locator('main form input').all())await expect(input).toHaveValue('');for(const select of await page.locator('main form select').all())await expect(select).toHaveValue('');await page.getByRole('button',{name:'Save draft',exact:true}).click();await expect(page.locator('[name="configurationVersionReference"]')).toBeFocused();expect(qualified.writes).toHaveLength(0);
  326 | });
  327 | function recoveryCandidate(baseline=false):Extract<Candidate,{family:'RecoveryCodes'}>{const value=candidate('RecoveryCodes',baseline);if(value.family!=='RecoveryCodes')throw new Error('Wrong synthetic RecoveryCodes family');return value;}
  328 | function recoveryFields(value:ReturnType<typeof recoveryCandidate>):Record<string,string>{const m=value.value.metadata,v=value.value.values,i=m.effectiveInterval,fields:Record<string,string>={'metadata.version':m.versionId,'metadata.status':m.status,'metadata.supersedes':m.supersedesVersionId??'—','metadata.scope':m.scope,'evidence.0':m.approvalEvidenceReferences[0]!,'interval.from':i.from,'interval.includesFrom':String(i.includesFrom),'interval.until':i.until??'—','interval.includesUntil':String(i.includesUntil)};for(const [key,item] of Object.entries(v))if(key!=='crypto')fields['values.'+key]=String(item);for(const [key,item] of Object.entries(v.crypto))fields['values.crypto.'+key]=String(item);return fields;}
> 329 | async function assertRecoveryDiff(page:Page,value:ReturnType<typeof recoveryCandidate>,column:'current'|'proposed'){const fields=recoveryFields(value),rows=page.locator('.configuration-diff [data-field]');await expect(rows).toHaveCount(36);expect((await rows.evaluateAll(items=>items.map(item=>item.getAttribute('data-field')))).sort()).toEqual(Object.keys(fields).sort());for(const [key,text] of Object.entries(fields))await expect(page.locator('[data-field="'+key+'"]').locator('td').nth(column==='current'?0:1)).toContainText(text);const choices={generationAdmission:labels.withinApprovedActiveSetCapacity,replacementMechanism:labels.retireExactExpectedSetsAtIssueCommit,sessionEffect:labels.noSessionMutation,deliveryMechanism:labels.oneVolatileViewWithOptionalSingleLocalDownload};for(const [key,choice] of Object.entries(choices)){const cell=page.locator('[data-field="values.'+key+'"]').locator('td').nth(column==='current'?0:1);await expect(cell).toContainText(choice);await expect(cell).toContainText('(1)');await expect(cell.locator('[data-content-publication="fixture-chrome-p1"]')).toHaveCount(1);}await expect(page.locator('[data-field="values.crypto.maximumHashWork"]').locator('td').nth(column==='current'?0:1)).toHaveText(new RegExp(value.value.values.crypto.maximumHashWork+'$'));}
      |                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       ^ Error: expect(locator).toHaveCount(expected) failed
  330 | for(const width of [1440,390])test(`RecoveryCodes typed policy preserves exact Int64 validation retry and immutable history at ${width}`,async({page},info)=>{
  331 |  test.setTimeout(120_000);await page.setViewportSize({width,height:width===1440?1024:844});const server=await fixture(page,{unknownOnce:true}),rejected:Record<string,unknown>[]=[];let reject=true;await page.route('**/identity/configuration/recovery-codes/draft',async route=>{if(!reject)return route.fallback();reject=false;rejected.push(route.request().postDataJSON() as Record<string,unknown>);return route.fulfill({status:400,contentType:'application/json',body:JSON.stringify({error_code:'VALIDATION_ERROR',detail:'fixture-private-recovery-policy-detail'})});});await select(page,'RecoveryCodes');const save=page.getByRole('button',{name:'Save draft',exact:true}),version=page.locator('[name="version"]'),work=page.locator('[name="crypto.maximumHashWork"]'),panel=page.locator('.configuration-fixed');await expect(panel).toHaveCount(1);await expect(panel).toHaveCSS('background-color','rgb(247, 249, 250)');await expect(page.locator('.configuration-fixed + .configuration-fields #configuration-version')).toHaveCount(1);await expect(work).toHaveAttribute('type','text');await expect(work).toHaveValue('9007199254740995');await expect(page.locator('input[name^="crypto."]')).toHaveCount(8);await expect(page.locator('[name="formatProfileVersion"],[name="generationAdmission"],[name="replacementMechanism"],[name="sessionEffect"],[name="deliveryMechanism"],[name="crypto.nativeIdentityVersion"]')).toHaveCount(0);for(const text of ['NATIVE_HEX32_V1','10.0.13',labels.withinApprovedActiveSetCapacity,labels.retireExactExpectedSetsAtIssueCommit,labels.noSessionMutation,labels.oneVolatileViewWithOptionalSingleLocalDownload])await expect(panel.getByText(text,{exact:true})).toBeVisible();await expect(page.getByRole('button',{name:/generate|reveal|download/i})).toHaveCount(0);
  332 |  await version.fill('');await version.press('Tab');await save.focus();await page.keyboard.press('Enter');await expect(version).toBeFocused();await expect(page.getByRole('alert')).toContainText(labels.validationSummary);expect(rejected).toHaveLength(0);expect(server.writes).toHaveLength(0);await save.evaluate(button=>(button as HTMLButtonElement).form!.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})));expect(rejected).toHaveLength(0);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.screenshot({path:info.outputPath(`recovery-codes-required-error-${width}.png`),fullPage:true});await version.fill('fixture-v2');await version.press('Tab');
  333 |  const invalid=async(name:string,bad:string,corrected:string)=>{const field=page.locator('[name="'+name+'"]');await field.fill(bad);await field.press('Tab');await expect(field).toHaveAttribute('aria-invalid','true');await save.focus();await page.keyboard.press('Enter');await expect(field).toBeFocused();expect(server.writes).toHaveLength(0);expect(rejected).toHaveLength(0);await field.fill(corrected);await field.press('Tab');await expect(field).not.toHaveAttribute('aria-invalid','true');};await invalid('crypto.maximumHashWork','9223372036854775808','9007199254740995');await invalid('crypto.maximumHashWork','0','9007199254740995');await invalid('crypto.maximumHashWork','1e20','9007199254740995');await invalid('crypto.maximumCodesPerSet','4','10');await invalid('crypto.maximumGenerationAttempts','4','10');await invalid('maximumActiveSets','2147483647','2');await invalid('operationLifetime','0','300.0000001');await invalid('deliveryProfileVersion','x'.repeat(257),'fixture-delivery-v1');await invalid('origin','https://fixture-user:fixture-password@recovery.fixture.example.test','https://recovery.fixture.example.test');
  334 |  // This product differs by one beyond Number precision: only integer-exact arithmetic can reject the lower bound.
  335 |  await page.locator('[name="maximumActiveSets"]').fill('1');await page.locator('[name="crypto.maximumCodesPerSet"]').fill('2147483647');await page.locator('[name="crypto.hashIterations"]').fill('2147483647');await invalid('crypto.maximumHashWork','4611686014132420608','4611686014132420609');await page.locator('[name="crypto.maximumCodesPerSet"]').fill('10');await page.locator('[name="crypto.hashIterations"]').fill('10');await page.locator('[name="maximumActiveSets"]').fill('2');await work.fill('9007199254740995');await work.press('Tab');await expect(work).not.toHaveAttribute('aria-invalid','true');await expect(page.locator(':invalid')).toHaveCount(0);
  336 |  const delivery=page.locator('[name="deliveryProfileVersion"]');await delivery.fill('fixture-rejected-delivery-v2');const inputs=()=>page.locator('main form input,main form select').evaluateAll(items=>items.map(item=>[(item as HTMLInputElement).name,(item as HTMLInputElement).value]));const entered=await inputs();await save.click();await expect(page.getByRole('alert')).toContainText(labels.validationSummary);await expect(save).toBeEnabled();expect(rejected).toHaveLength(1);expect(await inputs()).toEqual(entered);expect(server.writes).toHaveLength(0);await expect(page.getByRole('button',{name:'Retry same request',exact:true})).toHaveCount(0);await expect(page.getByText('fixture-private-recovery-policy-detail',{exact:true})).toHaveCount(0);await delivery.fill('fixture-reviewed-delivery-v3');await work.fill('9223372036854775807');await work.press('Tab');await expect(work).toHaveValue('9223372036854775807');await expect(work).not.toHaveAttribute('aria-invalid','true');expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`recovery-codes-edit-${width}.png`),fullPage:true});
  337 |  const expected=recoveryCandidate();expected.value.values.deliveryProfileVersion='fixture-reviewed-delivery-v3';expected.value.values.crypto.maximumHashWork='9223372036854775807';await save.dblclick();await expect(page.getByRole('button',{name:'Retry same request',exact:true})).toBeVisible();expect(server.writes).toHaveLength(1);expect(server.writes[0]).toMatchObject({path:'/recovery-codes/draft',body:{proposalReference:proposalId,expectedRevision:1}});expect(server.writes[0]!.body.candidate).toEqual(expected.value);const posted=server.writes[0]!.body.candidate as typeof expected.value;expect(Object.keys(posted.values)).toHaveLength(19);expect(Object.keys(posted.values.crypto)).toHaveLength(9);expect(typeof posted.values.crypto.maximumHashWork).toBe('string');expect(posted.values.crypto.maximumHashWork).toBe('9223372036854775807');expect(server.writes[0]!.body.idempotencyKey).not.toBe(rejected[0]!.idempotencyKey);await page.getByRole('button',{name:'Retry same request',exact:true}).click();await expect(page.getByText(labels.replayed,{exact:true})).toBeVisible();expect(server.writes[1]!.body).toEqual(server.writes[0]!.body);expect(server.revision).toBe(2);await expect(work).toHaveValue('9223372036854775807');
  338 |  await page.getByRole('button',{name:'Validate candidate',exact:true}).click();await page.getByRole('button',{name:'Review configuration',exact:true}).click();await expect(page.getByRole('button',{name:'Approve candidate',exact:true})).toBeEnabled();await page.getByLabel('Compare with published version',{exact:true}).selectOption(publicationId);const baseline=recoveryCandidate(true);await assertRecoveryDiff(page,expected,'proposed');await assertRecoveryDiff(page,baseline,'current');expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`recovery-codes-review-${width}.png`),fullPage:true});await page.getByRole('button',{name:'Approve candidate',exact:true}).click();await expect(page.getByRole('button',{name:'Publish configuration',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Publish configuration',exact:true}).click();await fillPublication(page);await page.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);expect(server.writes[4]).toMatchObject({path:'/proposals/publish',body:{proposalReference:proposalId,expectedRevision:4,supersedesPublicationReference:publicationId}});
  339 |  await page.getByRole('link',{name:'Publication history',exact:true}).click();await expect(page.locator('main[aria-busy="false"] .configuration-history')).toHaveCount(1);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`recovery-codes-history-${width}.png`),fullPage:true});await page.getByRole('button',{name:'Republish approved version',exact:true}).click();const dialog=page.getByRole('dialog');await expect(dialog).toBeVisible();await dialog.locator('summary').click();await assertRecoveryDiff(page,baseline,'proposed');await dialog.getByRole('button',{name:'Confirm publication',exact:true}).focus();await page.keyboard.press('Enter');await expect(dialog.getByLabel('Available from · UTC',{exact:true})).toBeFocused();await fillPublication(page);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.keyboard.press('Escape');await expect(dialog).toHaveCount(0);await expect(page.getByRole('button',{name:'Republish approved version',exact:true})).toBeFocused();expect(server.writes).toHaveLength(5);await page.getByRole('button',{name:'Republish approved version',exact:true}).click();await fillPublication(page);await page.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();await expect.poll(()=>server.writes.length).toBe(6);expect(server.writes.map(write=>write.path)).toEqual(['/recovery-codes/draft','/recovery-codes/draft','/proposals/validate','/proposals/approve','/proposals/publish','/rollback']);expect(server.writes[5]).toMatchObject({path:'/rollback',body:{approvedVersionReference:versionId,supersedesPublicationReference:publicationId}});expect(new URL(page.url()).search).toBe('');expect(new URL(page.url()).hash).toBe('');expect(page.url()).not.toContain(proposalId);expect(page.url()).not.toContain('9223372036854775807');expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
  340 | });
  341 | test('RecoveryCodes canonical routes guard direct entry reload history and capabilities',async({page})=>{
  342 |  test.setTimeout(90_000);const server=await fixture(page,{denyPublish:true}),family='RecoveryCodes' as const;for(const view of ['edit','review','history'] as const){const path=configurationPath(family,view),before=server.guards;await page.goto(path+'?candidate=discard#discard');await expect(page).toHaveURL('http://127.0.0.1:5298'+path);await expect.poll(()=>server.guards).toBeGreaterThan(before);await expect(page.locator('[data-tunner-screen="A-ID-SETTINGS-'+family+'-'+view+'"]')).toBeVisible();await page.getByLabel('Exact scope',{exact:true}).selectOption(scopeKey(scopes[family]));if(view!=='history'){await page.getByLabel('Saved candidate',{exact:true}).selectOption(proposalId);if(view==='edit')await expect(page.getByRole('button',{name:'Save draft',exact:true})).toBeEnabled();else await expect(page.getByRole('button',{name:'Publish configuration',exact:true})).toBeDisabled();}else await expect(page.locator('.configuration-history')).toHaveCount(1);const beforeReload=server.guards;await page.reload();await expect.poll(()=>server.guards).toBeGreaterThan(beforeReload);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');await expect(page.locator('.configuration-diff')).toHaveCount(0);const alternate=view==='edit'?'review':'edit';await page.getByRole('link',{name:labels[alternate],exact:true}).click();await expect(page).toHaveURL('http://127.0.0.1:5298'+configurationPath(family,alternate));const beforeBack=server.guards;await page.goBack();await expect(page).toHaveURL('http://127.0.0.1:5298'+path);await expect.poll(()=>server.guards).toBeGreaterThan(beforeBack);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');const beforeForward=server.guards;await page.goForward();await expect(page).toHaveURL('http://127.0.0.1:5298'+configurationPath(family,alternate));await expect.poll(()=>server.guards).toBeGreaterThan(beforeForward);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');}expect(server.writes).toHaveLength(0);expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
  343 | });
  344 | test('RecoveryCodes laptop tablet and narrow form preserves exact work text keyboard correction and reflow',async({page},info)=>{
  345 |  test.setTimeout(120_000);const server=await fixture(page);for(const viewport of [{width:1024,height:768},{width:768,height:1024},{width:320,height:800}]){await page.setViewportSize(viewport);await select(page,'RecoveryCodes');const field=page.locator('[name="crypto.maximumHashWork"]');await expect(field).toHaveValue('9007199254740995');await field.fill('');await field.press('Tab');await expect(field).toHaveAttribute('aria-invalid','true');await page.getByRole('button',{name:'Save draft',exact:true}).focus();await page.keyboard.press('Enter');await expect(field).toBeFocused();await expect(page.getByRole('alert')).toContainText(labels.validationSummary);expect(server.writes).toHaveLength(0);await field.fill('9223372036854775807');await field.press('Tab');await expect(field).not.toHaveAttribute('aria-invalid','true');await expect(field).toHaveValue('9223372036854775807');await expect(page.getByRole('alert')).toHaveCount(0);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);expect(await page.locator('input:visible,select:visible,button:visible').evaluateAll(items=>items.every(item=>{const b=item.getBoundingClientRect();return b.width>0&&b.left>=-0.5&&b.right<=innerWidth+0.5;}))).toBe(true);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);if(viewport.width!==1024)await page.screenshot({path:info.outputPath(`recovery-codes-edit-${viewport.width}.png`),fullPage:true});}expect(server.writes).toHaveLength(0);
  346 | });
  347 | test('RecoveryCodes conditional chrome preserves earlier ten family publications and blank owner input',async({page})=>{
  348 |  test.setTimeout(90_000);const server=await fixture(page,{tenOnly:true});for(const family of ['Authentication','Assurance','AuthenticationContent','Registration','Totp','RegistrationContent','Passkey','TotpContent','PasskeyContent','StepUpContent'] as const){await select(page,family);await expect(page.getByRole('button',{name:'Save draft',exact:true})).toBeEnabled();await expect(page.getByRole('link',{name:labels.RecoveryCodes,exact:true})).toHaveCount(0);}await page.goto(configurationPath('RecoveryCodes'));await expect(page.getByRole('alert')).toContainText('The request could not be completed.');await expect(page.getByRole('button',{name:'Save draft',exact:true})).toHaveCount(0);expect(server.writes).toHaveLength(0);await page.unrouteAll();const qualified=await fixture(page);await select(page,'RecoveryCodes',false);for(const input of await page.locator('main form input').all())await expect(input).toHaveValue('');for(const select of await page.locator('main form select').all())await expect(select).toHaveValue('');await page.getByRole('button',{name:'Save draft',exact:true}).click();await expect(page.locator('[name="version"]')).toBeFocused();expect(qualified.writes).toHaveLength(0);
  349 | });
  350 | test('duration conversion preserves every supported tick and rejects overflow',()=>{for(const raw of ['0.0000001','120.0000001','86400','922337203685.4775807'])expect(durationToSeconds(secondsToDuration(raw))).toBe(raw);for(const raw of ['0','-1','1e3','0.00000001','922337203685.4775808'])expect(()=>secondsToDuration(raw)).toThrow();});
  351 | 
```