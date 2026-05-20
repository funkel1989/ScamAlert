# Production prep (before Azure beta deploy)

Deploy to Azure is the **last step** for the family beta. Complete the items below on `main` first, then follow [infra/README.md](../infra/README.md).

## Phase 3 — Desktop install path (in progress)

| Item | Status |
|------|--------|
| Portal **Pair PC** one-time codes | Done |
| `POST /api/setup/redeem` + rate limit | Done |
| `scripts/configure-broker-from-pairing-code.ps1` | Done |
| Broker reads `%ProgramData%\ScamAlert\broker.appsettings.json` | Done |
| MSI (Broker service + Tray) | Done — `scripts/build-desktop-installer.ps1` |
| Code-signed MSI | Planned (beta can use unsigned) |
| Installer pairing UI | Planned (use pairing script after MSI) |

## Phase 5 — Hardening (in progress)

| Item | Status |
|------|--------|
| `/api/health` includes database check | Done |
| Production startup config validator (logs errors) | Done |
| `appsettings.Production.json` baseline | Done |
| Operational runbook | This doc + [go-live-plan.md](go-live-plan.md) |

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

Local testing without Stripe: keep `Stripe__SkipPaymentForDevelopment=true` in Development only.

## Smoke tests (local, before deploy)

```powershell
dotnet test ScamAlert.sln -c Release
dotnet run --project src/ScamAlert.Api/ScamAlert.Api.csproj
curl http://localhost:5000/api/health
```

Pairing flow:

1. Sign up / log in → `/devices` → **Pair PC**.
2. On the Windows machine (elevated PowerShell):

```powershell
.\scripts\configure-broker-from-pairing-code.ps1 -ApiBaseUrl "http://localhost:5000" -PairingCode "XXXXXXXX"
dotnet run --project src\ScamAlert.Broker\ScamAlert.Broker.csproj
dotnet run --project src\ScamAlert.Tray\ScamAlert.Tray.csproj
```

3. Trigger a simulator event; confirm alert appears under `/alerts`.

## Deploy (beta — last)

1. `infra/scripts/Deploy-Infrastructure.ps1`
2. GitHub OIDC + `deploy-staging.yml` or manual publish
3. `curl https://<app>/api/health` — expect `"database":"ok"`
4. Stripe webhook URL → `https://<app>/api/webhooks/stripe`
5. Twilio webhooks → public base URL

## Legal

Privacy, Terms, and Cookies pages are **MVP templates** — attorney review before public launch.
