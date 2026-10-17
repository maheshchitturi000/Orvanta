# Orvanta Microservices

Backend for Orvanta AI (.NET 10). The Angular app in the repo root talks **only** to the Gateway.

```
Angular (4200) ──► Gateway :5095 (YARP, CORS, rate limits)
                     ├─ /api/auth/*          ──► AuthService          :5101  (SQL: OrvantaAuth)
                     ├─ /api/integrations/*  ──► FoodService          :5077  (SQL: OrvantaFood)
                     ├─ /api/food/*          ──► FoodService          :5077 ──► Swiggy MCP / Zomato MCP
                     ├─ /api/chat            ──► AIService            :5212 ──► Ollama (qwen3:4b)
                     └─ /api/conversations/* ──► ConversationService  :5005  (SQL: OrvantaDb)
```

## Folder layout

```
Microservices/
├─ Orvanta.slnx
├─ run-all.ps1                         # starts all services locally
├─ setup-secrets.ps1                   # one-time: creates local dev keys in user-secrets
├─ src/
│  ├─ BuildingBlocks/Orvanta.Shared    # ResponseCls, JWT validation, AES-GCM, exception middleware
│  ├─ Gateway/Orvanta.Gateway          # YARP reverse proxy (replaces the old gateway)
│  └─ Services/
│     ├─ Orvanta.AuthService           # register → email OTP → login, JWT, refresh tokens
│     ├─ Orvanta.FoodService           # Swiggy + Zomato connections (OAuth), top-5 search, COD ordering
│     ├─ Orvanta.AIService             # Ollama chat (existing)
│     ├─ Orvanta.ConversationService   # chat history, Dapper + SQL Server (existing)
│     ├─ Orvanta.VoiceService          # placeholder (existing template)
│     └─ Orvanta.IntegrationService    # legacy Swiggy-only service, superseded by FoodService (port 5078, not routed)
└─ tests/Orvanta.UnitTests
```

FoodService runs on **5077** because `http://localhost:5077/api/integrations/swiggy/callback` is the
redirect URI already working with Swiggy's `swiggy-mcp` client. The OAuth callback goes straight to
FoodService; every other call goes through the Gateway.

## Jobs: CV-matched auto-apply

```
Angular  ──► Gateway /api/jobs/* ──► JobsService :5081 ──► SQL (OrvantaJobs)
                                         │  approve / run discovery
                                         ▼
                              Azure Storage Queues (Azurite locally)
                                 job-discovery │ job-apply
                                         ▼
                     Orvanta.Jobs.Functions (Azure Functions, .NET isolated)
   Scheduler (hourly timer) → DiscoveryWorker → ApplyWorker → ApplyPoisonHandler
                                         ▼
          Chrome extension fills each prepared application in the user's browser → user submits
```

| Project | Role |
|---|---|
| `Orvanta.Jobs.Core` | Shared domain: EF model, career-board sources (Greenhouse, Lever, Ashby), CV parsing, `JobMatcher`, workflows, queue client |
| `Orvanta.JobsService` | REST API: CV upload, profile, preferences, matches, one-click approval, applications |
| `Orvanta.Jobs.Functions` | Background workflow. **Scheduler** runs hourly and starts each user's daily search at *their* local hour (any time zone), and re-queues stuck items. **DiscoveryWorker** fetches boards, filters, scores and stores matches of 80% or more. **ApplyWorker** writes the cover letter and form answers and marks the application ready. Messages that fail 5 times go to a poison queue and are marked Failed |
| `extension/` (repo root) | Chrome extension that fills approved applications; the user presses Submit |

**Match score (0–100)** comes from `JobMatcher` and is explainable: 55% skills (job skills found in the
CV, with implied skills, so SQL Server counts as SQL), 30% role (job title vs target roles, with
synonyms), and 15% seniority (required years vs CV). Guards: a different job function (manager, sales,
solutions…) or a core skill in the title missing from the CV keeps a job below 80.

**AI is optional.** With Ollama running (`Jobs:Llm`), CVs are parsed and cover letters written by the
local model. Without it, rule-based parsing and a template letter are used, so nothing breaks.

**Why the extension, not a server bot:** LinkedIn and most job sites forbid automated submission, and
server-side bots get user accounts restricted. Orvanta automates discovery, matching and preparation in
the cloud; the final Submit happens in the user's own browser.

Run locally: Azurite + `func start` in `src/Services/Jobs/Orvanta.Jobs.Functions` (Core Tools **4.x
recent**; the copy bundled with older Visual Studio installs is too old), plus `JobsService`.
`run-all.ps1` starts all of them.

## Common response contract

Every endpoint returns `ResponseCls<T>` (`Orvanta.Shared/Responses/ResponseCls.cs`):

```json
{ "statusCode": 200, "statusMessage": "Human readable message", "statusObject": { } }
```

- Validation errors: `400` with `statusObject` = `{ "field": ["message", ...] }`.
- Unhandled errors: `500` with a trace reference only (no stack traces leak).
- 401/403 from JWT validation also use the envelope.

Services return `ServiceResult<T>` from business logic; `ApiControllerBase.Respond(...)` maps it to
`ResponseCls<T>` and the matching HTTP status. That keeps the services loosely coupled from HTTP.

## Security model

