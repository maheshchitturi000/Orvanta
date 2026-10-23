# Orvanta Chrome extension: Job Apply Assistant

Fills the job applications you approved in Orvanta, in **your own browser session**, and records each
one as applied. By default you review each form and press Submit yourself, which keeps your LinkedIn and
other accounts within each site's rules. **Auto-submit** (below) can press Easy Apply / Next / Submit for you.

## Start from the Orvanta app

On **Jobs → Applications**, **Apply now** asks how to apply (auto-submit or review yourself) and hands the
approved jobs to this extension. If the extension is missing or switched off, the dialog shows how to turn it
on and waits; as soon as it's enabled, applying starts by itself (no page refresh needed).

**Production:** `node extension/build-production.mjs https://<your-orvanta-host>` writes `dist/extension` with every
localhost URL replaced by your deployed address (see `docs/DEPLOYMENT.md`).

How the app and the extension talk: `bridge.js` runs on the Orvanta site (`http://localhost:4200`) and answers
`window.postMessage` pings / start requests from that page only. No tokens pass through it. When the extension
starts, the service worker also injects the bridge into Orvanta tabs that are already open. If Chrome hasn't
allowed Orvanta on all https sites yet, the extension opens `start.html` to ask (permission prompts need a
click inside the extension).

## Auto-submit (opt-in)

Off by default. To turn it on: right-click the Orvanta icon → **Options** → tick **Submit approved applications
automatically**, tick the acknowledgement, set a **daily limit** (default 15), **Save**. Then click **Start applying**.

- Clicks **Easy Apply → Next → Submit** (LinkedIn) or the form's Submit button (Greenhouse, Lever, Ashby,
  company sites), with short human-like pauses, and about 30 seconds between jobs.
- **Pauses and asks you** for a required question it has no answer for, any consent checkbox, a LinkedIn job that
  applies on the company's own website, or a page whose buttons it can't find. Then **Resume auto** or **Skip**.
- **Job description pages** (company careers sites, Glassdoor, LinkedIn "Apply on company website"): when the
  page has no form, Orvanta clicks **Apply** and follows it to the form (up to 3 hops per job; if Apply opens a new
  tab, the run follows it there).
- **Sign-in walls** (Workday and similar): Orvanta stops and asks you to sign in; it never types passwords.
- **Sites Orvanta isn't allowed on:** the icon shows a red **!**; open the popup and click **Allow & continue**.
- Presses **Submit at most once** per application. If no confirmation appears afterwards (different wording,
  an error, a CAPTCHA), it stops and asks you instead of pressing again, so a job is never applied to twice.
- Stops for the day at the daily limit.
- LinkedIn, Naukri and other job sites prohibit automated activity in their terms and may restrict accounts
  that use it. Keep the limit low and review your approved list before starting.

## How the agent works through a site

Sites put different steps between a job and its form. On every page the agent first works out which step
it is looking at, does what it safely can, and hands the rest to you with an **Action required** banner
(red, also shown in the toolbar popup with a red `!` badge). It then **continues by itself** once the page
moves past that step; **Continue** does the same at once.

| Step | What Orvanta does |
|---|---|
| Apply button, any wording ("Apply to this job", "Quick apply", "Start application"…) | Auto mode presses it (dialogs and new tabs included); normal mode asks you to |
| Email-first sign in / sign up (email, then a code) | Fills your email; auto mode presses Continue once |
| One-time code (OTP) boxes | Action required: enter the code sent to your email or phone |
| Sign up with a password | Fills name, email, phone. Action required: choose a password, accept the terms if you agree |
| Sign in with a password | Action required: sign in (or "Continue with Google / LinkedIn") |
| "Check your email" page | Action required: click the link in the email |
| CAPTCHA that still needs solving (invisible ones are ignored) | Action required: solve it |
| Application form | Fills it and attaches your CV. Auto mode presses Next / Submit; required questions or consent boxes → Action required |
| Anything else (e.g. a dashboard after signing in) | Goes back to the job by itself after a sign-in (at most twice); otherwise Action required with **Back to the job** |
| Confirmation ("Thank you for applying", "Successfully applied") | Marks the job submitted and opens the next |

Orvanta never types passwords, never solves CAPTCHAs, never ticks consent boxes, and presses a final Submit at
most once per job. Submissions you press yourself don't count against the auto-submit daily limit.

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
| LinkedIn job pages (Easy Apply) | ✓ | fills each step as it appears; you press Next / Submit |
| Any other company site | ✓ after you allow "all https sites" when starting | ✓ (label-based) |

When you click **Start applying**, Chrome asks once whether Orvanta may read and change data on https
sites. This is needed because jobs found via LinkedIn, Indeed or Glassdoor often redirect to the company's
own careers site. If you decline, the sites listed above still work.

## How it talks to Orvanta

- It uses the Orvanta sign-in cookie to get a short-lived access token from `/api/auth/refresh`.
  Tokens are kept in memory and never exposed to web pages.
- Endpoints: `GET /api/jobs/applications?status=ReadyToSubmit`, `GET /api/jobs/profile/cv`,
  `POST /api/jobs/applications/{id}/submitted`.
- When Orvanta is deployed, change the API URL in the extension **Options** and add the host to
  `host_permissions` in `manifest.json`.
