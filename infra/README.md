# ScamAlert Azure infrastructure (Phase 1)

Deploys staging/production foundations for **ScamAlert.Api** on Azure:

| Resource | Purpose |
|----------|---------|
| App Service (Linux, .NET 10) | API + Blazor portal |
| Azure SQL (`ScamAlert` DB) | Application data |
| Key Vault | SQL connection string, JWT signing key (references in App Service) |
| Application Insights + Log Analytics | Telemetry (via `APPLICATIONINSIGHTS_CONNECTION_STRING`) |
| Storage account + `installers` container | Windows installer blobs (Phase 3) |

**MVP scope:** user-mode protection only; no kernel driver in this stack.

## Prerequisites

- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az login`)
- Contributor access on a subscription
- .NET 10 SDK (to publish the API locally)

## First-time deploy (PowerShell)

```powershell
# 1. Create resource group
az group create --name rg-scamalert-staging --location eastus2

# 2. Copy and edit parameters (do not commit passwords)
Copy-Item infra/main.parameters.staging.example.json infra/main.parameters.staging.local.json
# Edit sqlAdminPassword in the local file

# 3. Deploy infrastructure
./infra/scripts/Deploy-Infrastructure.ps1 `
  -ResourceGroupName rg-scamalert-staging `
  -ParametersFile infra/main.parameters.staging.local.json

# 4. Set secrets in Key Vault (Portal or CLI) — see below

# 5. Publish the API
$webApp = az deployment group list -g rg-scamalert-staging --query "sort_by(@, &properties.timestamp)[-1].properties.outputs.webAppName.value" -o tsv
./infra/scripts/Publish-WebApp.ps1 -ResourceGroupName rg-scamalert-staging -WebAppName $webApp

# 6. Smoke test
curl "https://$webApp.azurewebsites.net/api/health"
```

After deploy, note Bicep **outputs**: `webAppUrl`, `keyVaultName`, `storageAccountName`.

## Key Vault secrets (required before go-live)

Bicep seeds:

| Secret name | Notes |
|-------------|--------|
| `ConnectionStrings--ScamAlertDb` | Set automatically from SQL |
| `Authentication--Jwt--SigningKey` | **Replace** placeholder with 32+ random characters |

Add manually for full functionality:

| Secret name (use `--` for nested config) | Maps to |
|------------------------------------------|---------|
| `Stripe--SecretKey` | `Stripe:SecretKey` |
| `Stripe--WebhookSecret` | `Stripe:WebhookSecret` |
| `Twilio--AccountSid` | `Twilio:AccountSid` |
| `Twilio--AuthToken` | `Twilio:AuthToken` |

Example:

```powershell
$kv = "<keyVaultName from output>"
az keyvault secret set --vault-name $kv --name "Authentication--Jwt--SigningKey" --value "<long-random-secret>"
```

Optional: add matching **App Service application settings** that reference Key Vault, or rely on settings already wired for SQL and JWT in `main.bicep`.

## GitHub Actions

### CI (`ci.yml`)

Runs `dotnet build` + `dotnet test` on push/PR to `main`.

### Deploy Staging (`deploy-staging.yml`)

Uses **OIDC** federated credentials (recommended).

1. Create an app registration / managed identity for GitHub Actions.
2. Grant **Contributor** on the resource group (or subscription for first RG create).
3. Configure [federated credential](https://learn.microsoft.com/entra/workload-id/workload-identity-federation-create-trust) for `repo:<owner>/<repo>:environment:staging`.
4. Add GitHub **environment** `staging` with secrets:

| Secret | Description |
|--------|-------------|
| `AZURE_CLIENT_ID` | App registration client ID |
| `AZURE_TENANT_ID` | Entra tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID |
| `SQL_ADMIN_LOGIN` | e.g. `scamalertadmin` |
| `SQL_ADMIN_PASSWORD` | Strong SQL password |
| `AZURE_WEBAPP_NAME` | App name from Bicep (after first infra deploy) |

**First run:** workflow_dispatch → enable **deploy_infrastructure** → leave `web_app_name` empty (filled from Bicep output).

**Later runs:** deploy_infrastructure = false, set `web_app_name` or `AZURE_WEBAPP_NAME` secret.

## App configuration

| Setting | Source |
|---------|--------|
| `ASPNETCORE_ENVIRONMENT` | `Staging` (staging) / `Production` (prod) |
| `ConnectionStrings__ScamAlertDb` | Key Vault reference |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | App Insights |
| `Web__PublicBaseUrl` | `https://<app>.azurewebsites.net` |
| `Twilio__WebhookPublicBaseUrl` | Same host (update when custom domain added) |

File: `src/ScamAlert.Api/appsettings.Staging.json` — bootstrap admin disabled, HTTPS metadata enforced.

## Custom domain (later)

Set Bicep parameter `customDomainHost` and bind certificate in App Service; update Stripe/Twilio webhook URLs.

## Troubleshooting

| Issue | Action |
|-------|--------|
| App fails to start, Key Vault errors | Wait 2–5 min after first deploy for RBAC propagation; restart App Service |
| `linuxFxVersion` not found | In Portal → Configuration, set stack to **.NET 10** when available, or retarget app to `DOTNETCORE|9.0` and align `TargetFramework` |
| EF migrations fail | Check SQL firewall (Azure services rule is deployed); verify connection secret |
| Health check fails | Hit `/api/health`; ensure site is up and DB reachable |

## Locked MVP decisions

See team notes: user-mode only (no kernel), App Service + Azure SQL, single Blazor host, pairing-based installer in Phase 3, ~6-week family beta.
