# ScamAlert go-live plan (locked decisions)

## Product scope (MVP / family beta)

- **Protection:** Broker + Tray + cloud SMS alerts — **no kernel block**
- **Hosting:** Azure App Service (Linux) + Azure SQL + Key Vault + Blob + App Insights
- **Web:** Marketing + portal in `ScamAlert.Api` (single deployable)
- **Desktop pairing:** One-time code from portal → installer writes config (Phase 3)
- **Email:** SendGrid (Phase 2) — welcome, password reset
- **Timeline:** ~6 weeks; beta = a few family members; small team

## Phases

| Phase | Status | Focus |
|-------|--------|--------|
| **1 — Azure foundation** | Code complete — deploy to your subscription | Bicep, CI/CD, staging deploy |
| **2 — Portal & onboarding** | Done (local) | Devices, contacts CRUD, email, keys, portal UI |
| **3 — Windows installer** | Planned | Broker + Tray MSI, pairing |
| **4 — Marketing & compliance** | Planned | Pricing, legal, SEO |
| **5 — Beta hardening** | Planned | Monitoring, runbooks |
| **6 — Kernel driver** | Deferred | Not in MVP |

## Phase 1 deliverables

- [x] `infra/main.bicep` — SQL, App Service, Key Vault, Storage, App Insights
- [x] `infra/scripts/` — PowerShell deploy helpers
- [x] `.github/workflows/ci.yml` — build + test
- [x] `.github/workflows/deploy-staging.yml` — optional infra + app deploy
- [x] Azure Monitor OpenTelemetry in `ScamAlert.ServiceDefaults`
- [x] `appsettings.Staging.json`

**Your next steps:** deploy RG (see `infra/README.md`), replace JWT secret in Key Vault, configure GitHub OIDC, run smoke test on `/api/health`.
