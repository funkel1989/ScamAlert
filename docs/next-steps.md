# Next steps (MVP → family beta)

**MVP application code in this repo is largely complete.** What remains is local validation, real third-party integrations, and Azure deployment (intentionally **last**).

Primary references: [go-live-plan.md](go-live-plan.md), [production-prep.md](production-prep.md), [infra/README.md](../infra/README.md), [installer/README.md](../installer/README.md).

---

## What is already built (in code)

| Area | Status |
|------|--------|
| Marketing site + $7.99 single-tier signup | Done |
| Portal (contacts, devices, alerts, billing, password reset) | Done |
| Device pairing API + portal **Pair PC** | Done |
| Desktop MSI (Broker Windows service + Tray) | Done |
| **Pair this PC** wizard (`ScamAlert.Configurator`) | Done |
| Bake API URL into MSI at build time (`-ApiBaseUrl`) | Done |
| Infra Bicep + CI + installer build script | Done (not deployed) |
| Kernel driver | Deferred — not MVP |

---

## Step 1 — Repo hygiene

- [ ] Commit any open work (Configurator, NotFound fix, doc updates)
- [ ] `dotnet test ScamAlert.sln -c Release` — expect **112** passing

---

## Step 2 — Local testing (no Azure)

Prove the product loop on **one Windows PC** before paying for cloud or live Stripe/Twilio.

### API + database

```powershell
dotnet run --project src\ScamAlert.Api\ScamAlert.Api.csproj
# http://localhost:5000 — home, /signup, /login, /devices
curl http://localhost:5000/api/health
```

Expect: `"status":"ok"`, `"database":"ok"`.

### Desktop stack (dev)

Three processes:

```powershell
# Terminal 1
dotnet run --project src\ScamAlert.Broker\ScamAlert.Broker.csproj

# Terminal 2
dotnet run --project src\ScamAlert.Tray\ScamAlert.Tray.csproj

# Terminal 3 — API (if not already running)
dotnet run --project src\ScamAlert.Api\ScamAlert.Api.csproj
```

### MSI + pairing (closer to real beta)

```powershell
.\scripts\build-desktop-installer.ps1
# Optional when API URL is known:
# .\scripts\build-desktop-installer.ps1 -ApiBaseUrl "https://your-app.azurewebsites.net"
```

On the protected PC:

1. Run `installer\ScamAlert.DesktopInstaller\bin\Release\ScamAlert-Desktop.msi` (elevated).
2. **Pair this PC** wizard opens (or Start Menu → ScamAlert → Pair this PC).
3. Portal → `/devices` → **Pair PC** → enter code in wizard.
4. Confirm `%ProgramData%\ScamAlert\broker.appsettings.json` exists.
5. `sc query ScamAlertBroker` — service running.
6. Sign out/in so Tray starts (HKLM Run key).

Fallback: `scripts\configure-broker-from-pairing-code.ps1` instead of the wizard.

### End-to-end alert

1. Portal: signup (or login), contacts, device paired.
2. Enable cloud on broker (`CloudAlerts:Enabled` via pairing config).
3. Simulator (with Broker + Tray running):

```powershell
dotnet run --project tools\ScamAlert.DriverSimulator\ScamAlert.DriverSimulator.csproj -- --ip 203.0.113.10 --port 3389
```

4. Portal `/alerts` shows the attempt; SMS only if Twilio is configured (Step 3).

- [ ] MSI install + pairing wizard succeeds
- [ ] Broker service + Tray run after logon
- [ ] Alert appears in portal
- [ ] (Optional) SMS received when Twilio is wired

---

## Step 3 — Integration testing (Stripe, email, SMS)

Can start **locally** with test keys; webhooks often need a **public URL** (ngrok) before Azure.

| Integration | Tasks |
|-------------|--------|
| **Stripe** | Create $7.99/month price; set `Billing:Tiers:0:StripePriceId`; `Stripe:SecretKey`, `Stripe:WebhookSecret`; disable `SkipPaymentForDevelopment` outside Development |
| **SendGrid** | `Email:SendGridApiKey`, `Email:FromAddress` — welcome + password reset (not console-only) |
| **Twilio** | Account, from number, `Twilio:WebhookPublicBaseUrl`; status + inbound SMS webhooks |

- [ ] Signup → checkout (or skip-payment dev path) → subscription active
- [ ] Password reset email delivers
- [ ] Test alert sends SMS; `ACK` reply acknowledges

---

## Step 4 — Config, legal, and installer polish (pre–public beta)

- [ ] `Web:SupportEmail`, `Web:LegalEntityName` in appsettings / App Service settings
- [ ] Attorney review: `/privacy`, `/terms`, `/cookies` (MVP templates today)
- [ ] Production MSI with baked URL: `.\scripts\build-desktop-installer.ps1 -ApiBaseUrl "https://…"`
- [ ] (Optional) **Code-sign MSI** — fewer SmartScreen warnings

---

## Step 5 — Azure deploy (family beta — last)

See [infra/README.md](../infra/README.md). Rough order:

1. Deploy resource group (Bicep) — budget ~$20/mo when ready
2. Key Vault: JWT signing key, Stripe, SendGrid, Twilio, SQL connection
3. GitHub OIDC or manual publish → App Service
4. Upload MSI to storage `installers` container
5. Set `Web__PublicBaseUrl`, `Web__InstallerDownloadUrl` (blob URL)
6. Smoke: `curl https://<app>/api/health`
7. Stripe webhook → `https://<app>/api/webhooks/stripe`
8. Twilio webhooks → public base URL

- [ ] Health check OK in Azure
- [ ] Signup + pay on production URL
- [ ] Download MSI from portal link
- [ ] Pair PC against production API

---

## Step 6 — Family beta

- [ ] 2–3 family members: install → pair → real or simulated alert
- [ ] Collect feedback (installer UX, SMS wording, false positives)
- [ ] Fix bugs found in testing (expect patch commits, not new phases)

---

## Explicitly out of scope (MVP)

- Kernel / WFP driver blocking
- Twilio voice workflow
- Email verification on signup
- Separate marketing site project
- Full commercial marketing polish (screenshots, social proof)

---

## Quick command reference

```powershell
dotnet test ScamAlert.sln -c Release
dotnet build ScamAlert.sln -c Release
.\scripts\build-desktop-installer.ps1
dotnet run --project src\ScamAlert.Api\ScamAlert.Api.csproj
```
