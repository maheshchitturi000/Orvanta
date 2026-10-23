# Deploying Orvanta (free tiers)

This guide puts Orvanta on the internet at no cost, or a few cents a month at most. After the one-time setup,
every push to `main` runs the tests and deploys automatically.

## What runs where

| Part | Azure service | Free allowance (per month) |
|---|---|---|
| Gateway + web app (public), Auth, Food, Jobs, AI, Conversation services | **Container Apps**, Consumption plan. Each app scales to zero when idle | 180,000 vCPU-seconds, 360,000 GiB-seconds and 2 million requests per subscription |
| Database | **Azure SQL Database free offer**, serverless | 100,000 vCore-seconds and 32 GB per database. When the allowance runs out, the database pauses until next month (it never bills you) |
| Job discovery and application workers | **Azure Functions**, Flex Consumption | 100,000 GB-seconds and 250,000 executions |
| Job queues and the Functions package | **Storage account** | Not free, but this workload costs a few cents |
| Container logs | **Log Analytics**, capped at 0.15 GB a day | First 5 GB free |
| Container images | **GitHub Container Registry** | Free |
| AI (chat, CV parsing, cover letters) | **Groq** free API (OpenAI-compatible). Google Gemini's free tier also works | Rate-limited free tier, no card needed |
| Email (sign-up codes) | Gmail SMTP with an app password, or Brevo's free plan | 500 emails a day (Gmail) / 300 a day (Brevo) |

**What to expect from free tiers:**
- **Cold starts.** Apps scale to zero, so the first request after a quiet period takes 10–30 seconds. After a
  long idle period the database also has to resume (about a minute). The services retry, so this is a slow
  first page rather than an error.
- **Monthly allowance.** The allowances cover a personal project or a demo with light daily use. Heavy traffic
  can use up the Container Apps grant, after which normal per-second pricing applies. Set a budget alert (step 7).
- **Azure free account.** Signing up needs a card for identity checks. You aren't charged unless you upgrade
  or go past the allowances above.

## 1. Install the tools

- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli): `winget install Microsoft.AzureCLI`
- An Azure account: https://azure.microsoft.com/free
- A Groq API key: https://console.groq.com/keys (free)

## 2. Create the Azure resource group and a deploy identity

GitHub Actions signs in to Azure with OpenID Connect, so no Azure password is stored in GitHub. Run in
PowerShell, replacing `<owner>/<repo>` with your GitHub repository (for example `maheshchitturi000/Orvanta`):

```powershell
az login
$repo = '<owner>/<repo>'
$rg = 'orvanta-prod'

# Pick a region that has Functions Flex Consumption, e.g. eastus, westeurope, southeastasia
az functionapp list-flexconsumption-locations -o table
$location = 'eastus'

foreach ($ns in 'Microsoft.App','Microsoft.OperationalInsights','Microsoft.Sql','Microsoft.Web','Microsoft.Storage') {
    az provider register --namespace $ns
}
az group create -n $rg -l $location

$sub = az account show --query id -o tsv
$tenant = az account show --query tenantId -o tsv
$appId = az ad app create --display-name orvanta-github-deploy --query appId -o tsv
az ad sp create --id $appId | Out-Null
az role assignment create --assignee $appId --role Contributor --scope "/subscriptions/$sub/resourceGroups/$rg" | Out-Null

@{ name = 'github-production'; issuer = 'https://token.actions.githubusercontent.com';
   subject = "repo:${repo}:environment:production"; audiences = @('api://AzureADTokenExchange') } |
    ConvertTo-Json | Set-Content fic.json
az ad app federated-credential create --id $appId --parameters fic.json
Remove-Item fic.json

"AZURE_CLIENT_ID=$appId"; "AZURE_TENANT_ID=$tenant"; "AZURE_SUBSCRIPTION_ID=$sub"
```

## 3. Generate the production keys

These are new keys, different from your local ones. Keep a copy in a password manager. Changing
`PASSWORD_PEPPER` later breaks every password, and changing `DATA_PROTECTION_KEY` or `JOBS_SECRETS_KEY` breaks
stored provider tokens and job-source keys.

```powershell
function New-Bytes([int]$n) { $b = New-Object byte[] $n; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); $b }
function New-Key([int]$n) { [Convert]::ToBase64String((New-Bytes $n)) }
function New-Hex([int]$n) { -join ((New-Bytes $n) | ForEach-Object { $_.ToString('x2') }) }

"SQL_ADMIN_PASSWORD=Ov1!$(New-Hex 24)"
"JWT_SIGNING_KEY=$(New-Key 64)"
"JWT_ENCRYPTION_KEY=$(New-Key 32)"
"PASSWORD_PEPPER=$(New-Key 32)"
"OTP_HMAC_KEY=$(New-Key 32)"
"DATA_PROTECTION_KEY=$(New-Key 32)"
"JOBS_SECRETS_KEY=$(New-Key 32)"
```

## 4. Create a registry pull token