| Concern | Implementation |
|---|---|
| Password storage | PBKDF2-HMAC-SHA256, 600k iterations, 16-byte random salt per user + server-side pepper; constant-time compare; automatic rehash when the policy is raised |
| Password policy | 8–128 chars, upper + lower + digit + symbol (validated on the client and the server) |
| Email verification | 6-digit OTP, HMAC-hashed at rest, 10-min expiry, max 5 attempts, 60 s resend cooldown, single active code |
| Brute force | Account lockout (5 failures → 15 min) + Gateway rate limit (10/min/IP on login, register, OTP) |
| Access token | JWT **signed** (HS256) **and encrypted** (JWE: A256KW + A256CBC-HS512), 15-min lifetime, kept in memory in the browser |
| Refresh token | 64 random bytes, SHA-256 hashed in DB, HttpOnly + Secure + SameSite=Strict cookie scoped to `/api/auth`, rotated on every use; reuse of an old token revokes the whole session family |
| Provider tokens | Swiggy/Zomato OAuth tokens encrypted at rest with AES-256-GCM |
| Provider OAuth | OAuth 2.1 + PKCE (S256), one-time `state` stored hashed, RFC 8414 discovery, RFC 7591 dynamic client registration |
| Account enumeration | Generic messages for unknown emails on login and resend; constant-time dummy hash |
| Authorization | `[Authorize(Policy = "UserOnly")]` on every food and integration endpoint; role claim supports `Admin` |

### Secrets

No keys are committed. Locally they live in `dotnet user-secrets` (your Windows profile). On a new
machine, run `setup-secrets.ps1` once. For servers, use environment variables or a vault. To generate a key by hand:

```powershell
# Cryptographically random key as base64: 32 bytes (use 64 for Jwt:SigningKey)
$b = New-Object byte[] 32; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
```

| Setting | Services | Notes |
|---|---|---|
| `Jwt:SigningKey` (≥32 bytes) / `Jwt:EncryptionKey` (32 bytes) | Auth, Food | Must be identical in both |
| `AuthSecurity:PasswordPepper`, `AuthSecurity:OtpHmacKey` | Auth | Changing the pepper invalidates all passwords |
| `DataProtection:Key` (32 bytes) | Food | Changing it invalidates stored provider tokens |
| `Smtp:*` | Auth | When `Smtp:Host` is empty, OTP emails are written to the AuthService console instead |

Environment variable form: `Jwt__SigningKey`, `AuthSecurity__PasswordPepper`, and so on.

## Running locally

Prerequisites: .NET 10 SDK, SQL Server (dev config uses `localhost\SQLEXPRESS` with Windows auth).
Databases are created automatically on first start in Development.

```powershell
powershell -ExecutionPolicy Bypass -File .\Microservices\setup-secrets.ps1   # first time only
powershell -ExecutionPolicy Bypass -File .\Microservices\run-all.ps1   # backend
npm start                                                              # frontend → http://localhost:4200
dotnet test .\Microservices\Orvanta.slnx                               # backend tests
```

With SMTP not configured, find the OTP in the **AuthService** window (`[DEV EMAIL] ... Subject: 123456 is your ...`).

## Food flow

1. **Connect**: Integrations page → `POST /api/integrations/{provider}/connect` → provider consent screen →
   `GET /api/integrations/{provider}/callback` → tokens stored encrypted → back to `/integrations`.
2. **Ask in chat**: `POST /api/food/assist { prompt }`, for example "order veg biryani from swiggy".
   - `FoodIntentParser` extracts the dish, veg filter, preferred provider and "top one" auto-pick.
   - All connected providers are searched **in parallel**; one failing provider doesn't block the others.
   - Results are ranked (rating × popularity, name match, bestseller, in-stock, no customisation needed) and the **top 5** are returned.
3. **Preview**: `POST /api/food/orders/preview` builds the provider cart and returns the bill, after confirming cash is an available payment method.
4. **Confirm**: `POST /api/food/orders/confirm { previewId }` places the order as **Cash on Delivery**.
   Preview IDs are single-use, so a double-click can't place two orders.

### Adding a provider (for example travel later)

Implement `IFoodProvider` (or a new `ITravelProvider` contract), register it in `FoodService/Program.cs`,
and add an entry under `FoodProviders:Providers` in `appsettings.json`. OAuth, token refresh, encrypted
storage and the MCP transport are shared.

## Provider notes (please read)

- **Swiggy** ([docs](https://mcp.swiggy.com/builders/docs/)): production access is allow-list only.
  `http://localhost` redirect URIs work for development. To allow-list
  a production HTTPS callback URL, email builders@swiggy.in. Local development uses the existing
  `http://localhost:5077/api/integrations/swiggy/callback`.
  Tools used: `get_addresses`, `search_menu`, `flush_food_cart`, `update_food_cart`, `get_food_cart`,
  `get_payment_options`, `place_food_order` with `paymentMethod: "Cash"`. Swiggy orders **cannot be
  cancelled through the API**, which is why the UI always shows the bill and asks for confirmation.
  Items that need size or add-on choices are ranked lower and can't be auto-ordered.
- **Zomato** (`https://mcp-server.zomato.com/mcp`): Zomato hasn't published tool schemas, and redirect
  URIs need allow-listing by Zomato. Tool names come from its public listing; argument names in
  `ZomatoFoodProvider` are best effort, so verify them against the live `tools/list` once you have access.
  Zomato's documented payment types are `upi_qr` and `pay_later`; **cash on delivery isn't confirmed**,
  so `FoodProviders:Providers:zomato:CashPaymentMethod` is empty and Zomato orders are refused with a
  clear message. Search results from Zomato still appear in the top 5.
