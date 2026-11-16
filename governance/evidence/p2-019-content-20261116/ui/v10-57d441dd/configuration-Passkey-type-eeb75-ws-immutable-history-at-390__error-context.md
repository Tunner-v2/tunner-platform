# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: configuration.spec.ts >> Passkey typed policy validates ordered inputs, retries exact intent and reviews immutable history at 390
- Location: tests\configuration-admin\configuration.spec.ts:245:31

# Error details

```
Error: expect(locator).toHaveCount(expected) failed

Locator:  locator('[data-field="values.revocationSessionEffect"]').locator('td').nth(1).locator('[data-content-publication="fixture-chrome-p1"]')
Expected: 1
Received: 2
Timeout:  5000ms

Call log:
  - Expect "toHaveCount" with timeout 5000ms
  - waiting for locator('[data-field="values.revocationSessionEffect"]').locator('td').nth(1).locator('[data-content-publication="fixture-chrome-p1"]')
    14 × locator resolved to 2 elements
       - unexpected value "2"

```

# Page snapshot

```yaml
- generic [ref=e4]:
  - link "Skip to content" [ref=e5] [cursor=pointer]:
    - /url: "#configuration-main"
  - generic [ref=e7]:
    - banner [ref=e8]:
      - generic [ref=e9]:
        - strong [ref=e10]: Identity settings
        - paragraph [ref=e11]: Typed, versioned runtime configuration
      - button "Navigation" [ref=e12]
    - region "Identity security authority is separate from Content editing." [ref=e13]
    - main [ref=e14]:
      - generic [ref=e15]:
        - generic [ref=e16]:
          - paragraph [ref=e17]: Identity settings
          - heading "Review configuration" [active] [level=1] [ref=e18]
          - paragraph [ref=e19]: Typed, versioned runtime configuration
        - navigation "Review configuration" [ref=e20]:
          - link "Edit draft" [ref=e21] [cursor=pointer]:
            - /url: /identity/settings/passkey
          - link "Review configuration" [ref=e22] [cursor=pointer]:
            - /url: /identity/settings/passkey/review
          - link "Publication history" [ref=e23] [cursor=pointer]:
            - /url: /identity/settings/passkey/history
      - status [ref=e24]:
        - generic [ref=e25]: The request was applied. Current state has been reloaded.
        - button "Refresh" [ref=e26] [cursor=pointer]
      - generic [ref=e28]:
        - generic [ref=e29]:
          - generic [ref=e30]: Exact scope
          - combobox "Exact scope" [ref=e31]:
            - option "Choose a server-qualified scope"
            - option "PASSKEY" [selected]
        - generic [ref=e32]:
          - generic [ref=e33]:
            - generic [ref=e34]: Search candidates
            - textbox "Search candidates" [ref=e35]
            - generic [ref=e36]: Find an exact candidate policy version.
          - button "Search candidates" [ref=e37] [cursor=pointer]
        - generic [ref=e39]:
          - generic [ref=e40]: Saved candidate
          - combobox "Saved candidate" [ref=e41]:
            - option "New typed draft"
            - option "fixture-v2 · Validated · 3" [selected]
      - generic [ref=e42]:
        - generic [ref=e43]:
          - heading "Current vs proposed" [level=2] [ref=e44]
          - generic [ref=e45]:
            - generic [ref=e46]: Compare with published version
            - combobox "Compare with published version" [ref=e47]:
              - option "Choose"
              - option "fixture-v1 · 00000001" [selected]
          - table [ref=e48]:
            - rowgroup [ref=e49]:
              - row [ref=e50]:
                - rowheader "Policy version reference" [ref=e51]
                - cell "Selected publication · fixture-v1" [ref=e52]:
                  - generic [ref=e53]: Selected publication ·
                  - text: fixture-v1
                - cell "Saved candidate · fixture-v2" [ref=e54]:
                  - generic [ref=e55]: Saved candidate ·
                  - text: fixture-v2
              - row [ref=e56]:
                - rowheader "Candidate metadata selection status" [ref=e57]
                - cell "Selected publication · ACTIVE" [ref=e58]:
                  - generic [ref=e59]: Selected publication ·
                  - text: ACTIVE
                - cell "Saved candidate · ACTIVE" [ref=e60]:
                  - generic [ref=e61]: Saved candidate ·
                  - text: ACTIVE
              - row [ref=e62]:
                - rowheader "Previous policy version reference" [ref=e63]
                - cell "Selected publication · —" [ref=e64]:
                  - generic [ref=e65]: Selected publication ·
                  - text: —
                - cell "Saved candidate · fixture-v1" [ref=e66]:
                  - generic [ref=e67]: Saved candidate ·
                  - text: fixture-v1
              - row [ref=e68]:
                - rowheader "Candidate source evidence 1" [ref=e69]:
                  - text: Candidate source evidence
                  - generic [ref=e70]: "1"
                - cell "Selected publication · fixture-source-evidence" [ref=e71]:
                  - generic [ref=e72]: Selected publication ·
                  - text: fixture-source-evidence
                - cell "Saved candidate · fixture-source-evidence" [ref=e73]:
                  - generic [ref=e74]: Saved candidate ·
                  - text: fixture-source-evidence
              - row [ref=e75]:
                - rowheader "Available from · UTC" [ref=e76]
                - cell "Selected publication · 2026-01-01T00:00:00.0000001Z" [ref=e77]:
                  - generic [ref=e78]: Selected publication ·
                  - text: 2026-01-01T00:00:00.0000001Z
                - cell "Saved candidate · 2026-01-01T00:00:00.0000001Z" [ref=e79]:
                  - generic [ref=e80]: Saved candidate ·
                  - text: 2026-01-01T00:00:00.0000001Z
              - row [ref=e81]:
                - rowheader "Start boundary" [ref=e82]
                - cell "Selected publication · true" [ref=e83]:
                  - generic [ref=e84]: Selected publication ·
                  - text: "true"
                - cell "Saved candidate · true" [ref=e85]:
                  - generic [ref=e86]: Saved candidate ·
                  - text: "true"
              - row [ref=e87]:
                - rowheader "Available until · UTC" [ref=e88]
                - cell "Selected publication · —" [ref=e89]:
                  - generic [ref=e90]: Selected publication ·
                  - text: —
                - cell "Saved candidate · —" [ref=e91]:
                  - generic [ref=e92]: Saved candidate ·
                  - text: —
              - row [ref=e93]:
                - rowheader "End boundary" [ref=e94]
                - cell "Selected publication · false" [ref=e95]:
                  - generic [ref=e96]: Selected publication ·
                  - text: "false"
                - cell "Saved candidate · false" [ref=e97]:
                  - generic [ref=e98]: Saved candidate ·
                  - text: "false"
              - row [ref=e99]:
                - rowheader "Exact scope" [ref=e100]
                - cell "Selected publication · PASSKEY" [ref=e101]:
                  - generic [ref=e102]: Selected publication ·
                  - text: PASSKEY
                - cell "Saved candidate · PASSKEY" [ref=e103]:
                  - generic [ref=e104]: Saved candidate ·
                  - text: PASSKEY
              - row [ref=e105]:
                - rowheader "Policy version reference" [ref=e106]
                - cell "Selected publication · fixture-v1" [ref=e107]:
                  - generic [ref=e108]: Selected publication ·
                  - text: fixture-v1
                - cell "Saved candidate · fixture-v2" [ref=e109]:
                  - generic [ref=e110]: Saved candidate ·
                  - text: fixture-v2
              - row [ref=e111]:
                - rowheader "Relying-party DNS identifier" [ref=e112]
                - cell "Selected publication · fixture.example.test" [ref=e113]:
                  - generic [ref=e114]: Selected publication ·
                  - text: fixture.example.test
                - cell "Saved candidate · fixture.example.test" [ref=e115]:
                  - generic [ref=e116]: Saved candidate ·
                  - text: fixture.example.test
              - row [ref=e117]:
                - rowheader "Origin" [ref=e118]
                - cell "Selected publication · https://login.fixture.example.test" [ref=e119]:
                  - generic [ref=e120]: Selected publication ·
                  - text: https://login.fixture.example.test
                - cell "Saved candidate · https://login.fixture.example.test" [ref=e121]:
                  - generic [ref=e122]: Saved candidate ·
                  - text: https://login.fixture.example.test
              - row [ref=e123]:
                - rowheader "Resident-key requirement" [ref=e124]
                - cell "Selected publication · required" [ref=e125]:
                  - generic [ref=e126]: Selected publication ·
                  - text: required
                - cell "Saved candidate · required" [ref=e127]:
                  - generic [ref=e128]: Saved candidate ·
                  - text: required
              - row [ref=e129]:
                - rowheader "Enrollment assurance policy version" [ref=e130]
                - cell "Selected publication · fixture-enrollment-policy" [ref=e131]:
                  - generic [ref=e132]: Selected publication ·
                  - text: fixture-enrollment-policy
                - cell "Saved candidate · fixture-enrollment-policy" [ref=e133]:
                  - generic [ref=e134]: Saved candidate ·
                  - text: fixture-enrollment-policy
              - row [ref=e135]:
                - rowheader "Revocation assurance policy version" [ref=e136]
                - cell "Selected publication · fixture-revocation-policy" [ref=e137]:
                  - generic [ref=e138]: Selected publication ·
                  - text: fixture-revocation-policy
                - cell "Saved candidate · fixture-revocation-policy" [ref=e139]:
                  - generic [ref=e140]: Saved candidate ·
                  - text: fixture-revocation-policy
              - row [ref=e141]:
                - rowheader "Processing reference" [ref=e142]
                - cell "Selected publication · fixture-processing" [ref=e143]:
                  - generic [ref=e144]: Selected publication ·
                  - text: fixture-processing
                - cell "Saved candidate · fixture-processing" [ref=e145]:
                  - generic [ref=e146]: Saved candidate ·
                  - text: fixture-processing
              - row [ref=e147]:
                - rowheader "Retention reference" [ref=e148]
                - cell "Selected publication · fixture-retention" [ref=e149]:
                  - generic [ref=e150]: Selected publication ·
                  - text: fixture-retention
                - cell "Saved candidate · fixture-retention" [ref=e151]:
                  - generic [ref=e152]: Saved candidate ·
                  - text: fixture-retention
              - row [ref=e153]:
                - rowheader "User-verification requirement" [ref=e154]
                - cell "Selected publication · required" [ref=e155]:
                  - generic [ref=e156]: Selected publication ·
                  - text: required
                - cell "Saved candidate · required" [ref=e157]:
                  - generic [ref=e158]: Saved candidate ·
                  - text: required
              - row [ref=e159]:
                - rowheader "Attestation conveyance" [ref=e160]
                - cell "Selected publication · none" [ref=e161]:
                  - generic [ref=e162]: Selected publication ·
                  - text: none
                - cell "Saved candidate · none" [ref=e163]:
                  - generic [ref=e164]: Saved candidate ·
                  - text: none
              - row [ref=e165]:
                - rowheader "Challenge length · bytes" [ref=e166]
                - cell "Selected publication · 32" [ref=e167]:
                  - generic [ref=e168]: Selected publication ·
                  - text: "32"
                - cell "Saved candidate · 32" [ref=e169]:
                  - generic [ref=e170]: Saved candidate ·
                  - text: "32"
              - row [ref=e171]:
                - rowheader "Maximum credential JSON · bytes" [ref=e172]
                - cell "Selected publication · 4096" [ref=e173]:
                  - generic [ref=e174]: Selected publication ·
                  - text: "4096"
                - cell "Saved candidate · 4096" [ref=e175]:
                  - generic [ref=e176]: Saved candidate ·
                  - text: "4096"
              - row [ref=e177]:
                - rowheader "Maximum active credentials" [ref=e178]
                - cell "Selected publication · 5" [ref=e179]:
                  - generic [ref=e180]: Selected publication ·
                  - text: "5"
                - cell "Saved candidate · 5" [ref=e181]:
                  - generic [ref=e182]: Saved candidate ·
                  - text: "5"
              - row [ref=e183]:
                - rowheader "Maximum pending ceremonies" [ref=e184]
                - cell "Selected publication · 8" [ref=e185]:
                  - generic [ref=e186]: Selected publication ·
                  - text: "8"
                - cell "Saved candidate · 8" [ref=e187]:
                  - generic [ref=e188]: Saved candidate ·
                  - text: "8"
              - row [ref=e189]:
                - rowheader "Ceremony lifetime" [ref=e190]
                - cell "Selected publication · 00:02:00" [ref=e191]:
                  - generic [ref=e192]: Selected publication ·
                  - text: 00:02:00
                - cell "Saved candidate · 00:02:00" [ref=e193]:
                  - generic [ref=e194]: Saved candidate ·
                  - text: 00:02:00
              - row [ref=e195]:
                - rowheader "Authenticator timeout" [ref=e196]
                - cell "Selected publication · 00:00:30.0000001" [ref=e197]:
                  - generic [ref=e198]: Selected publication ·
                  - text: 00:00:30.0000001
                - cell "Saved candidate · 00:00:30.0000001" [ref=e199]:
                  - generic [ref=e200]: Saved candidate ·
                  - text: 00:00:30.0000001
              - row [ref=e201]:
                - rowheader "Result recovery lifetime" [ref=e202]
                - cell "Selected publication · 00:05:00" [ref=e203]:
                  - generic [ref=e204]: Selected publication ·
                  - text: 00:05:00
                - cell "Saved candidate · 00:05:00" [ref=e205]:
                  - generic [ref=e206]: Saved candidate ·
                  - text: 00:05:00
              - row [ref=e207]:
                - rowheader "Revocation session effect" [ref=e208]
                - cell "Selected publication · Preserve existing sessions (1)" [ref=e209]:
                  - generic [ref=e210]: Selected publication ·
                  - text: Preserve existing sessions (1)
                - cell "Saved candidate · Revoke credential sessions (2)" [ref=e211]:
                  - generic [ref=e212]: Saved candidate ·
                  - text: Revoke credential sessions (2)
              - row [ref=e213]:
                - rowheader "Algorithm identifier 1" [ref=e214]:
                  - text: Algorithm identifier
                  - generic [ref=e215]: "1"
                - cell "Selected publication · -7" [ref=e216]:
                  - generic [ref=e217]: Selected publication ·
                  - text: "-7"
                - cell "Saved candidate · -999" [ref=e218]:
                  - generic [ref=e219]: Saved candidate ·
                  - text: "-999"
              - row [ref=e220]:
                - rowheader "Mapped assurance identifier uv:required;backupEligible:False;backedUp:False;attestation:unverified" [ref=e221]:
                  - text: Mapped assurance identifier
                  - generic [ref=e222]: uv:required;backupEligible:False;backedUp:False;attestation:unverified
                - cell "Selected publication · fixture-old-passkey-assurance" [ref=e223]:
                  - generic [ref=e224]: Selected publication ·
                  - text: fixture-old-passkey-assurance
                - cell "Saved candidate · fixture-passkey-assurance" [ref=e225]:
                  - generic [ref=e226]: Saved candidate ·
                  - text: fixture-passkey-assurance
              - row [ref=e227]:
                - rowheader "Mapped assurance identifier uv:required;backupEligible:True;backedUp:False;attestation:unverified" [ref=e228]:
                  - text: Mapped assurance identifier
                  - generic [ref=e229]: uv:required;backupEligible:True;backedUp:False;attestation:unverified
                - cell "Selected publication · fixture-backup-assurance" [ref=e230]:
                  - generic [ref=e231]: Selected publication ·
                  - text: fixture-backup-assurance
                - cell "Saved candidate · fixture-backup-assurance" [ref=e232]:
                  - generic [ref=e233]: Saved candidate ·
                  - text: fixture-backup-assurance
              - row [ref=e234]:
                - rowheader "Algorithm identifier 2" [ref=e235]:
                  - text: Algorithm identifier
                  - generic [ref=e236]: "2"
                - cell "Selected publication · —" [ref=e237]:
                  - generic [ref=e238]: Selected publication ·
                  - text: —
                - cell "Saved candidate · -7" [ref=e239]:
                  - generic [ref=e240]: Saved candidate ·
                  - text: "-7"
        - generic [ref=e241]:
          - heading "Identity approval" [level=2] [ref=e242]
          - generic [ref=e243]:
            - generic [ref=e244]:
              - term [ref=e245]: Saved candidate
              - definition [ref=e246]: fixture-v2
            - generic [ref=e247]:
              - term [ref=e248]: State
              - definition [ref=e249]: Validated
            - generic [ref=e250]:
              - term [ref=e251]: Candidate revision
              - definition [ref=e252]: "3"
            - generic [ref=e253]:
              - term [ref=e254]: Candidate digest
              - definition [ref=e255]: fixture-digest-3
            - generic [ref=e256]:
              - term [ref=e257]: Governance version
              - definition [ref=e258]: fixture-governance-v1
          - paragraph [ref=e259]:
            - generic [ref=e260]: Configured author, approver and publisher separation is checked independently.
          - generic [ref=e261]:
            - button "Validate candidate" [disabled] [ref=e262]
            - button "Approve candidate" [ref=e264] [cursor=pointer]
            - button "Publish configuration" [disabled] [ref=e266]
      - generic [ref=e268]:
        - heading "Publication history" [level=2] [ref=e269]
        - article [ref=e270]:
          - generic [ref=e271]:
            - generic [ref=e272]:
              - term [ref=e273]: Policy version reference
              - definition [ref=e274]: fixture-v1
            - generic [ref=e275]:
              - term [ref=e276]: Publication reference
              - definition [ref=e277]: 43000000-0000-0000-0000-000000000001
            - generic [ref=e278]:
              - term [ref=e279]: Candidate digest
              - definition [ref=e280]: fixture-historical-digest
            - generic [ref=e281]:
              - term [ref=e282]: Available from · UTC
              - definition [ref=e283]: 2026-01-01T00:00:00.0000001Z
            - generic [ref=e284]:
              - term [ref=e285]: Available until · UTC
              - definition [ref=e286]: No scheduled end
            - generic [ref=e287]:
              - term [ref=e288]: Start boundary
              - definition [ref=e289]: Inclusive
            - generic [ref=e290]:
              - term [ref=e291]: Approver reference
              - definition [ref=e292]: fixture-other-approver
            - generic [ref=e293]:
              - term [ref=e294]: Approval evidence reference
              - definition [ref=e295]: fixture-actual-approval
            - generic [ref=e296]:
              - term [ref=e297]: Publisher reference
              - definition [ref=e298]: fixture-publisher
            - generic [ref=e299]:
              - term [ref=e300]: Governance version
              - definition [ref=e301]: fixture-governance-v1
          - button "Republish approved version" [ref=e302] [cursor=pointer]
```

