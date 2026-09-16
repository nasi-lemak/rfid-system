# Vertical journey — Healthcare: CSSD reprocessing and medical devices

Template `medical-assets`. Item types MED-DEVICE (Clean → InUse → Dirty → Cleaning → Clean;
UnderMaintenance; inspection 365 d), TRAY (container; Sterilised → InUse → Dirty → Decontaminated →
Sterilised, cycle +1), INSTRUMENT (cycles, max 500). Custom operations **Decontaminate** (Dirty →
Decontaminated) and **Sterilise** (Decontaminated → Sterilised, cycle +1, `lastSterilisedAt`).
Workflow **CSSD reprocessing** (Receive dirty trays → Decontaminate [rescan] → Sterilise [rescan,
asks toLocation]). Rules *Dirty device in clean area* (Critical), *Tray issued unsterilised* (Critical).

Markers: **[H]** handheld · **[F]** fixed reader · **[A]** automatic · **[W]** web.

## 1. Deployment shape

```
   Theatre                CSSD dirty side         Washer      Autoclave       Sterile store
 ┌────────────┐  Return  ┌──────────────┐        ┌──────┐    ┌──────────┐    ┌──────────────┐
 │ InUse      │────────► │ Dirty        │──────► │Decon.│──► │Sterilise │──► │ Sterilised   │
 │ [F] door   │◄──────── │ [H] workflow │        └──────┘    └──────────┘    │ clean = true │
 └────────────┘  Issue   └──────────────┘                                     │ [F] door (In)│
                                                                              └──────────────┘
```

The sterile store and theatres carry `clean: true`; the rule *Dirty device in clean area* fires on a
Moved event into such a zone while the item is Dirty/Cleaning.

## 2. Happy path — one tray, one cycle

```
[USER][H]  Issue: scan trays → Party "Theatre 4 team" (or Location) → Submit            Sterilised→InUse
[HW][F]    Theatre door antenna reads trays                                              Moved into Theatre 4
    …surgery…
[USER][H]  Workflows → CSSD reprocessing
           1 "Receive dirty trays": scan returned trays → Submit                          InUse→Dirty, custody cleared
           2 "Decontaminate" (rescan): scan trays coming out of the washer → Submit       Dirty→Decontaminated
           3 "Sterilise" (rescan): scan trays out of the autoclave → dest "Sterile store"  Decontaminated→Sterilised, cycle +1, lastSterilisedAt, Moved
[AUTO][A]  INSTRUMENT cycle counting (if instruments are tagged and packed in the tray) — Sterilise applies to TRAY and INSTRUMENT
[HW][F]    Sterile store door (In) reads trays                                             Moved; rule: state Sterilised → no alert
```

Each step is a separate scan set (`rescan`) because the trays physically pass through machines
between steps; the workflow's value is enforcing order and recording `lastSterilisedAt`.

## 3. Exception paths

| Situation | What happens | Who sees what | Label |
|---|---|---|---|
| **Tray issued while Dirty/Decontaminated** | TRAY lifecycle only allows Issue from Sterilised → line **Rejected** "Issue not allowed while TRAY-7 is 'Dirty'" — the tray is *not* issued | operator: rejected line | works; this is the primary safety guard |
| **Tray *moved* (not issued) into a theatre while Dirty** | [F] read at `clean: true` zone → rule *Dirty device in clean area* → **Critical alert** | web | works (fixed-reader driven) |
| **Rule "Tray issued unsterilised"** | can only fire if custody is set while state ≠ InUse; the lifecycle prevents Issue from non-Sterilised states, so the rule catches web-side Transfer-with-custodian or a lifecycle edit | web | works as defence in depth |
| **Tray skipped decontamination** (operator scans it at Sterilise directly) | Sterilise requires Decontaminated → Rejected "No transition from 'Dirty' to 'Sterilised'" | operator | works |
| **Instrument missing from a tray** | not detectable unless instruments are individually tagged and Packed into the tray; then a Count of the tray container's children on the web shows the set | web | **Missing product capability**: tray completeness view (list expected instruments per tray vs present). Template mentions "tray completeness" in its description but no screen implements it. P2 for CSSD pilots |
| **Autoclave batch failed (biological indicator)** | no operation; operators would Process stage back to Dirty (Sterilised→? no transition) — the lifecycle has no *Sterilised→Dirty* except via InUse | supervisor | **Recommended improvement** (content): add a `Recall`/`Reprocess` transition Sterilised→Dirty |
| **Max cycles for instruments** | Warning via a rule? The template has no max-cycles rule for INSTRUMENT (linen has one) | — | Recommended improvement (content) |
| **Network down in CSSD** | workflow steps queue in order; server applies in order per batch | operator | works |
| **Two handhelds reprocess the same tray** | second submit Rejected (no transition) or duplicate-ok; idempotency is per clientId, not per tray | | works |

## 4. Gaps specific to this vertical

| Gap | Label | Priority |
|---|---|---|
| Tray completeness (expected instrument set vs scanned) | Missing product capability | P2 |
| Reprocess/recall transition for a failed sterilisation batch | Recommended improvement (template content) | P2 |
| Max-cycle rule for instruments | Recommended improvement (content) | P3 |
| `lastSterilisedAt` not shown on handheld Lookup as a first-class field (it is in attributes) | Missing UX | P3 |
