# Orvanta AI

Orvanta is a personal AI assistant that does three jobs from one sign-in:

- **Finds and applies to jobs for you.** Upload your CV once. Every day Orvanta searches company career boards
  (Greenhouse, Lever, Ashby) and job aggregators (JSearch: LinkedIn, Indeed, Glassdoor, Foundit; and Adzuna),
  scores each job against your CV, and shows only the ones that match 80% or more, with the reasons. Approve the
  ones you like and Orvanta writes the cover letter and form answers. The Orvanta Chrome extension then fills the
  application in your own browser. You press Submit, or switch on auto-submit.
- **Orders food.** Connect Swiggy (or Zomato) once, describe what you want ("something spicy under ₹300, veg"),
  get the top 5 dishes, and order with cash on delivery. ONDC, India's open commerce network, is supported as a
  third source (sandbox today).
- **Answers questions.** A chat assistant with saved conversation history, private to each user.

## How it works

```
Browser ─► Gateway (YARP: one public URL, serves the Angular app + /api, rate limits)
             ├─ /api/auth/*           AuthService          sign-up, email OTP, login, sessions
             ├─ /api/integrations, /api/food, /api/ondc
             │                        FoodService          Swiggy / Zomato (OAuth + MCP), ONDC buyer app
             ├─ /api/jobs/*           JobsService          CV, preferences, matches, applications
             ├─ /api/chat             AIService            LLM chat (Ollama locally, Groq/Gemini in the cloud)
             └─ /api/conversations/*  ConversationService  chat history
                                         │
                      SQL Server (one database: Orvanta) · Azure Storage queues
                                         │
                      Jobs Functions (Azure Functions): hourly discovery, application preparation
Chrome extension ─► fills approved applications on LinkedIn, Greenhouse, Lever, Ashby and company sites
```

| Folder | What's in it |
|---|---|
| `src/` | Angular 21 web app (dashboard, AI assistant, jobs, integrations) |
| `Microservices/` | .NET 10 backend: gateway, services, shared building blocks, database scripts, tests. See [Microservices/README.md](Microservices/README.md) |
| `extension/` | Chrome extension (Manifest V3) that fills job applications. See [extension/README.md](extension/README.md) |
| `infra/` | Azure infrastructure as code (Bicep) |
| `.github/workflows/` | CI (build + tests) and deployment to Azure |

**Security:** passwords are hashed with PBKDF2 (600k iterations) plus a pepper. Email is verified by OTP.
Access tokens are short-lived, signed and encrypted JWTs. Refresh tokens live in HttpOnly cookies and rotate
on every use. Provider tokens are encrypted at rest (AES-256-GCM). Every endpoint that touches user data
requires a signed-in user, and each user sees only their own data. Details are in
[Microservices/README.md](Microservices/README.md#security-model).

## Run it locally

Requirements: .NET 10 SDK, Node 22, SQL Server Express, `sqlcmd`, Azurite and Azure Functions Core Tools 4.x.
For local AI, install [Ollama](https://ollama.com) with `ollama pull qwen3:4b`. AI is optional: without it
Orvanta uses rule-based CV parsing and template cover letters.

```powershell
# once: create local keys (stored in dotnet user-secrets, never in the repo)
powershell -ExecutionPolicy Bypass -File .\Microservices\setup-secrets.ps1

# every time: database schema + all services + Azurite + Functions, each in its own window
powershell -ExecutionPolicy Bypass -File .\Microservices\run-all.ps1

# web app
npm install
npm start          # http://localhost:4200
```

Tests: `dotnet test Microservices/tests/Orvanta.UnitTests` and `npx ng test --watch=false`.

The Chrome extension: open `chrome://extensions`, turn on Developer mode, then **Load unpacked** →
`extension/`.

## Deploy to production (free tiers)

Orvanta deploys to Azure using free tiers: Container Apps free grant, the Azure SQL free offer, Functions Flex
Consumption free grant, and Groq's free LLM API. Every push to `main` builds, tests and deploys through GitHub
Actions. Step-by-step setup: **[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)**.
