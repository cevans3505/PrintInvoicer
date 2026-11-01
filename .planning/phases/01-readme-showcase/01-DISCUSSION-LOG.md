# Phase 1: README Showcase - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md. This log preserves the alternatives considered.

**Date:** 2026-11-01
**Phase:** 01-readme-showcase
**Areas discussed:** Sample data shown, Image look and size

---

## Sample data shown

| Option | Description | Selected |
|--------|-------------|----------|
| Extend SampleShop | Add a paid job and more to the existing SampleShop, shared with tests | ✓ |
| Separate showcase data | New data used only for the README | |

| Option | Description | Selected |
|--------|-------------|----------|
| 6-10 jobs | Looks like a real shop | |
| 3-4 jobs | One of each state, simpler image | ✓ |

| Option | Description | Selected |
|--------|-------------|----------|
| FDM job with extras | Wire Brush Inserts with discount and customer-supplied filament | ✓ |
| Resin job | Shows .goo support | |
| One of each | Editor and invoice on different jobs | |

| Option | Description | Selected |
|--------|-------------|----------|
| Final, unpaid | The usual invoice a customer receives | ✓ |
| Final, marked PAID | Shows the PAID line | |

---

## Image look and size

| Option | Description | Selected |
|--------|-------------|----------|
| 2x (192 DPI) | Sharp, larger files | ✓ |
| 1x (96 DPI) | What tests save today | |

| Option | Description | Selected |
|--------|-------------|----------|
| Default 1100x680 | How users see it | ✓ |
| Smaller, about 900 wide | Fits README better, may wrap | |

| Option | Description | Selected |
|--------|-------------|----------|
| Window content only | Dark content, no title bar | ✓ |
| Add a frame | Drawn title bar and shadow | |

| Option | Description | Selected |
|--------|-------------|----------|
| Full page 1, ~110 DPI | Whole letter page | ✓ |
| Cropped to the top | Header, lines and totals only | |

---

## Claude's Discretion

- The form of the regenerate command (script plus tagged UI test, never rewriting committed images on normal runs)
- README placement, captions and the regeneration note
- Stable output (fixed dates) and file size

## Deferred Ideas

- Invoice render-comparison test (v2, FIX2-06) may reuse the generator
