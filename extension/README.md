# Orvanta Chrome extension: Job Apply Assistant

Fills the job applications you approved in Orvanta, in **your own browser session**, and records each
one as applied. It never presses Submit for you: you review the form and submit it yourself. That keeps
your LinkedIn and other accounts within each site's rules.

## Install (developer mode)

1. Open `chrome://extensions` and turn on **Developer mode** (top right).
2. Click **Load unpacked** and choose this `extension` folder.
3. Sign in to Orvanta at http://localhost:4200 in the same browser.

## Use

- **Start applying:** click the Orvanta icon. It shows how many approved applications are ready.
  Click **Start applying**. Each job opens in turn with your details, CV and cover letter filled in.
  Answer anything left, press the site's **Submit**, and the extension detects the confirmation and
  opens the next job. **I submitted it** and **Skip this job** are on the banner if detection misses.
- **Fill this page:** on any application form (for example a LinkedIn Easy Apply step), click
  **Fill this page** to fill the fields Orvanta knows.

## Supported forms

| Site | Auto-open from queue | Fill |
|---|---|---|
| Greenhouse (`job-boards.greenhouse.io`, `boards.greenhouse.io`) | ✓ | ✓ |
| Lever (`jobs.lever.co`) | ✓ | ✓ |
| Ashby (`jobs.ashbyhq.com`) | ✓ | ✓ |
| LinkedIn Easy Apply | via **Fill this page** | best effort, step by step |

## How it talks to Orvanta

- It uses the Orvanta sign-in cookie to get a short-lived access token from `/api/auth/refresh`.
  Tokens are kept in memory and never exposed to web pages.
- Endpoints: `GET /api/jobs/applications?status=ReadyToSubmit`, `GET /api/jobs/profile/cv`,
  `POST /api/jobs/applications/{id}/submitted`.
- When Orvanta is deployed, change the API URL in the extension **Options** and add the host to
  `host_permissions` in `manifest.json`.
