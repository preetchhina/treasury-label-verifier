# Label Review Assistant

An AI-assisted alcohol-label pre-screening prototype for Treasury's TTB take-home assignment. A reviewer uploads label artwork, enters application values, and receives field-by-field comparisons with extracted evidence, confidence, and reasons. Results deliberately end at **human review**; they are not legal approvals or rejections.

**Live prototype:** [treasury-label-verifier-tot3.onrender.com](https://treasury-label-verifier-tot3.onrender.com)

For a quick demo, run the application and select **Run clear sample** followed by **Run issue sample**. The first shows exact and formatting-only matches; the second demonstrates warning capitalization and punctuation failures with extracted evidence.

## What the prototype does

- Analyzes arbitrary PNG, JPEG, and WebP label images with local machine-learning OCR—no cloud AI account, API key, or usage charge is required.
- Compares brand name, class/type, alcohol content, net contents, producer/bottler, country of origin, and the federal government warning.
- Distinguishes exact matches, harmless formatting differences, substantive mismatches, unreadable fields, low-confidence extraction, and application fields not supplied.
- Checks warning wording, capitalization, and punctuation deterministically after extraction.
- Reports only image-supported visual conclusions. Local OCR can check heading case; font weight, layout, contrast, and physical size remain explicit human-review items.
- Supports five images per request, with two concurrent OCR processes and independent partial-error results.
- Handles invalid/oversized files, timeouts, missing OCR, unreadable images, and partial batch failures with plain-language recovery guidance.
- Retains no uploads: validated bytes are streamed through the OCR process and discarded after the request. There is no upload directory, database, or object storage.

## Quick start

Prerequisites:

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Tesseract OCR 5](https://tesseract-ocr.github.io/tessdoc/Installation.html) with English language data

macOS:

```bash
brew install tesseract
dotnet restore
dotnet run --project src/TreasuryLabelVerifier.Web
```

Debian/Ubuntu:

```bash
sudo apt-get update
sudo apt-get install tesseract-ocr tesseract-ocr-eng
dotnet restore
dotnet run --project src/TreasuryLabelVerifier.Web
```

Open the local URL printed by ASP.NET Core. Click **Run clear sample** or **Run issue sample**, or upload a label. No secret configuration is needed.

## Architecture and data flow

```text
Browser (accessible Razor Pages form)
        │ multipart POST + anti-forgery token
        ▼
File validation (count, length, PNG/JPEG/WebP magic bytes)
        │ in-memory byte stream
        ▼
TesseractLabelExtractor (local OCR subprocess, TSV confidence output)
        │ parsed lines + bounding-order/confidence data
        ▼
OcrTextInterpreter (evidence selection; no invented label text)
        │ typed LabelExtraction
        ▼
ComparisonEngine (deterministic C# normalization and warning rules)
        │
        ▼
Evidence + status + reason + confidence + human-review visual checks
```

Application values help locate likely OCR evidence, but the observed value shown in the result always comes from OCR text. A similarity score below 42% is treated as unreadable; extraction confidence below 70% is routed to review. Neither threshold is a TTB rule.

### Project structure

```text
src/TreasuryLabelVerifier.Web/
  Configuration/       validated runtime options
  Domain/              application, extraction, and result types
  Services/            OCR, interpretation, comparison, and file validation
  Pages/               accessible Razor Pages UI
  wwwroot/samples/     reproducible SVG and PNG fixtures
tests/TreasuryLabelVerifier.Tests/
  comparison, OCR interpretation, TSV parsing, and upload-validation tests
```

## Technology choices

- **ASP.NET Core 8 Razor Pages / C#** provides a typed, server-rendered application aligned with the stakeholder's .NET environment and minimizes client-side complexity.
- **Tesseract 5 local OCR** provides credential-free text recognition inside the deployment container. It avoids external-domain firewall dependencies, prevents API-key exposure, and keeps uploaded label bytes within the application host.
- **Deterministic C# comparison** owns normalization, numeric ABV/proof and volume checks, warning strictness, confidence routing, and overall result severity. OCR never decides legal compliance.
- **Docker** packages .NET, Tesseract, and English trained data into a reproducible non-root deployment.
- **xUnit** covers the highest-risk comparison, OCR interpretation, TSV parsing, and upload-validation behavior.

The trade-off for eliminating a hosted vision model is weaker extraction on curved bottles, glare, decorative fonts, multilingual text, and complex layouts. The UI exposes uncertainty instead of turning an OCR guess into a legal conclusion.

## Configuration

ASP.NET Core uses `__` for nested environment-variable keys.

| Variable | Default | Purpose |
|---|---:|---|
| `LabelAnalysis__Provider` | `LocalOcr` | `LocalOcr`; `Demo` exists only for deterministic fixture development |
| `LabelAnalysis__TesseractPath` | `tesseract` | OCR executable name or absolute path |
| `LabelAnalysis__TimeoutSeconds` | `15` | Per-image timeout, 1–60 seconds |
| `LabelAnalysis__MaxBatchSize` | `5` | Per-request image cap, 1–10 |
| `LabelAnalysis__MaxFileSizeMb` | `10` | Per-image cap, 1–25 MB |
| `PORT` | platform supplied or `8080` in Docker | Hosting platform HTTP port |

See `.env.example`. There are intentionally no API keys or other secrets.

## Tests and quality checks

```bash
dotnet format TreasuryLabelVerifier.sln --verify-no-changes
dotnet build TreasuryLabelVerifier.sln --configuration Release
dotnet test TreasuryLabelVerifier.sln --configuration Release --no-build
dotnet publish src/TreasuryLabelVerifier.Web --configuration Release --no-build --output ./artifacts/publish
```

The 14 focused automated tests cover:

- formatting-only brand differences;
- ABV/proof mismatches and equivalent liter/milliliter quantities;
- exact warning case and punctuation;
- unreadable and below-70%-confidence fields;
- warning visual mismatches;
- OCR evidence mapping without substituting application text;
- missing-warning behavior and TSV word/line confidence parsing;
- file-signature spoofing, valid PNG signatures, and empty uploads.

Manual browser verification covers both sample POST workflows, evidence rendering, semantic labels/headings, error states, and responsive layout. The SVG fixtures are editable; regenerate them with `sips -s format png input.svg --out output.png` on macOS or Inkscape elsewhere.

## Observed latency

Measured locally on August 10, 2026, after a warm Release build:

- clear 800 × 1000 PNG, local OCR + interpretation + comparison: **423 ms** server-reported;
- automated comparison/test suite: **14 tests passed** after build.

This comfortably meets the approximately five-second target for the representative fixture on the development machine. It is not a production percentile or accuracy benchmark. Cold starts, host CPU throttling, larger images, and multiple files will change performance; the UI enforces a 15-second per-image timeout and reports failures independently.

## Supported fields and comparison behavior

| Field | Behavior |
|---|---|
| Brand name | Exact text; case/punctuation/spacing-only differences are informational |
| Class/type | Exact text; case/punctuation/spacing-only differences are informational |
| Alcohol content | Numeric ABV comparison; optional proof formatting tolerated; conflicting proof flagged |
| Net contents | Exact or equivalent liters/milliliters |
| Producer/bottler | Optional normalized text comparison |
| Country of origin | Optional normalized comparison; never inferred from an address |
| Government warning | Prescribed wording, capitalization, and punctuation after line-wrap normalization |
| Warning heading case | OCR-supported; low-confidence evidence is routed to review |
| Font weight/layout/contrast/size | Unknown and routed to trained human review; no unsupported visual claim |

The category selector records distilled spirits, wine, or malt beverage context, but the prototype applies the same common-field form to each category. It does not yet change mandatory fields by beverage-specific exception.

## Assumptions and trade-offs

- Direct COLA integration is out of scope, so application data is entered manually.
- Each selected image is compared independently with one shared application record. Production importer batches need per-item metadata, a queue, resumability, and bounded workers.
- Five files and concurrency two protect memory and shared-host CPU.
- A 42% text-similarity floor and 70% extraction-confidence threshold are product assumptions to be calibrated with representative labeled data.
- Ordinary name/type case and punctuation differences are harmless formatting differences; prescribed warning differences are mismatches.
- The included artwork is synthetic test data, not an approved label or evidence of compliance.

## Known limitations

- Tesseract OCR is less robust than a modern hosted vision-language model on glare, curved containers, low resolution, rotated/vertical text, handwriting, ornate fonts, and non-English text.
- OCR confidence is engine confidence, not calibrated regulatory accuracy. Evidence must be checked against the original image.
- A single panel may omit required information elsewhere on the container; “unreadable” is not proof that the complete container lacks a field.
- Font boldness, non-bold body text, continuous-paragraph layout, separation, contrast, and physical millimeter size are not automatically proven by this no-key version.
- The app does not cover every beverage-specific rule, same-field-of-vision geometry, misleading claims, standards of identity, formula issues, age/origin statements, ingredients, state rules, or COLA status.
- Batch results are request-scoped; there is no history, export, authentication, audit log, or annotation workflow.
- Free hosting can cold-start and has no production SLA.

## Privacy and security

- Uploaded bytes remain request-scoped and are streamed to a local OCR process through standard input. Application code does not write images to disk, logs, a database, object storage, browser storage, or an external AI service.
- No API key or external AI account is used.
- Extension/MIME claims are untrusted; PNG/JPEG/WebP signatures, count, and size are validated before OCR.
- OCR output is treated as untrusted evidence and mapped into typed records before deterministic comparison.
- Anti-forgery protection, CSP, `nosniff`, frame denial, no-referrer policy, production HSTS, generic unexpected-error messages, bounded concurrency, timeouts, and a non-root container user are enabled.
- A production system still needs authentication/authorization, approved telemetry redaction, rate limiting, malware/content policy, dependency scanning, and a formal retention schedule.

## Accessibility and usability

The server-rendered workflow uses semantic regions, headings, labels, validation summaries, live loading text, keyboard-focus styles, a skip link, text-plus-color statuses, reduced-motion support, responsive layout, and plain-language recovery messages. Tables keep headers and scroll horizontally on narrow screens. Formal Section 508/WCAG conformance testing remains future work.

## Authoritative regulatory references

Current regulations and TTB guidance always control:

- [27 CFR § 16.21 — mandatory warning text](https://www.ecfr.gov/current/title-27/chapter-I/subchapter-A/part-16/subpart-C/section-16.21)
- [TTB — Distilled Spirits Health Warning Statement](https://www.ttb.gov/regulated-commodities/beverage-alcohol/distilled-spirits/ds-labeling-home/ds-health-warning)
- [TTB — Distilled Spirits Mandatory Label Information](https://www.ttb.gov/regulated-commodities/beverage-alcohol/distilled-spirits/ds-labeling-home/ds-brand-label)
- [TTB — Distilled Spirits Alcohol Content](https://www.ttb.gov/regulated-commodities/beverage-alcohol/distilled-spirits/ds-labeling-home/ds-alcohol-content)
- [TTB — Distilled Spirits Net Contents](https://www.ttb.gov/regulated-commodities/beverage-alcohol/distilled-spirits/ds-labeling-home/ds-net-contents)
- [TTB — Wine Brand Label and Mandatory Information](https://www.ttb.gov/regulated-commodities/beverage-alcohol/wine/labeling-wine/wine-labeling-brand-label)
- [TTB — Malt Beverage Mandatory Label Information](https://www.ttb.gov/regulated-commodities/beverage-alcohol/beer/labeling/malt-beverage-mandatory-label-information)

The prescribed warning wording is implemented as a constant and compared for exact case and punctuation after whitespace normalization. Physical type-size requirements require real-world scale and remain a human measurement.

## Deployment

### Docker

```bash
docker build -t treasury-label-verifier .
docker run --rm -p 8080:8080 treasury-label-verifier
```

Verify `http://localhost:8080/healthz`, then run both sample workflows. The container includes Tesseract English data, runs as a non-root user, and listens on port 8080 unless `PORT` is supplied.

### Render free web service

1. In Render, create a **Web Service** from this GitHub repository.
2. Select branch `main`, language/runtime **Docker**, and instance type **Free**.
3. Set health check path `/healthz`. No environment variables or secrets are required.
4. Deploy, run both samples from a clean browser, and record the `onrender.com` URL.

The free instance can sleep after inactivity and take roughly a minute to wake. A paid always-on instance or another approved host is appropriate if first-request latency matters.

## How AI/Codex was used

Codex was used as an implementation partner to inspect the assignment, research TTB sources, implement and refactor the .NET application, create deterministic SVG fixtures, write tests/documentation, run quality checks, and exercise the UI. The deployed runtime does **not** call Codex, OpenAI, or another hosted AI API. Regulatory behavior remains deterministic C#, linked to authoritative sources, and presented for human confirmation.
