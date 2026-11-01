---
phase: 01
review: 01-REVIEW.md
titles: json
findings:
  - id: WR-01
    severity: warning
    disposition: fixed
    title: "Windows are leaked when a picture precondition fails"
  - id: WR-02
    severity: warning
    disposition: open
    title: "AUDITING.md says a normal test run writes only to the bin folder, which is not true"
  - id: IN-01
    severity: info
    disposition: open
    title: "RenderPng depends on the template's first visual child"
  - id: IN-02
    severity: info
    disposition: open
    title: "Dev script and test have small robustness gaps"
open: 3
total: 4
recorded: 2026-11-01T23:34:40.191Z
---

# Phase 01: Code Review Disposition

| Finding | Severity | Disposition | Source |
|---------|----------|-------------|--------|
| WR-01 | warning | fixed | 01-REVIEW-FIX.md |
| WR-02 | warning | open | - |
| IN-01 | info | open | - |
| IN-02 | info | open | - |

Dispositions: `open` (recorded, not yet triaged), `fixed`, `skipped`, `deferred`.
Set `deferred` by hand and put the reason in the Source cell; both are preserved. A `|` in the reason is kept as prose and escaped on the next run.
Re-running the gate keeps every row it can. A row the current review no longer reports is kept and its Source cell flagged, so a finding does not leave this record silently. ONE exception: when a finding id is REUSED by a different finding, the earlier decision cannot keep a row — the id is taken — and it is dropped. A RECORDED decision (anything but `open`) is named on the console when that happens; a row still at `open` is replaced silently, because `open` records no decision to lose.
