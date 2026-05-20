# Production prep (before Azure beta deploy)

Deploy to Azure is the **last step** for the family beta.

**Canonical checklist:** [next-steps.md](next-steps.md) — local testing, integrations, deploy, and beta.

---

## Code status (MVP)

Application features for the locked MVP are **implemented in the repo**. Remaining work is validation, third-party accounts, configuration, and hosting — not a large new coding phase.

| Phase | Status |
|-------|--------|
| Portal & onboarding | Done |
| Marketing & $7.99 billing | Done |
| Pairing + desktop MSI + Configurator wizard | Done |
| Azure foundation (Bicep, CI) | Code complete — not deployed |
| Beta hardening (health, config validator) | Done |

---

## Phase 3 — Desktop install path

| Item | Status |
|------|--------|
| Portal **Pair PC** one-time codes | Done |
| `POST /api/setup/redeem` + rate limit | Done |
| `scripts/configure-broker-from-pairing-code.ps1` | Done |
| Broker reads `%ProgramData%\ScamAlert\broker.appsettings.json` | Done |
| MSI (Broker service + Tray) | Done — `scripts/build-desktop-installer.ps1` |
| Installer pairing UI (`ScamAlert.Configurator`) | Done |
| Bake `ApiBaseUrl` at MSI build (`-ApiBaseUrl`) | Done |
| Code-signed MSI | Optional before wider distribution |

---

## Phase 5 — Hardening

| Item | Status |
|------|--------|
| `/api/health` includes database check | Done |
| Production startup config validator (logs errors) | Done |
| `appsettings.Production.json` baseline | Done |
| Operational runbook | [next-steps.md](next-steps.md) + this doc |

---

## Configuration checklist (production / staging)

Set via App Service settings or Key Vault references:

- `ConnectionStrings__ScamAlertDb` — Azure SQL
- `Authentication__Jwt__SigningKey` — random, ≥ 32 bytes (not a placeholder)
- `Authentication__BootstrapAdmin__Enabled` — `false`
- `Web__PublicBaseUrl` — `https://…` public site URL
- `Web__InstallerDownloadUrl` — real installer blob URL when MSI exists
- `Web__SupportEmail`, `Web__LegalEntityName` — your legal entity
- `Stripe__SecretKey`, `Stripe__WebhookSecret`, `Billing__Tiers__0__StripePriceId` — live $7.99/month price
- `Email__SendGridApiKey`, `Email__FromAddress`
- `Twilio__*` — SMS for alerts
- `APPLICATIONINSIGHTS_CONNECTION_STRING` — optional until deploy

Local testing without Stripe: keep `Stripe__SkipPaymentForDevelopment=true` in **Development** only.

---

## Smoke tests (local, before deploy)

See [next-steps.md](next-steps.md) for the full walkthrough. Quick commands:

```powershell
dotnet test ScamAlert.sln -c Release
dotnet run --project src/ScamAlert.Api/ScamAlert.Api.csproj
curl http://localhost:5000/api/health
```

---

## Deploy (beta — last)

1. `infra/scripts/Deploy-Infrastructure.ps1`
2. GitHub OIDC + `deploy-staging.yml` or manual publish
3. `curl https://<app>/api/health` — expect `"database":"ok"`
4. Upload MSI to `installers` blob; set `Web__InstallerDownloadUrl`
5. Stripe webhook → `https://<app>/api/webhooks/stripe`
6. Twilio webhooks → public base URL

---

## Legal

Privacy, Terms, and Cookies pages are **MVP templates** — attorney review before public launch.
