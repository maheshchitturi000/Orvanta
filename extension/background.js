// Orvanta extension service worker.
// Owns the session (via the Orvanta refresh cookie), the apply queue, and all API calls.
// The content script never sees tokens; it only receives the answers for its own tab.

const DEFAULT_API = 'http://localhost:5095/api';

let accessToken = null;
let tokenExpiresAt = 0;

async function apiBase() {
  const { apiBase } = await chrome.storage.sync.get('apiBase');
  return (apiBase || DEFAULT_API).replace(/\/$/, '');
}

/** Uses the HttpOnly refresh cookie set when the user signed in to Orvanta. */
async function getToken() {
  if (accessToken && Date.now() < tokenExpiresAt - 60_000) return accessToken;

  const res = await fetch(`${await apiBase()}/auth/refresh`, { method: 'POST', credentials: 'include' });
  const body = await res.json().catch(() => null);
  if (!res.ok || !body?.statusObject?.accessToken) {
    accessToken = null;
    throw new Error('Sign in to Orvanta first, then try again.');
  }

  accessToken = body.statusObject.accessToken;
  tokenExpiresAt = Date.now() + body.statusObject.expiresInSeconds * 1000;
  return accessToken;
}

async function api(path, options = {}) {
  const token = await getToken();
  const res = await fetch(`${await apiBase()}${path}`, {
    ...options,
    headers: { Authorization: `Bearer ${token}`, ...(options.body ? { 'Content-Type': 'application/json' } : {}), ...options.headers }
  });
  if (res.status === 401) {
    accessToken = null;
    throw new Error('Your Orvanta session expired. Sign in again.');
  }
  return res;
}

async function readyApplications() {
  const res = await api('/jobs/applications?status=ReadyToSubmit');
  const body = await res.json();
  if (!res.ok) throw new Error(body?.statusMessage || 'Could not load applications.');
  return body.statusObject || [];
}

async function cvAsDataUrl() {
  const res = await api('/jobs/profile/cv');
  if (!res.ok) return null;
  const blob = await res.blob();
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(res.headers.get('Content-Disposition') || '')?.[1] || 'cv.pdf';
  const buffer = await blob.arrayBuffer();
  let binary = '';
  new Uint8Array(buffer).forEach(b => (binary += String.fromCharCode(b)));
  return { name: decodeURIComponent(name), type: blob.type || 'application/pdf', base64: btoa(binary) };
}

/** Same keys as the server-side ApplicationPreparer, built from the profile for forms outside the queue. */
async function genericAnswers() {
  const [profile, prefs] = await Promise.all([
    api('/jobs/profile').then(r => r.json()).then(b => b.statusObject || {}),
    api('/jobs/preferences').then(r => r.json()).then(b => b.statusObject || {})
  ]);
  const [firstName, ...rest] = (profile.fullName || '').split(' ');
  return {
    firstName,
    lastName: rest.join(' '),
    fullName: profile.fullName || '',
    email: profile.email || '',
    phone: profile.phone || '',
    location: profile.location || '',
    currentTitle: profile.currentTitle || '',
    yearsOfExperience: String(profile.totalYearsExperience ?? ''),
    linkedInUrl: profile.linkedInUrl || '',
    portfolioUrl: profile.portfolioUrl || '',
    noticePeriodDays: String(prefs.noticePeriodDays ?? ''),
    workAuthorization: prefs.workAuthorization || '',
    needsVisaSponsorship: prefs.needsVisaSponsorship ? 'Yes' : 'No',
    willingToRelocate: prefs.willingToRelocate ? 'Yes' : 'No',
    expectedSalary: prefs.expectedSalary || ''
  };
}

// ---------- Queue (persisted in session storage so it survives worker restarts) ----------

async function getRun() {
  const { run } = await chrome.storage.session.get('run');
  return run || null;
}

async function setRun(run) {
  await chrome.storage.session.set({ run });
}

async function startRun() {
  const items = await readyApplications();
  if (!items.length) return { started: false, message: 'Nothing is ready to submit yet.' };

  const run = { queue: items.map(a => a.id), byId: Object.fromEntries(items.map(a => [a.id, a])), index: 0, tabId: null, done: 0 };
  await setRun(run);
  await openCurrent(run);
  return { started: true, total: items.length };
}

async function openCurrent(run) {
  const id = run.queue[run.index];
  if (!id) {
    await setRun(null);
    return;
  }

  const url = run.byId[id].job.applyUrl;
  if (run.tabId) {
    try {
      await chrome.tabs.update(run.tabId, { url, active: true });
      return;
    } catch {
      /* tab was closed */
    }
  }
  const tab = await chrome.tabs.create({ url, active: true });
  run.tabId = tab.id;
  await setRun(run);
}

// ---------- Auto-submit settings (opt-in, daily limit) ----------

const DEFAULT_DAILY_LIMIT = 15;
const NEXT_JOB_ALARM = 'orvanta-next-job';