# Test source

```ts
  141 |     expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);
  142 |     if(viewport.width===768)await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-tablet-768.png`),fullPage:true});
  143 |    }catch(error){if(viewport.width!==768)await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-reflow-failure-${viewport.width}.png`),fullPage:true});throw error;}
  144 |   });
  145 |  }
  146 |  expect(server.writes).toHaveLength(0);expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
  147 | });
  148 | // Check the complete wire candidate, independently of the UI's candidateFields projection.
  149 | function additionalFieldValues(value:Extract<Candidate,{family:'Registration'|'Totp'}>):Record<string,string>{
  150 |  const metadata=value.value.metadata,values=value.value.values,fields:Record<string,string>={'metadata.version':metadata.versionId,'metadata.status':metadata.status,'metadata.supersedes':metadata.supersedesVersionId??'—','evidence.0':metadata.approvalEvidenceReferences[0]!,'interval.from':metadata.effectiveInterval.from,'interval.includesFrom':String(metadata.effectiveInterval.includesFrom),'interval.until':metadata.effectiveInterval.until??'—','interval.includesUntil':String(metadata.effectiveInterval.includesUntil),'metadata.scope':metadata.scope};
  151 |  for(const [name,item] of Object.entries(values))if(name!=='mfaAssurances')fields['values.'+name]=String(item);
  152 |  if(value.family==='Totp')for(const [method,assurance] of Object.entries(value.value.values.mfaAssurances))fields['method.'+method]=assurance;
  153 |  return fields;
  154 | }
  155 | async function assertAdditionalDiff(page:Page,value:Extract<Candidate,{family:'Registration'|'Totp'}>,column:'current'|'proposed'){
  156 |  const fields=additionalFieldValues(value),rows=page.locator('.configuration-diff [data-field]');expect((await rows.evaluateAll(elements=>elements.map(element=>element.getAttribute('data-field')))).sort()).toEqual(Object.keys(fields).sort());
  157 |  for(const [key,text] of Object.entries(fields))await expect(page.locator('[data-field="'+key+'"]').locator('td').nth(column==='current'?0:1)).toContainText(text);
  158 |  if(value.family==='Totp'){
  159 |   const sessionChoices={1:labels.preserveExistingSessions,2:labels.revokeFactorSessions},replacementChoices={1:labels.independentEnrollment,2:labels.disableBeforeEnrollment},values=value.value.values;
  160 |   const choices={disableSessionEffect:sessionChoices[values.disableSessionEffect],resetSessionEffect:sessionChoices[values.resetSessionEffect],replacementOrdering:replacementChoices[values.replacementOrdering],resetBehavior:labels.retireAffectedFactorThenSeparateEnrollment};
  161 |   // The raw-number assertions above remain required beside the qualified human meaning.
  162 |   for(const [field,choice] of Object.entries(choices))await expect(page.locator('[data-field="values.'+field+'"]').locator('td').nth(column==='current'?0:1)).toContainText(choice);
  163 |  }
  164 | }
  165 | for(const family of ['Registration','Totp'] as const)for(const width of [1440,390])test(`${family} typed policy validates, preserves retry intent and reviews every immutable field at ${width}`,async({page},info)=>{
  166 |  test.setTimeout(60_000);await page.setViewportSize({width,height:width===1440?1024:844});const server=await fixture(page,{unknownOnce:true});await select(page,family);const save=page.getByRole('button',{name:'Save draft',exact:true});
  167 |  await expect(page.getByLabel('Policy version reference',{exact:true})).toHaveValue('fixture-v2');
  168 |  const fixed=family==='Registration'?{accountEligibilityAuthority:'PDR-0011',activationProfile:'PASSWORD_AND_VERIFIED_EMAIL'}:{nativeVerifierVersion:'10.0.13',algorithm:'SHA1',digits:'6',periodSeconds:'30',nativeWindowSteps:'2'};
  169 |  const fixedPanel=page.locator('.configuration-fixed');await expect(fixedPanel).toHaveCount(1);await expect(fixedPanel).toHaveCSS('background-color','rgb(247, 249, 250)');await expect(fixedPanel).toHaveCSS('border-radius','8px');await expect(page.locator('.configuration-fixed + .configuration-fields #configuration-version')).toHaveCount(1);
  170 |  for(const [name,value] of Object.entries(fixed)){await expect(fixedPanel.getByText(value,{exact:true}).first()).toBeVisible();await expect(page.locator('input[name="'+name+'"],select[name="'+name+'"]')).toHaveCount(0);}
  171 |  const invalid=async(name:string,bad:string,corrected:string)=>{
  172 |   const field=page.locator('[name="'+name+'"]');await field.fill(bad);await field.press('Tab');await expect(field).toHaveAttribute('aria-invalid','true');await save.focus();await expect(save).toBeFocused();await page.keyboard.press('Enter');await expect(field).toBeFocused();expect(server.writes).toHaveLength(0);
  173 |   await save.evaluate(button=>(button as HTMLButtonElement).form!.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})));expect(server.writes).toHaveLength(0);await expect(page.getByRole('alert')).toContainText(labels.validationSummary);await expect(page.locator(':invalid')).toHaveCount(0);
  174 |   await field.fill(corrected);await field.press('Tab');await expect(field).not.toHaveAttribute('aria-invalid','true');
  175 |  };
  176 |  if(family==='Registration'){
  177 |   await expect(page.getByLabel('Resend cooldown',{exact:true})).toHaveValue('0');await expect(page.getByLabel('Resend limit',{exact:true})).toHaveValue('0');
  178 |   await invalid('resendLimit','-1','0');await invalid('pendingMaterialRetention','299','3600');await page.getByLabel('Minimum password length',{exact:true}).fill('129');await invalid('passwordMaximumLength','128','256');await page.getByLabel('Minimum password length',{exact:true}).fill('15');await page.getByLabel('Maximum password length',{exact:true}).fill('128');
  179 |   await page.getByLabel('Terms version reference',{exact:true}).fill('fixture-updated-terms');
  180 |  }else{
  181 |   await expect(page.getByLabel('Maximum clock deviation',{exact:true})).toHaveValue('0');await invalid('origin','https://totp.fixture.example.test/path','https://totp.fixture.example.test');await invalid('replayRetention','159','180');await invalid('maximumLiveReplayClaims','1001','16');await invalid('operationProofAssurance','AAL3','fixture-operation-proof');await invalid('assurance.0','AAL3','fixture-mfa');
  182 |   await page.getByLabel('Method',{exact:true}).nth(1).selectOption('pwd');await save.focus();await page.keyboard.press('Enter');await expect(page.getByLabel('Method',{exact:true}).nth(1)).toBeFocused();await expect(page.getByLabel('Method',{exact:true}).nth(1)).toHaveAttribute('aria-invalid','true');expect(server.writes).toHaveLength(0);await page.getByLabel('Method',{exact:true}).nth(1).selectOption('passkey');await page.getByLabel('Method',{exact:true}).nth(1).press('Tab');
  183 |   await page.getByLabel('Issuer',{exact:true}).fill('Fixture updated issuer');
  184 |  }
  185 |  expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-edit-${width}.png`),fullPage:true});
  186 |  const expected=candidate(family);if(expected.family==='Registration')expected.value.values.termsVersion='fixture-updated-terms';else if(expected.family==='Totp')expected.value.values.issuer='Fixture updated issuer';
  187 |  await expect(save).toBeEnabled();await save.focus();await expect(save).toBeFocused();await page.keyboard.press('Enter');await expect(page.getByRole('button',{name:'Retry same request',exact:true})).toBeVisible();expect(server.writes).toHaveLength(1);expect(server.writes[0]!.path).toBe(family==='Registration'?'/registration/draft':'/totp/draft');expect(server.writes[0]!.body.candidate).toEqual(expected.value);expect(server.writes[0]!.body).toMatchObject({proposalReference:proposalId,expectedRevision:1});
  188 |  await expect(page.getByLabel(family==='Registration'?'Terms version reference':'Issuer',{exact:true})).toHaveValue(family==='Registration'?'fixture-updated-terms':'Fixture updated issuer');await page.getByRole('button',{name:'Retry same request',exact:true}).click();await expect(page.getByText(labels.replayed,{exact:true})).toBeVisible();expect(server.writes[1]!.body).toEqual(server.writes[0]!.body);expect(server.revision).toBe(2);
  189 |  await expect(page.getByRole('button',{name:'Validate candidate',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Validate candidate',exact:true}).click();await page.getByRole('button',{name:'Review configuration',exact:true}).click();await expect(page.getByRole('button',{name:'Approve candidate',exact:true})).toBeEnabled();await page.getByLabel('Compare with published version',{exact:true}).selectOption(publicationId);
  190 |  if(expected.family!=='Registration'&&expected.family!=='Totp')throw new Error('Unexpected synthetic family');await assertAdditionalDiff(page,expected,'proposed');const baseline=candidate(family,true);if(baseline.family!=='Registration'&&baseline.family!=='Totp')throw new Error('Unexpected synthetic baseline');await assertAdditionalDiff(page,baseline,'current');
  191 |  expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-review-${width}.png`),fullPage:true});await page.getByRole('button',{name:'Approve candidate',exact:true}).click();await expect(page.getByRole('button',{name:'Publish configuration',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Publish configuration',exact:true}).click();await fillPublication(page);await page.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect.poll(()=>server.writes.length).toBe(5);
  192 |  expect(server.writes.map(write=>write.path)).toEqual([family==='Registration'?'/registration/draft':'/totp/draft',family==='Registration'?'/registration/draft':'/totp/draft','/proposals/validate','/proposals/approve','/proposals/publish']);expect(server.writes[4]!.body).toMatchObject({proposalReference:proposalId,expectedRevision:4,supersedesPublicationReference:publicationId});
  193 |  await page.getByRole('link',{name:'Publication history',exact:true}).click();await expect(page.locator('main[aria-busy="false"] .configuration-history')).toHaveCount(1);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-history-${width}.png`),fullPage:true});
  194 |  await page.getByRole('button',{name:'Republish approved version',exact:true}).click();await expect(page.getByRole('dialog')).toBeVisible();await page.getByRole('dialog').locator('summary').click();await assertAdditionalDiff(page,baseline,'proposed');await page.getByRole('dialog').getByRole('button',{name:'Cancel',exact:true}).click();expect(server.writes).toHaveLength(5);
  195 |  expect(new URL(page.url()).search).toBe('');expect(new URL(page.url()).hash).toBe('');expect(page.url()).not.toContain(proposalId);expect(page.url()).not.toContain(publicationId);expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);await expect(page.locator('[data-content-key="admin.identity.configuration.title"]').first()).toHaveAttribute('data-content-publication','fixture-chrome-p1');
  196 | });
  197 | test('additional family tablet and narrow reflow preserves Registration and Totp typed forms',async({page},info)=>{
  198 |  test.setTimeout(60_000);const server=await fixture(page);
  199 |  for(const width of [768,320])for(const family of ['Registration','Totp','RegistrationContent'] as const)await test.step(width+' / '+family,async()=>{
  200 |   await page.setViewportSize({width,height:width===768?1024:800});await select(page,family);const field=page.getByLabel(family==='RegistrationContent'?'Configuration version reference':'Policy version reference',{exact:true});await field.fill('');await field.press('Tab');const save=page.getByRole('button',{name:'Save draft',exact:true});await expect(save).toBeEnabled();await save.focus();await expect(save).toBeFocused();await page.keyboard.press('Enter');await expect(field).toBeFocused();await expect(page.getByRole('alert')).toContainText(labels.validationSummary);expect(server.writes).toHaveLength(0);await field.fill(family==='RegistrationContent'?'fixture-registration-binding-v2':'fixture-v2');await field.press('Tab');await expect(field).not.toHaveAttribute('aria-invalid','true');
  201 |   expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);expect(await page.locator('input:visible,select:visible,textarea:visible,button:visible').evaluateAll(elements=>elements.every(element=>{const bounds=element.getBoundingClientRect();return bounds.width>0&&bounds.left>=-0.5&&bounds.right<=innerWidth+0.5;}))).toBe(true);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.screenshot({path:info.outputPath(`${family.toLowerCase()}-edit-${width}.png`),fullPage:true});
  202 |  });expect(server.writes).toHaveLength(0);expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
  203 | });
  204 | test('original three-family publication remains sufficient and unqualified additional routes fail closed',async({page})=>{
  205 |  const server=await fixture(page,{baseOnly:true});for(const family of ['Authentication','Assurance','AuthenticationContent'] as const){await select(page,family);await expect(page.getByRole('button',{name:'Save draft',exact:true})).toBeEnabled();await expect(page.getByRole('link',{name:labels.Registration,exact:true})).toHaveCount(0);await expect(page.getByRole('link',{name:labels.Totp,exact:true})).toHaveCount(0);await expect(page.getByRole('link',{name:labels.RegistrationContent,exact:true})).toHaveCount(0);await expect(page.locator('[data-content-key="admin.identity.configuration.title"]').first()).toHaveAttribute('data-content-publication','fixture-chrome-p1');}
  206 |  for(const family of ['Registration','Totp','RegistrationContent'] as const){await page.goto(configurationPath(family));await expect(page.getByRole('alert')).toContainText('The request could not be completed.');await expect(page.getByRole('button',{name:'Save draft',exact:true})).toHaveCount(0);}expect(server.writes).toHaveLength(0);
  207 | });
  208 | for(const width of [1440,390])test(`RegistrationContent exact notice binding preserves typed retry, publication and historical rollback at ${width}`,async({page},info)=>{
  209 |  test.setTimeout(60_000);await page.setViewportSize({width,height:width===1440?1024:844});const server=await fixture(page,{unknownOnce:true});await select(page,'RegistrationContent');const save=page.getByRole('button',{name:'Save draft',exact:true}),panel=page.locator('.configuration-fixed');
  210 |  await expect(panel).toHaveCount(1);await expect(panel).toHaveCSS('background-color','rgb(247, 249, 250)');await expect(panel.getByText('fixture-registration-v1',{exact:true})).toBeVisible();await expect(panel.getByText(labels.registrationPolicyVersion,{exact:true})).toBeVisible();await expect(page.locator('.configuration-fixed + .configuration-fields #configuration-configurationVersionReference')).toHaveCount(1);await expect(page.locator('input[name="registrationPolicyVersion"],select[name="registrationPolicyVersion"]')).toHaveCount(0);await expect(page.locator('input[name="version"],input[name="evidence.0"]')).toHaveCount(0);
  211 |  const termsId=page.getByLabel('Terms version reference',{exact:true}),privacyId=page.getByLabel('Privacy version reference',{exact:true}),termsLink=page.getByLabel('Terms link',{exact:true}),privacyLink=page.getByLabel('Privacy link',{exact:true});await expect(termsId).toHaveValue('fixture-terms-v2');await expect(privacyId).toHaveValue('fixture-privacy-v2');await expect(termsLink).toHaveValue('https://terms.example.test/');await expect(privacyLink).toHaveValue('https://privacy.example.test/');await expect(page.getByText(labels.noticeIdentifiers,{exact:true})).toBeVisible();
  212 |  await termsId.fill('');await termsId.press('Tab');await expect(termsId).toHaveAttribute('aria-invalid','true');await expect(termsId).toHaveAttribute('aria-describedby',/(?:^| )configuration-termsVersion-error(?: |$)/);await save.focus();await page.keyboard.press('Enter');await expect(termsId).toBeFocused();expect(server.writes).toHaveLength(0);await save.evaluate(button=>(button as HTMLButtonElement).form!.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})));expect(server.writes).toHaveLength(0);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);if(width===390)await page.screenshot({path:info.outputPath('registration-content-required-error-390.png'),fullPage:true});await termsId.fill('fixture-reviewed-terms');await termsId.press('Tab');await expect(termsId).not.toHaveAttribute('aria-invalid','true');await expect(termsLink).toHaveValue('https://terms.example.test/');
  213 |  await privacyLink.fill('https://fixture-user:fixture-password@privacy.example.test/');await privacyLink.press('Tab');await expect(privacyLink).toHaveAttribute('aria-invalid','true');await save.focus();await page.keyboard.press('Enter');await expect(privacyLink).toBeFocused();expect(server.writes).toHaveLength(0);await privacyLink.fill('https://privacy.example.test/');await privacyLink.press('Tab');await expect(privacyLink).not.toHaveAttribute('aria-invalid','true');await expect(privacyId).toHaveValue('fixture-privacy-v2');await expect(page.locator(':invalid')).toHaveCount(0);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`registration-content-edit-${width}.png`),fullPage:true});
  214 |  const expected=candidate('RegistrationContent');if(expected.family!=='RegistrationContent')throw new Error('Unexpected synthetic binding');expected.value.termsVersion='fixture-reviewed-terms';await expect(save).toBeEnabled();await save.focus();await expect(save).toBeFocused();await page.keyboard.press('Enter');await expect(page.getByRole('button',{name:'Retry same request',exact:true})).toBeVisible();expect(server.writes).toHaveLength(1);expect(server.writes[0]).toMatchObject({path:'/registration-content/draft',body:{proposalReference:proposalId,expectedRevision:1}});expect(server.writes[0]!.body.candidate).toEqual(expected.value);expect(Object.keys(server.writes[0]!.body.candidate as object).sort()).toEqual(['registrationPolicyVersion','termsVersion','privacyVersion','configurationVersionReference','qualificationReference','contentScope','variantReference','effectiveInterval','termsUri','privacyUri','supportUri'].sort());await expect(termsId).toHaveValue('fixture-reviewed-terms');await expect(privacyId).toHaveValue('fixture-privacy-v2');await page.getByRole('button',{name:'Retry same request',exact:true}).click();await expect(page.getByText(labels.replayed,{exact:true})).toBeVisible();expect(server.writes[1]!.body).toEqual(server.writes[0]!.body);expect(server.revision).toBe(2);
  215 |  await expect(page.getByRole('button',{name:'Validate candidate',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Validate candidate',exact:true}).click();await page.getByRole('button',{name:'Review configuration',exact:true}).click();await expect(page.getByRole('button',{name:'Approve candidate',exact:true})).toBeEnabled();await page.getByLabel('Compare with published version',{exact:true}).selectOption(publicationId);
  216 |  const baseline=candidate('RegistrationContent',true);if(baseline.family!=='RegistrationContent')throw new Error('Unexpected synthetic historical binding');const assertBindingDiff=async(value:typeof expected,column:'current'|'proposed')=>{const v=value.value,fields:Record<string,string>={registrationPolicyVersion:v.registrationPolicyVersion,termsVersion:v.termsVersion,privacyVersion:v.privacyVersion,configurationVersionReference:v.configurationVersionReference,qualificationReference:v.qualificationReference,contentScope:v.contentScope,variantReference:v.variantReference,termsUri:v.termsUri,privacyUri:v.privacyUri,supportUri:v.supportUri??'—','interval.from':v.effectiveInterval.from,'interval.includesFrom':String(v.effectiveInterval.includesFrom),'interval.until':v.effectiveInterval.until??'—','interval.includesUntil':String(v.effectiveInterval.includesUntil)};const rows=page.locator('.configuration-diff [data-field]');await expect(rows).toHaveCount(14);expect((await rows.evaluateAll(elements=>elements.map(element=>element.getAttribute('data-field')))).sort()).toEqual(Object.keys(fields).sort());for(const [key,text] of Object.entries(fields))await expect(page.locator('[data-field="'+key+'"]').locator('td').nth(column==='current'?0:1)).toContainText(text);};
  217 |  await assertBindingDiff(expected,'proposed');await assertBindingDiff(baseline,'current');expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`registration-content-review-${width}.png`),fullPage:true});await page.getByRole('button',{name:'Approve candidate',exact:true}).click();await expect(page.getByRole('button',{name:'Publish configuration',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Publish configuration',exact:true}).click();await fillPublication(page);await page.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);await expect.poll(()=>server.writes.length).toBe(5);expect(server.writes[4]).toMatchObject({path:'/proposals/publish',body:{proposalReference:proposalId,expectedRevision:4,supersedesPublicationReference:publicationId,effectiveInterval:{from:'2099-01-01T00:00:00.0000001Z',includesFrom:false,until:null,includesUntil:false}}});
  218 |  await page.getByRole('link',{name:'Publication history',exact:true}).click();await expect(page.locator('main[aria-busy="false"] .configuration-history')).toHaveCount(1);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`registration-content-history-${width}.png`),fullPage:true});await page.getByRole('button',{name:'Republish approved version',exact:true}).click();await expect(page.getByRole('dialog')).toBeVisible();await page.getByRole('dialog').locator('summary').click();await assertBindingDiff(baseline,'proposed');await fillPublication(page);await page.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();await expect.poll(()=>server.writes.length).toBe(6);expect(server.writes[5]).toMatchObject({path:'/rollback',body:{approvedVersionReference:versionId,supersedesPublicationReference:publicationId,effectiveInterval:{from:'2099-01-01T00:00:00.0000001Z',includesFrom:false,until:null,includesUntil:false}}});expect(server.writes.map(write=>write.path)).toEqual(['/registration-content/draft','/registration-content/draft','/proposals/validate','/proposals/approve','/proposals/publish','/rollback']);
  219 |  expect(new URL(page.url()).search).toBe('');expect(new URL(page.url()).hash).toBe('');expect(page.url()).not.toContain(proposalId);expect(page.url()).not.toContain(publicationId);expect(page.url()).not.toContain('fixture-reviewed-terms');expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);await expect(page.locator('[data-content-key="admin.identity.configuration.title"]').first()).toHaveAttribute('data-content-publication','fixture-chrome-p1');
  220 | });
  221 | test('configuration Search independently validates its qualified budget and preserves input after server rejection',async({page},info)=>{
  222 |  test.setTimeout(60_000);const server=await fixture(page),queries:Record<string,unknown>[]=[],requests:string[]=[];
  223 |  page.on('request',request=>{if(new URL(request.url()).pathname.startsWith('/identity/configuration/'))requests.push(request.url());});
  224 |  await page.route('**/identity/configuration/proposals/search',async route=>{const body=route.request().postDataJSON() as Record<string,unknown>;queries.push(body);if(String(body.search).startsWith('fixture-unavailable-')){expect(route.request().headers()['x-tunner-csrf']).toBe('fixture-configuration-csrf');return route.fulfill({status:503,contentType:'application/json',body:JSON.stringify({error_code:'DEPENDENCY_UNAVAILABLE',detail:'fixture-private-server-detail'})});}return route.fallback();});
  225 |  for(const width of [1440,768,390])await test.step('Search at '+width,async()=>{
  226 |   await page.setViewportSize({width,height:width===390?844:1024});await select(page,'Authentication',false);const search=page.getByLabel('Search candidates',{exact:true}),submit=page.getByRole('button',{name:'Search candidates',exact:true}),error=page.locator('#configuration-search-error');await expect(search).toBeEnabled();await expect(search).toHaveAttribute('maxlength','64');await search.evaluate(input=>input.removeAttribute('maxlength'));
  227 |   const beforeQueries=queries.length,beforeRequests=requests.length;await search.fill('x'.repeat(65));await expect(search).toHaveValue('x'.repeat(65));await search.press('Tab');await expect(search).toHaveAttribute('aria-invalid','true');await expect(search).toHaveAttribute('aria-describedby',/(?:^| )configuration-search-error(?: |$)/);await expect(error).toBeVisible();await expect(error).toHaveText(labels.validationSummary);await expect(error).not.toContainText(labels.invalidNumber);await submit.focus();await expect(submit).toBeFocused();await page.keyboard.press('Enter');await expect(search).toBeFocused();expect(queries).toHaveLength(beforeQueries);expect(requests).toHaveLength(beforeRequests);expect(server.writes).toHaveLength(0);await expect(page.locator(':invalid')).toHaveCount(0);
  228 |   expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`configuration-search-error-${width}.png`),fullPage:true});
  229 |   const bounded='s'.repeat(64);await search.fill(bounded);await search.press('Tab');await expect(search).not.toHaveAttribute('aria-invalid','true');await expect(error).toHaveCount(0);await submit.focus();await page.keyboard.press('Enter');await expect.poll(()=>queries.length).toBe(beforeQueries+1);expect(queries.at(-1)).toEqual({scope:scopes.Authentication,search:bounded,pageSize:5,offset:0});await expect(search).toBeEnabled();await expect(search).toHaveValue(bounded);
  230 |   const unavailable='fixture-unavailable-'+width;await search.fill(unavailable);await submit.focus();await page.keyboard.press('Enter');await expect.poll(()=>queries.length).toBe(beforeQueries+2);expect(queries.at(-1)).toEqual({scope:scopes.Authentication,search:unavailable,pageSize:5,offset:0});await expect(page.getByText(labels.unavailable,{exact:true})).toBeVisible();await expect(search).toHaveValue(unavailable);await expect(page.getByText('fixture-private-server-detail',{exact:true})).toHaveCount(0);expect(server.writes).toHaveLength(0);expect(new URL(page.url()).search).toBe('');expect(new URL(page.url()).hash).toBe('');expect(page.url()).not.toContain(unavailable);
  231 |  });
  232 | });
  233 | function passkeyCandidate(baseline=false):Extract<Candidate,{family:'Passkey'}>{const value=candidate('Passkey',baseline);if(value.family!=='Passkey')throw new Error('Wrong synthetic Passkey family');return value;}
  234 | function passkeyFields(value:ReturnType<typeof passkeyCandidate>):Record<string,string>{
  235 |  const m=value.value.metadata,v=value.value.values,fields:Record<string,string>={'metadata.version':m.versionId,'metadata.status':m.status,'metadata.supersedes':m.supersedesVersionId??'—','metadata.scope':m.scope,'evidence.0':m.approvalEvidenceReferences[0]!,'interval.from':m.effectiveInterval.from,'interval.includesFrom':String(m.effectiveInterval.includesFrom),'interval.until':m.effectiveInterval.until??'—','interval.includesUntil':String(m.effectiveInterval.includesUntil)};
  236 |  for(const [key,item] of Object.entries(v))if(key!=='allowedAlgorithms'&&key!=='assuranceMappings')fields['values.'+key]=String(item);v.allowedAlgorithms.forEach((item,index)=>{fields['algorithm.'+index]=String(item);});for(const [key,item] of Object.entries(v.assuranceMappings))fields['method.'+key]=item;return fields;
  237 | }
  238 | async function assertPasskeyDiff(page:Page,value:ReturnType<typeof passkeyCandidate>,column:'current'|'proposed',other?:ReturnType<typeof passkeyCandidate>){
  239 |  const fields=passkeyFields(value),keys=[...new Set([...Object.keys(fields),...Object.keys(other?passkeyFields(other):{})])].sort();expect((await page.locator('.configuration-diff [data-field]').evaluateAll(rows=>rows.map(row=>row.getAttribute('data-field')))).sort()).toEqual(keys);
  240 |  for(const [key,item] of Object.entries(fields))await expect(page.locator('[data-field="'+key+'"]').locator('td').nth(column==='current'?0:1)).toContainText(item);
> 241 |  const effect=page.locator('[data-field="values.revocationSessionEffect"]').locator('td').nth(column==='current'?0:1);await expect(effect).toContainText(value.value.values.revocationSessionEffect===1?labels.preserveExistingSessions:labels.revokeCredentialSessions);await expect(effect.locator('[data-content-publication="fixture-chrome-p1"]')).toHaveCount(1);
      |                                                                                                                                                                                                                                                                                                                                                         ^ Error: expect(locator).toHaveCount(expected) failed
  242 |  // Historical as well as current/proposed previews must disclose the three constructor invariants.
  243 |  for(const [key,item] of Object.entries({residentKeyRequirement:'required',userVerificationRequirement:'required',attestationConveyancePreference:'none'}))await expect(page.locator('[data-field="values.'+key+'"]').locator('td').nth(column==='current'?0:1)).toContainText(item);
  244 | }
  245 | for(const width of [1440,390])test(`Passkey typed policy validates ordered inputs, retries exact intent and reviews immutable history at ${width}`,async({page},info)=>{
  246 |  test.setTimeout(90_000);await page.setViewportSize({width,height:width===1440?1024:844});const server=await fixture(page,{unknownOnce:true});await select(page,'Passkey');const save=page.getByRole('button',{name:'Save draft',exact:true}),panel=page.locator('.configuration-fixed');await expect(panel).toHaveCSS('background-color','rgb(247, 249, 250)');await expect(page.locator('.configuration-fixed + .configuration-fields #configuration-version')).toHaveCount(1);
  247 |  for(const [key,item] of Object.entries({residentKeyRequirement:'required',userVerificationRequirement:'required',attestationConveyancePreference:'none'})){await expect(panel.locator('dt').filter({hasText:labels[key as ConfigurationLabel]}).locator('..').locator('dd')).toHaveText(item);await expect(page.locator('[name="'+key+'"]')).toHaveCount(0);}
  248 |  const invalid=async(name:string,bad:string,corrected:string)=>{const field=page.locator('[name="'+name+'"]');await field.fill(bad);await field.press('Tab');await expect(field).toHaveAttribute('aria-invalid','true');await save.focus();await page.keyboard.press('Enter');await expect(field).toBeFocused();expect(server.writes).toHaveLength(0);await save.evaluate(button=>(button as HTMLButtonElement).form!.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})));expect(server.writes).toHaveLength(0);await expect(page.getByRole('alert')).toContainText(labels.validationSummary);await expect(page.locator(':invalid')).toHaveCount(0);await field.fill(corrected);await field.press('Tab');await expect(field).not.toHaveAttribute('aria-invalid','true');};
  249 |  await invalid('origin','https://fixture-user:fixture-password@login.fixture.example.test','https://login.fixture.example.test');await invalid('origin','https://unrelated.example.test','https://login.fixture.example.test');await invalid('authenticatorTimeout','121','30.0000001');await invalid('challengeBytes','15','32');await invalid('algorithm.0','-7.5','-257');await invalid('algorithm.0','2147483648','-257');await invalid('assurance.0','AAL3','fixture-passkey-assurance');
  250 |  const secondKey=page.locator('[name="method.1"]');await secondKey.selectOption(passkeyFactKeys[0]);await secondKey.press('Tab');await expect(secondKey).toHaveAttribute('aria-invalid','true');await save.focus();await page.keyboard.press('Enter');await expect(secondKey).toBeFocused();expect(server.writes).toHaveLength(0);await secondKey.selectOption(passkeyFactKeys[1]);await secondKey.press('Tab');await expect(secondKey).not.toHaveAttribute('aria-invalid','true');
  251 |  await page.locator('[name="algorithm.0"]').fill('-999');await page.locator('[name="algorithm.0"]').press('Tab');await expect(page.locator('[name="algorithm.1"]')).toHaveValue('-7');await expect(page.locator('[name="authenticatorTimeout"]')).toHaveValue('30.0000001');expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`passkey-edit-${width}.png`),fullPage:true});
  252 |  const expected=passkeyCandidate();expected.value.values.allowedAlgorithms=[-999,-7];await save.dblclick();await expect(page.getByRole('button',{name:'Retry same request',exact:true})).toBeVisible();expect(server.writes).toHaveLength(1);expect(server.writes[0]).toMatchObject({path:'/passkey/draft',body:{proposalReference:proposalId,expectedRevision:1}});expect(server.writes[0]!.body.candidate).toEqual(expected.value);expect(Object.keys((server.writes[0]!.body.candidate as typeof expected.value).values)).toHaveLength(20);await expect(page.locator('[name="algorithm.0"]')).toHaveValue('-999');await page.getByRole('button',{name:'Retry same request',exact:true}).click();await expect(page.getByText(labels.replayed,{exact:true})).toBeVisible();expect(server.writes[1]!.body).toEqual(server.writes[0]!.body);expect(server.revision).toBe(2);
  253 |  await page.getByRole('button',{name:'Validate candidate',exact:true}).click();await page.getByRole('button',{name:'Review configuration',exact:true}).click();await expect(page.getByRole('button',{name:'Approve candidate',exact:true})).toBeEnabled();await page.getByLabel('Compare with published version',{exact:true}).selectOption(publicationId);const baseline=passkeyCandidate(true);await assertPasskeyDiff(page,expected,'proposed',baseline);await assertPasskeyDiff(page,baseline,'current',expected);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`passkey-review-${width}.png`),fullPage:true});
  254 |  await page.getByRole('button',{name:'Approve candidate',exact:true}).click();await expect(page.getByRole('button',{name:'Publish configuration',exact:true})).toBeEnabled();await page.getByRole('button',{name:'Publish configuration',exact:true}).click();await fillPublication(page);await page.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();await expect(page.getByRole('dialog')).toHaveCount(0);expect(server.writes[4]).toMatchObject({path:'/proposals/publish',body:{proposalReference:proposalId,expectedRevision:4,supersedesPublicationReference:publicationId}});
  255 |  await page.getByRole('link',{name:'Publication history',exact:true}).click();await expect(page.locator('main[aria-busy="false"] .configuration-history')).toHaveCount(1);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);await page.screenshot({path:info.outputPath(`passkey-history-${width}.png`),fullPage:true});await page.getByRole('button',{name:'Republish approved version',exact:true}).click();const dialog=page.getByRole('dialog');await expect(dialog).toBeVisible();await dialog.locator('summary').click();await assertPasskeyDiff(page,baseline,'proposed');await dialog.getByRole('button',{name:'Confirm publication',exact:true}).focus();await page.keyboard.press('Enter');await expect(dialog.getByLabel('Available from · UTC',{exact:true})).toBeFocused();await fillPublication(page);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);await page.keyboard.press('Escape');await expect(dialog).toHaveCount(0);await expect(page.getByRole('button',{name:'Republish approved version',exact:true})).toBeFocused();expect(server.writes).toHaveLength(5);await page.getByRole('button',{name:'Republish approved version',exact:true}).click();await fillPublication(page);await page.getByRole('dialog').getByRole('button',{name:'Confirm publication',exact:true}).click();await expect.poll(()=>server.writes.length).toBe(6);expect(server.writes.map(write=>write.path)).toEqual(['/passkey/draft','/passkey/draft','/proposals/validate','/proposals/approve','/proposals/publish','/rollback']);expect(server.writes[5]).toMatchObject({path:'/rollback',body:{approvedVersionReference:versionId,supersedesPublicationReference:publicationId}});
  256 |  expect(new URL(page.url()).search).toBe('');expect(new URL(page.url()).hash).toBe('');expect(page.url()).not.toContain(proposalId);expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
  257 | });
  258 | test('Passkey canonical routes guard direct entry refresh history and server capabilities',async({page})=>{
  259 |  test.setTimeout(90_000);const server=await fixture(page,{denyPublish:true});for(const view of ['edit','review','history'] as const){const path=configurationPath('Passkey',view),before=server.guards;await page.goto(path+'?candidate=discard#discard');await expect(page).toHaveURL('http://127.0.0.1:5298'+path);await expect.poll(()=>server.guards).toBeGreaterThan(before);await expect(page.locator('[data-tunner-screen="A-ID-SETTINGS-Passkey-'+view+'"]')).toBeVisible();await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');await page.getByLabel('Exact scope',{exact:true}).selectOption(scopeKey(scopes.Passkey));if(view!=='history'){await page.getByLabel('Saved candidate',{exact:true}).selectOption(proposalId);if(view==='edit')await expect(page.getByRole('button',{name:'Save draft',exact:true})).toBeEnabled();else await expect(page.getByRole('button',{name:'Publish configuration',exact:true})).toBeDisabled();}else await expect(page.locator('.configuration-history')).toHaveCount(1);const previous=server.guards;await page.reload();await expect.poll(()=>server.guards).toBeGreaterThan(previous);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');await expect(page.locator('.configuration-diff')).toHaveCount(0);const alternate=view==='edit'?'review':'edit';await page.getByRole('link',{name:labels[alternate],exact:true}).click();await expect(page).toHaveURL('http://127.0.0.1:5298'+configurationPath('Passkey',alternate));const beforeBack=server.guards;await page.goBack();await expect(page).toHaveURL('http://127.0.0.1:5298'+path);await expect.poll(()=>server.guards).toBeGreaterThan(beforeBack);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');const beforeForward=server.guards;await page.goForward();await expect(page).toHaveURL('http://127.0.0.1:5298'+configurationPath('Passkey',alternate));await expect.poll(()=>server.guards).toBeGreaterThan(beforeForward);await expect(page.getByLabel('Exact scope',{exact:true})).toHaveValue('');}expect(server.writes).toHaveLength(0);expect(await page.evaluate(()=>[localStorage.length,sessionStorage.length])).toEqual([0,0]);
  260 | });
  261 | test('Passkey laptop tablet and narrow form reflows with shared keyboard error correction',async({page},info)=>{
  262 |  test.setTimeout(120_000);const server=await fixture(page);for(const viewport of [{width:1024,height:768},{width:768,height:1024},{width:320,height:800}]){await page.setViewportSize(viewport);await select(page,'Passkey');const field=page.locator('[name="algorithm.0"]');await field.fill('');await field.press('Tab');await expect(field).toHaveAttribute('aria-invalid','true');await page.getByRole('button',{name:'Save draft',exact:true}).focus();await page.keyboard.press('Enter');await expect(field).toBeFocused();await expect(page.getByRole('alert')).toContainText(labels.validationSummary);expect(server.writes).toHaveLength(0);await field.fill('-257');await field.press('Tab');await expect(field).not.toHaveAttribute('aria-invalid','true');await expect(page.getByRole('alert')).toHaveCount(0);expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBe(true);expect(await page.locator('input:visible,select:visible,button:visible').evaluateAll(elements=>elements.every(element=>{const b=element.getBoundingClientRect();return b.width>0&&b.left>=-0.5&&b.right<=innerWidth+0.5;}))).toBe(true);expect((await new AxeBuilder({page}).analyze()).violations).toEqual([]);if(viewport.width!==1024)await page.screenshot({path:info.outputPath(`passkey-edit-${viewport.width}.png`),fullPage:true});}expect(server.writes).toHaveLength(0);
  263 | });
  264 | test('Passkey conditional publication preserves all six earlier families without new chrome',async({page})=>{
  265 |  const server=await fixture(page,{sixOnly:true});for(const family of ['Authentication','Assurance','AuthenticationContent','Registration','Totp','RegistrationContent'] as const){await select(page,family);await expect(page.getByRole('button',{name:'Save draft',exact:true})).toBeEnabled();await expect(page.getByRole('link',{name:labels.Passkey,exact:true})).toHaveCount(0);await expect(page.locator('[data-content-key="admin.identity.configuration.title"]').first()).toHaveAttribute('data-content-publication','fixture-chrome-p1');}await page.goto(configurationPath('Passkey'));await expect(page.getByRole('alert')).toContainText('The request could not be completed.');await expect(page.getByRole('button',{name:'Save draft',exact:true})).toHaveCount(0);expect(server.writes).toHaveLength(0);await page.unrouteAll();await fixture(page);await select(page,'Passkey',false);for(const input of await page.locator('main form input').all())await expect(input).toHaveValue('');for(const selectInput of await page.locator('main form select').all())await expect(selectInput).toHaveValue('');await page.getByRole('button',{name:'Save draft',exact:true}).click();await expect(page.locator('[name="version"]')).toBeFocused();
  266 | });
  267 | test('duration conversion preserves every supported tick and rejects overflow',()=>{for(const raw of ['0.0000001','120.0000001','86400','922337203685.4775807'])expect(durationToSeconds(secondsToDuration(raw))).toBe(raw);for(const raw of ['0','-1','1e3','0.00000001','922337203685.4775808'])expect(()=>secondsToDuration(raw)).toThrow();});
  268 | 
```