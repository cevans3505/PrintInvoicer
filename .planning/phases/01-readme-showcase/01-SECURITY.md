---
phase: "01"
slug: "readme-showcase"
status: verified
# threats_open = count of OPEN threats at or above workflow.security_block_on severity (the blocking gate)
threats_open: 0
asvs_level: 1
created: "2026-11-01"
---

# Phase 01: Security

> Per-phase security contract: threat register, accepted risks, and audit trail.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| test run -> repository files | The README image test can write into the working tree (`docs/images`); a normal run must not | PNG files, public once committed |
| sample data -> public README | Whatever the sample shop holds becomes a public picture on GitHub | Sample business and customer names (public) |
| maintainer environment -> test process | `PRINTINVOICER_README_IMAGES` names an output folder chosen by the maintainer's own script | A folder path (local only) |
| generator -> sample database | The generator reads the Wire Brush job and must not change it | Job rows in a temporary test database |
| docs -> auditors | AUDITING.md must list every file write, including developer tools | Documentation |

---

## Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation | Status |
|-----------|----------|-----------|----------|-------------|------------|--------|
| T-01-01 | Tampering | `ReadmeImageTests.Save`, `docs/images` | high | mitigate | Copies to `docs/images` only when `PRINTINVOICER_README_IMAGES` is set (`ReadmeImageTests.cs:22`); only `make-readme-images.ps1:15` sets it and line 22 clears it in `finally`; not present in `.github/` or `publish.ps1` | closed |
| T-01-02 | Information disclosure | `SampleShop` data in `main-jobs.png` | high | mitigate | Sample names only (`SampleShop.cs:34` "Harbor Dive Shop"); pictures viewed by the executor and the verifier, sample names only | closed |
| T-01-03 | Tampering | Output folder from `PRINTINVOICER_README_IMAGES` | low | accept | See accepted risks AR-01 | closed |
| T-01-04 | Information disclosure | Network from the app during regeneration | low | mitigate | `WpfHost.cs:48` sets `MainWindow.CheckForUpdatesOnLaunch = false` before any window opens | closed |
| T-01-05 | Denial of service | The maintainer's desktop (focus stolen or a modal prompt) | low | mitigate | Windows open only through `WpfHost.Offscreen` (`ReadmeImageTests.cs:70`, `:110`); no `ShowDialog`, `Activate`, `Focus` or `MessageBox` in `ReadmeImageTests.cs` | closed |
| T-01-06 | Information disclosure | `job-editor.png`, `invoice-page-1.png` | high | mitigate | Source files shown through `Path.GetFileName` (`InvoicePdf.cs:168-169`, `PartViewModel.cs:76-77`); pictures viewed by the verifier, sample names only | closed |
| T-01-07 | Tampering | Sample database: finalizing the Wire Brush job for the invoice | medium | mitigate | No `SaveJob` or `Save(` of a job in `ReadmeImageTests.cs`; `ReadmeImageTests.cs:152` asserts the reloaded job is still not `Finalized` | closed |
| T-01-08 | Repudiation | AUDITING.md missing the new developer-tool access | low | mitigate | `AUDITING.md:63` and `:112` describe `make-readme-images.ps1`; 2 `# AUDIT:` tags in the script, 2 `// AUDIT:` in `WpfHost.cs`, 4 in `ReadmeImageTests.cs` | closed |
| T-01-SC | Tampering | NuGet / npm / pip installs | low | accept | See accepted risks AR-02 | closed |

*Status: open · closed · open, below high threshold (non-blocking)*
*Severity: critical > high > medium > low; only open threats at or above workflow.security_block_on count toward threats_open*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| AR-01 | T-01-03 | The folder is set by the maintainer's own script on their own PC; only fixed file names from code are written; developer tool, never shipped | Plan 01-01 threat model | 2026-11-01 |
| AR-02 | T-01-SC | No packages were added in this phase, so there is nothing to vet | Plans 01-01 and 01-02 threat models | 2026-11-01 |

*Accepted risks do not resurface in future audit runs.*

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Run By |
|------------|---------------|--------|------|--------|
| 2026-11-01 | 9 | 9 | 0 | gsd-secure-phase (ASVS L1 grep verification, orchestrator) |

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: verified` set in frontmatter

**Approval:** verified 2026-11-01