function today() {
  return new Date().toISOString().slice(0, 10);
}

async function autoSettings() {
  const { autoSubmit = false, autoSubmitAcknowledged = false, dailyLimit = DEFAULT_DAILY_LIMIT } =
    await chrome.storage.sync.get(['autoSubmit', 'autoSubmitAcknowledged', 'dailyLimit']);
  const { autoDay, autoCount = 0 } = await chrome.storage.local.get(['autoDay', 'autoCount']);
  const usedToday = autoDay === today() ? autoCount : 0;
  const remaining = Math.max(dailyLimit - usedToday, 0);
  return { autoSubmit: autoSubmit && autoSubmitAcknowledged && remaining > 0, enabled: autoSubmit && autoSubmitAcknowledged, remaining, dailyLimit };
}

async function countAutoSubmission() {
  const { autoDay, autoCount = 0 } = await chrome.storage.local.get(['autoDay', 'autoCount']);
  await chrome.storage.local.set({ autoDay: today(), autoCount: autoDay === today() ? autoCount + 1 : 1 });
}

async function advance(markSubmitted, automatic = false) {
  const run = await getRun();
  if (!run) return;

  const id = run.queue[run.index];
  if (markSubmitted && id) {
    await api(`/jobs/applications/${id}/submitted`, { method: 'POST' });
    run.done++;
    if (automatic) await countAutoSubmission();
  }
  run.index++;
  await setRun(run);
  await chrome.action.setBadgeText({ text: '' });

  if (run.index >= run.queue.length) {
    await setRun(null);
    return;
  }

  // In auto mode, pause between applications instead of submitting back to back.
  if ((await autoSettings()).autoSubmit) {
    await chrome.alarms.create(NEXT_JOB_ALARM, { delayInMinutes: 0.5 });
  } else {
    await openCurrent(run);
  }
}

chrome.alarms.onAlarm.addListener(async alarm => {
  if (alarm.name !== NEXT_JOB_ALARM) return;
  const run = await getRun();
  if (run) await openCurrent(run);
});

async function taskForTab(tabId) {
  const run = await getRun();
  if (!run || run.tabId !== tabId) return null;
  const app = run.byId[run.queue[run.index]];
  return app ? { application: app, position: run.index + 1, total: run.queue.length } : null;
}

// Jobs from LinkedIn/Naukri/Indeed can redirect to any company site. When the user granted the
// optional "all https sites" permission, inject the filler into the run tab wherever it lands.
chrome.tabs.onUpdated.addListener(async (tabId, change, tab) => {
  if (change.status !== 'complete' || !/^https:/.test(tab.url || '')) return;
  const run = await getRun();
  if (!run || run.tabId !== tabId) return;
  try {
    await chrome.scripting.insertCSS({ target: { tabId }, files: ['content.css'] });
    await chrome.scripting.executeScript({ target: { tabId }, files: ['content.js'] });
  } catch {
    // No permission for this site: the user can still fill it manually and press "I submitted it".
  }
});

// ---------- Messages from popup and content script ----------

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  (async () => {
    switch (message.type) {
      case 'status': {
        const run = await getRun();
        try {
          const ready = await readyApplications();
          const auto = await autoSettings();
          return { signedIn: true, ready: ready.length, running: !!run, position: run ? run.index + 1 : 0, total: run?.queue.length ?? 0, auto };
        } catch (e) {
          return { signedIn: false, error: e.message };
        }
      }
      case 'start':
        return await startRun();
      case 'stop':
        await chrome.alarms.clear(NEXT_JOB_ALARM);
        await setRun(null);
        await chrome.action.setBadgeText({ text: '' });
        return { stopped: true };
      case 'task': // content script asks: is this tab part of the run?
        return await taskForTab(sender.tab?.id);
      case 'cv':
        return await cvAsDataUrl();
      case 'submitted':
        await advance(true, !!message.automatic);
        return { ok: true };
      case 'settings':
        return await autoSettings();
      case 'needs-user':
        // Auto mode paused on this job: flag it so the user notices.
        await chrome.action.setBadgeBackgroundColor({ color: '#d93a4a' });
        await chrome.action.setBadgeText({ text: '!' });
        return { ok: true };
      case 'skip':
        await advance(false);
        return { ok: true };
      case 'fill-active': {
        // "Fill this page" from the popup: any application form (e.g. LinkedIn Easy Apply).
        const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
        if (!tab?.id || !/^https:/.test(tab.url || '')) return { error: 'Open the application form first.' };
        await chrome.scripting.insertCSS({ target: { tabId: tab.id }, files: ['content.css'] });
        await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ['content.js'] });
        await chrome.tabs.sendMessage(tab.id, { type: 'fill-now', answers: await genericAnswers() });
        return { ok: true };
      }
      default:
        return { error: 'Unknown message' };
    }
  })()
    .then(sendResponse)
    .catch(error => sendResponse({ error: error.message }));
  return true; // async response
});
