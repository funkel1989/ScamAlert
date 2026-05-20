# ScamAlert go-live plan (locked decisions)

## Product scope (MVP / family beta)

- **Protection:** Broker + Tray + cloud SMS alerts — **no kernel block**
- **Hosting:** Azure App Service (Linux) + Azure SQL + Key Vault + Blob + App Insights
- **Web:** Marketing + portal in `ScamAlert.Api` (single deployable)
- **Desktop pairing:** One-time code from portal → script/installer writes `%ProgramData%\ScamAlert\broker.appsettings.json`
- **Email:** SendGrid — welcome, password reset
- **Timeline:** ~6 weeks; beta = a few family members; small team
- **Pricing:** One plan — **$7.99/month** (plan code `pro`, “Family protection”)

## Phases

| Phase | Status | Focus |
|-------|--------|--------|
| **1 — Azure foundation** | Code complete — **deploy last (beta)** | Bicep, CI/CD, staging deploy |
| **2 — Portal & onboarding** | Done (local) | Devices, contacts CRUD, email, keys, portal UI |
| **3 — Windows installer** | In progress | Pairing codes + broker config done; MSI next |
| **4 — Marketing & compliance** | Done (local) | Pricing, legal, SEO, signup consents |
| **5 — Beta hardening** | In progress | Health checks, production validator, runbooks |
| **6 — Kernel driver** | Deferred | Not in MVP |

**Current focus:** Finish Phase 3 (MSI) and Phase 5 on `main`, then Phase 1 deploy for beta.

See **[production-prep.md](production-prep.md)** for the pre-deploy checklist.

## Phase 1 deliverables

- [x] `infra/main.bicep` — SQL, App Service, Key Vault, Storage, App Insights
- [x] `infra/scripts/` — PowerShell deploy helpers
- [x] `.github/workflows/ci.yml` — build + test
- [x] `.github/workflows/deploy-staging.yml` — optional infra + app deploy
- [x] Azure Monitor OpenTelemetry in `ScamAlert.ServiceDefaults`
- [x] `appsettings.Staging.json`

**Deploy when ready for beta:** see `infra/README.md`, replace JWT secret in Key Vault, configure GitHub OIDC, smoke test `/api/health`.

## Phase 3 deliverables (partial)

- [x] Device pairing codes (portal + `POST /api/setup/redeem`)
- [x] `scripts/configure-broker-from-pairing-code.ps1`
- [x] Broker loads `ProgramData\ScamAlert\broker.appsettings.json`
- [x] MSI packaging Broker (Windows service) + Tray (`installer/`, `scripts/build-desktop-installer.ps1`)
- [ ] Code-signed MSI for production SmartScreen trust
- [ ] Installer pairing UI (calls redeem, writes config — script works today)