Container Apps pulls the images from GitHub Container Registry. On GitHub go to **Settings → Developer settings
→ Personal access tokens → Tokens (classic) → Generate new token**. Tick only `read:packages`, choose an expiry,
and copy the token.

## 5. Set up email

Without SMTP, sign-up codes are only written to the logs, so real users can't register.
- **Gmail:** turn on 2-step verification, then create an app password at https://myaccount.google.com/apppasswords.
  Use host `smtp.gmail.com`, your Gmail address as the user name and the from address, and the app password.
- **Brevo:** create a free account, then under **SMTP & API** use host `smtp-relay.brevo.com` with the SMTP login and key.

## 6. Add everything to GitHub

In your repository go to **Settings → Secrets and variables → Actions**.

**Secrets:**

| Name | Value |
|---|---|
| `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | from step 2 |
| `SQL_ADMIN_PASSWORD`, `JWT_SIGNING_KEY`, `JWT_ENCRYPTION_KEY`, `PASSWORD_PEPPER`, `OTP_HMAC_KEY`, `DATA_PROTECTION_KEY`, `JOBS_SECRETS_KEY` | from step 3 |
| `GHCR_PULL_TOKEN` | from step 4 |
| `SMTP_PASSWORD` | from step 5 |
| `LLM_API_KEY` | your Groq key. Leave it unset to run without AI |
| `JSEARCH_API_KEY` | optional: RapidAPI JSearch key (LinkedIn/Indeed/Glassdoor listings) |
| `ADZUNA_APP_KEY` | optional: Adzuna API key |

**Variables:**

| Name | Value |
|---|---|
| `AZURE_RESOURCE_GROUP` | `orvanta-prod`. **Deployment stays off until this is set** |
| `SMTP_HOST`, `SMTP_USERNAME`, `SMTP_FROM_EMAIL` | from step 5 |
| `LLM_BASE_URL` | optional. Default is Groq, `https://api.groq.com/openai/v1/`. For Gemini use `https://generativelanguage.googleapis.com/v1beta/openai/` |
| `LLM_MODEL` | optional. Default is `llama-3.1-8b-instant`; check https://console.groq.com/docs/models for current free models |
| `ADZUNA_APP_ID` | optional |
| `PUBLIC_URL` | optional. Set it only after you add a custom domain (see below) |

## 7. Deploy

Push to `main`, or open **Actions → Deploy → Run workflow**. The run:
1. builds and tests the backend and the web app,
2. builds six images (the gateway image includes the Angular app) and pushes them to GHCR,
3. creates or updates the Azure resources from `infra/main.bicep`,
4. opens the SQL firewall for the runner, applies `Microservices/Database` (idempotent), and closes it again,
5. publishes the Jobs Azure Functions.

The run summary shows the app URL, `https://orvanta-gateway.<id>.<region>.azurecontainerapps.io`. Open it,
register, and check that the verification email arrives.

**Budget alert (recommended):** in the Azure portal go to **Cost Management → Budgets → Add**, set the amount
to 1 (in your currency), and add an alert at 100%, so you hear about any charge right away.

## 8. Point the Chrome extension at production

```powershell
node extension/build-production.mjs https://orvanta-gateway.<id>.<region>.azurecontainerapps.io
```

Load `dist/extension` unpacked from `chrome://extensions`. Publishing to the Chrome Web Store needs a one-time
5 USD developer registration; until then, share the unpacked folder.

## Custom domain (optional, free certificate)

```powershell
az containerapp hostname add -g orvanta-prod -n orvanta-gateway --hostname app.example.com
az containerapp hostname bind -g orvanta-prod -n orvanta-gateway --hostname app.example.com --environment orvanta-env --validation-method CNAME
```

Add the DNS records that the first command prints, then set the `PUBLIC_URL` variable to `https://app.example.com`
and deploy again. The CORS settings, OAuth callbacks and ONDC URLs then use the custom domain.

## Things that need a provider's approval

- **Swiggy:** Swiggy's `swiggy-mcp` client only accepts the redirect URI `http://localhost:5077/...`. Connecting
  Swiggy in production needs Swiggy to register `https://<your-domain>/api/integrations/swiggy/callback`. Until
  then, food ordering through Swiggy works only locally.
- **ONDC:** runs in Sandbox mode. Going live means registering as a buyer app with ONDC (subscriber ID and
  signing keys) and setting `Ondc__Mode` and the keys on the food app.

## Troubleshooting

| Symptom | Check |
|---|---|
| Deploy job skipped | The `AZURE_RESOURCE_GROUP` variable isn't set |
| `azure/login` fails | The federated credential subject must be exactly `repo:<owner>/<repo>:environment:production` |
| Container stuck on "Activating" / image pull errors | `GHCR_PULL_TOKEN` is missing, expired, or lacks `read:packages` |
| First request is slow, then fine | A cold start: apps scale to zero and the database pauses |
| Service logs | Portal → Container App → **Log stream**, or the `AppLogs` table in the database |
| No verification emails | `SMTP_*` settings; the auth app's log says which mode it started in |
