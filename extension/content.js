// Orvanta content script: works through a job application page by page (see "What step is this page?").
// Normal mode fills forms and the user presses Submit; auto mode (opted in, daily limit) also presses Next/Submit.
(() => {
  if (window.__orvantaLoaded) return;
  window.__orvantaLoaded = true;

  /** Form label/name/placeholder patterns → answer keys. First match wins. */
  const FIELD_RULES = [
    { key: 'firstName', test: /\bfirst[\s_-]?name\b|given name/i },
    { key: 'lastName', test: /\blast[\s_-]?name\b|surname|family name/i },
    { key: 'fullName', test: /^(full\s*)?name\b|your name|legal name/i },
    { key: 'email', test: /e-?mail/i },
    { key: 'phone', test: /phone|mobile|contact number/i },
    { key: 'linkedInUrl', test: /linked\s*in/i },
    { key: 'portfolioUrl', test: /github|portfolio|website|personal (site|url)/i },
    { key: 'location', test: /current location|^location|city|where are you (based|located)/i },
    { key: 'currentTitle', test: /current (job )?title|current role|current position/i },
    { key: 'yearsOfExperience', test: /years of (professional )?experience|how many years/i },
    { key: 'noticePeriodDays', test: /notice period|start date|available to start/i },
    { key: 'expectedSalary', test: /salary|compensation expectation|expected (ctc|pay)/i },
    { key: 'workAuthorization', test: /work authori[sz]ation|right to work|legally authori[sz]ed/i },
    { key: 'needsVisaSponsorship', test: /sponsor/i },
    { key: 'willingToRelocate', test: /relocat/i }
  ];

  const isVisible = el => !!(el.offsetWidth || el.offsetHeight || el.getClientRects().length);

  function labelText(el) {
    const parts = [];
    if (el.id) {
      const label = document.querySelector(`label[for="${CSS.escape(el.id)}"]`);
      if (label) parts.push(label.textContent);
    }
    const wrapping = el.closest('label');
    if (wrapping) parts.push(wrapping.textContent);
    const labelledBy = el.getAttribute('aria-labelledby');
    if (labelledBy) labelledBy.split(' ').forEach(id => parts.push(document.getElementById(id)?.textContent || ''));
    parts.push(el.getAttribute('aria-label') || '', el.getAttribute('placeholder') || '', el.name || '', el.id || '');
    // Question text often sits in the field's container (Greenhouse/Lever/Ashby/LinkedIn).
    const container = el.closest('.field, .application-question, .application-field, [class*="_fieldEntry"], .fb-dash-form-element, div');
    if (container) parts.push(container.querySelector('label, legend, .application-label, .text')?.textContent || '');
    return parts.join(' ').replace(/\s+/g, ' ').trim();
  }

  /** Sets a value so React/Angular-controlled inputs notice it. */
  function setValue(el, value) {
    const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : el instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
    el.dispatchEvent(new Event('blur', { bubbles: true }));
  }

  function chooseOption(select, value) {
    const wanted = String(value).toLowerCase();
    const option = [...select.options].find(o => o.text.trim().toLowerCase() === wanted)
      || [...select.options].find(o => o.text.toLowerCase().startsWith(wanted));
    if (option) {
      setValue(select, option.value);
      return true;
    }
    return false;
  }

  function fill(answers, coverLetter) {
    let filled = 0;
    const inputs = [...document.querySelectorAll('input, textarea, select')]
      .filter(el => isVisible(el) && !el.disabled && !el.readOnly)
      .filter(el => !['hidden', 'submit', 'button', 'file', 'checkbox', 'password'].includes(el.type));

    for (const el of inputs) {
      const text = labelText(el);

      if (el instanceof HTMLTextAreaElement && coverLetter && /cover letter|why .*(join|interested|company)|additional information|message to/i.test(text)) {
        if (!el.value) { setValue(el, coverLetter); filled++; }
        continue;
      }

      const rule = FIELD_RULES.find(r => r.test.test(text));
      const value = rule ? answers[rule.key] : null;
      if (!value) continue;

      if (el instanceof HTMLSelectElement) {
        if (chooseOption(el, value)) filled++;
      } else if (el.type === 'radio') {
        const radioLabel = labelText(el).toLowerCase();
        if (radioLabel.endsWith(String(value).toLowerCase()) && !el.checked) { el.click(); filled++; }
      } else if (!el.value) {
        setValue(el, value);
        filled++;
      }
    }
    return filled;
  }

  async function attachCv() {
    const input = [...document.querySelectorAll('input[type="file"]')].find(el => /resume|cv/i.test(labelText(el)) || el.name === 'resume')
      || document.querySelector('input[type="file"]');
    if (!input || input.files?.length) return false;

    const cv = await chrome.runtime.sendMessage({ type: 'cv' });
    if (!cv?.base64) return false;

    const bytes = Uint8Array.from(atob(cv.base64), c => c.charCodeAt(0));
    const transfer = new DataTransfer();
    transfer.items.add(new File([bytes], cv.name, { type: cv.type }));
    input.files = transfer.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
    input.dispatchEvent(new Event('input', { bubbles: true }));
    return true;
  }

  const CONFIRMATION = /thank you for (applying|your application)|application (has been )?(submitted|received|sent)|we('ve| have) received your application|you('ve| have) (successfully )?applied|successfully applied|applied successfully/i;

  function banner(html, actions = [], { attention = false } = {}) {
    document.getElementById('orvanta-banner')?.remove();
    const el = document.createElement('div');
    el.id = 'orvanta-banner';
    el.classList.toggle('orv-attention', attention);
    el.innerHTML = `<div class="orv-brand">O</div><div class="orv-text">${html}</div><div class="orv-actions"></div>`;
    for (const action of actions) {
      const button = document.createElement('button');
      button.textContent = action.label;
      button.className = action.primary ? 'orv-primary' : '';
      button.addEventListener('click', action.onClick);
      el.querySelector('.orv-actions').append(button);
    }
    document.body.append(el);
  }

  // ---------- What step is this page? ----------
  // Between a job and its form, sites put all kinds of steps: an "Apply" button worded many ways, an account
  // wall (sign in / sign up, often email + one-time code), a "check your email" link, a CAPTCHA, extra
  // questions, a dashboard after signing in. The agent recognises the step, does what it safely can, and asks
  // the user for the rest ("Action required"), then continues by itself once the page moves on.
  // It never types passwords, solves CAPTCHAs or ticks consent boxes, and presses a final Submit at most once.

  const APPLY_ENTRY = /^(apply|apply now|apply here|apply (to|for) (this )?(job|position|role|opening|vacancy)|apply on (the )?(company|employer)('?s)? (site|website)|apply (at|via|on) company (site|website)|continue to apply|continue application|easy apply|quick apply|i'?m interested|start (my |your |an? )?application|begin (my |your )?application)$/i;
  const SUBMIT = /^(submit application|submit your application|submit|send application|send|apply|apply now|finish|complete application)$/i;
  const NEXT = /^(next|continue|proceed|review|review your application|continue to next step|save and continue|save & continue)$/i;
  const EMAIL_CONTINUE = /^(continue|next|proceed|send (otp|code)|get (otp|code)|continue with e-?mail|sign ?(in|up) with e-?mail|log ?in|sign ?in|sign ?up|register|verify)$/i;
  const AUTH_WORDS = /\b(sign ?in|log ?in|sign ?up|register|create (an |your )?account|continue with (google|linkedin|e-?mail|microsoft|apple)|one[- ]time|otp)\b/i;
  const SIGN_UP_WORDS = /\b(sign ?up|register|create (an |your )?account|join (now|for free|us))\b/i;
  const CHECK_EMAIL = /check your (e-?mail|inbox)|verify your e-?mail|confirm your e-?mail|we('ve| have)( just)? sent (you )?(an? )?(e-?mail|link|verification)|click the link (in|we sent)/i;
  const OTP_WORDS = /\b(otp|one[- ]?time (pass(word|code)|code)|verification code|security code|confirmation code|enter (the )?code|\d[- ]digit code)\b/i;

  const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
  const humanPause = () => sleep(1500 + Math.random() * 2500);

  const visibleFields = () => [...document.querySelectorAll('input, textarea, select')]
    .filter(el => isVisible(el) && !el.disabled && !['hidden', 'submit', 'button', 'image', 'search'].includes(el.type));

  /** Visible fields a person would fill in (search boxes don't count). */
  function formFieldCount() {
    return visibleFields()
      .filter(el => !/search|keyword/i.test(`${el.name} ${el.id} ${el.placeholder || ''} ${el.getAttribute('aria-label') || ''}`))
      .length;
  }

  function buttonText(el) {
    return (el.innerText || el.value || el.getAttribute('aria-label') || '').replace(/\s+/g, ' ').trim();
  }

  /** Button text without screen-reader hints such as "(opens in new window)" or "external link". */
  function plainLabel(el) {
    return buttonText(el)
      .replace(/\(?\s*(opens|open) in (a )?new (window|tab)\s*\)?/gi, '')
      .replace(/\b(external link|link opens externally)\b/gi, '')
      .replace(/[↗→›»]/g, '')
      .trim();
  }

  function findApplyEntry() {
    return [...document.querySelectorAll('a[href], button, [role="button"], input[type="button"], input[type="submit"]')]
      .filter(el => isVisible(el) && !el.disabled)
      .find(el => APPLY_ENTRY.test(plainLabel(el)));
  }

  function findButton(root, pattern) {
    return [...root.querySelectorAll('button, input[type="submit"], [role="button"]')]
      .filter(el => isVisible(el) && !el.disabled && el.getAttribute('aria-disabled') !== 'true')
      // Never press "Apply on company website" style buttons inside a form: they leave the site.
      .filter(el => !/company website|external/i.test(el.getAttribute('aria-label') || ''))
      .find(el => pattern.test(buttonText(el)));
  }

  /** Scope searches to an open dialog (LinkedIn Easy Apply, sign-in pop-ups) when there is one. */
  function formRoot() {
    const dialogs = [...document.querySelectorAll('[role="dialog"], .jobs-easy-apply-modal')].filter(isVisible);
    return dialogs.at(-1) || document;
  }

  /** The dialog (or else the form) an input belongs to, for reading the words around it, headings included. */
  const contextOf = el =>
    el.closest('[role="dialog"], [aria-modal="true"], [class*="modal" i], [class*="dialog" i]') || el.closest('form');

  /** A CAPTCHA still to be solved. Invisible ones are ignored; solved ones fill a hidden response field. */
  function captchaPending() {
    const widgets = [...document.querySelectorAll('iframe')]
      .filter(f => isVisible(f) && f.offsetWidth > 60 && f.offsetHeight > 40)
      .filter(f => /recaptcha\/(api2|enterprise)\/anchor|hcaptcha\.com|challenges\.cloudflare\.com/.test(f.src) && !/size=invisible/.test(f.src));
    if (!widgets.length) return false;
    return ![...document.querySelectorAll('[name="g-recaptcha-response"], [name="h-captcha-response"], [name="cf-turnstile-response"]')]
      .some(el => String(el.value || '').length > 0);
  }

  /** One-time code boxes: a field labelled OTP / verification code, or a row of single-character boxes. */
  function otpFields() {
    const inputs = visibleFields().filter(el => el instanceof HTMLInputElement && !['checkbox', 'radio', 'file', 'password'].includes(el.type));
    const boxes = inputs.filter(el => el.maxLength === 1);
    if (boxes.length >= 4) return boxes;
    return inputs.filter(el => el.autocomplete === 'one-time-code' || OTP_WORDS.test(labelText(el)));
  }

  function detectStep() {
    const text = document.body.innerText;
    if (CONFIRMATION.test(text)) return { kind: 'confirmation' };
    if (captchaPending()) return { kind: 'captcha' };
    if (otpFields().length) return { kind: 'otp' };

    const passwords = visibleFields().filter(el => el.type === 'password');
    if (passwords.length) {
      const words = (contextOf(passwords[0])?.innerText || text).slice(0, 3000);
      return { kind: passwords.length > 1 || SIGN_UP_WORDS.test(words) ? 'sign-up' : 'sign-in' };
    }

    // Email-first sign in / sign up (Cutshort and many job boards): an email box and Continue, then a code.
    const fields = formFieldCount();
    const email = visibleFields().find(el => el.type === 'email' || /e-?mail/i.test(labelText(el)));
    const emailContext = email && contextOf(email);
    if (email && fields <= 3 && emailContext && AUTH_WORDS.test(emailContext.innerText.slice(0, 3000))) {
      return { kind: 'email-step', email, root: emailContext };
    }

    if (fields >= 2) return { kind: 'form' };
    const entry = findApplyEntry();
    if (entry) return { kind: 'apply-entry', entry };
    if (CHECK_EMAIL.test(text)) return { kind: 'check-email' };
    if (fields === 1) return { kind: 'form' };
    return { kind: 'unknown' };
  }

  /** Required fields still empty, and consent boxes we must not tick for the user. */
  function blockers(root) {
    const problems = [];
    const fields = [...root.querySelectorAll('input, textarea, select')].filter(el => isVisible(el) && !el.disabled);

    for (const el of fields) {
      const required = el.required || el.getAttribute('aria-required') === 'true';
      if (!required) continue;
      const label = labelText(el).slice(0, 80) || 'A required question';

      if (el.type === 'checkbox') {
        if (!el.checked) problems.push(`${label} (needs your consent)`);
      } else if (el.type === 'radio') {
        const group = root.querySelectorAll(`input[type="radio"][name="${CSS.escape(el.name)}"]`);
        if (![...group].some(r => r.checked) && !problems.includes(label)) problems.push(label);
      } else if (el.type === 'file') {
        if (!el.files?.length) problems.push(label);
      } else if (!String(el.value || '').trim()) {
        problems.push(label);
      }
    }
    return [...new Set(problems)];
  }

  // Submit is pressed at most once per application, and an email step's Continue once per page. Remembered
  // in sessionStorage so it survives the page reloading (falls back to memory where storage is blocked).
  const pressedInMemory = new Set();
  function pressedOnce(key) {
    try {
      return sessionStorage.getItem(key) === '1' || pressedInMemory.has(key);
    } catch {
      return pressedInMemory.has(key);
    }
  }
  function markPressed(key) {
    pressedInMemory.add(key);
    try {
      sessionStorage.setItem(key, '1');
    } catch {
      /* memory only */
    }
  }
  const submitKey = application => `orvanta-submit-pressed:${application.id}`;

  // ---------- The agent ----------

  /** Watchers started while waiting for the user; stopped whenever the agent moves on. */
  let watchers = [];
  function stopWatching() {
    watchers.forEach(stop => stop());
    watchers = [];
  }

  /** Calls onChange once when the page is no longer at this step (code accepted, signed in, form submitted…). */
  function watchStep(kind, onChange) {
    let done = false;
    const check = () => {
      if (done || detectStep().kind === kind) return;
      done = true;
      stopWatching();
      onChange();
    };
    const timer = setInterval(check, 1500);
    window.addEventListener('focus', check); // back from the email app
    watchers.push(() => { clearInterval(timer); window.removeEventListener('focus', check); });
  }

  /** Clicks an "Apply" entry; links navigate in this tab, buttons may open a dialog or a new tab (the run follows). */
  function pressEntry(entry) {
    const href = entry.tagName === 'A' ? entry.href : '';
    if (href && !href.startsWith('javascript:') && href.split('#')[0] !== location.href.split('#')[0]) {
      location.assign(href);
    } else {
      entry.click();
    }
  }

  async function runForQueuedTask() {
    const task = await chrome.runtime.sendMessage({ type: 'task' });
    if (!task || task.error) return;

    // Pages often render client-side; give them a moment.
    await sleep(1200);
    const settings = await chrome.runtime.sendMessage({ type: 'settings' });
    await agent(task, !!settings?.autoSubmit);
  }

  async function agent(task, auto) {
    stopWatching();
    const { application, position, total } = task;
    const job = `${escapeHtml(application.job.title)} at ${escapeHtml(application.job.company)}`;
    const heading = `<b>${auto ? 'Auto-applying' : 'Job'} ${position} of ${total}:</b> ${job}`;
    const site = escapeHtml(location.hostname.replace(/^www\./, ''));
    const again = () => { chrome.runtime.sendMessage({ type: 'resumed' }); agent(task, auto); };
    const skip = { label: 'Skip', onClick: () => { stopWatching(); chrome.runtime.sendMessage({ type: 'skip' }); } };

    const reportSubmitted = automatic => {
      stopWatching();
      banner(`<b>Submitted ✓</b> ${job}. ${automatic ? 'Next job in about 30 seconds…' : 'Moving to the next job…'}`);
      return chrome.runtime.sendMessage({ type: 'submitted', applicationId: application.id, automatic });
    };
    const iSubmitted = { label: 'I submitted it', onClick: () => reportSubmitted(false) };

    /** Hands a step to the user, says what to do, and continues by itself once the page leaves that step. */
    const actionRequired = (kind, html, { backToJob = false } = {}) => {
      chrome.runtime.sendMessage({ type: 'needs-user', text: html.replace(/<[^>]+>/g, '') });
      banner(`<b>Action required</b> · ${heading}<br>${html}<br><small>Orvanta continues by itself when you're done.</small>`, [
        { label: 'Continue', primary: true, onClick: () => { stopWatching(); again(); } },
        ...(backToJob ? [{ label: 'Back to the job', onClick: () => { stopWatching(); chrome.runtime.sendMessage({ type: 'reopen-job', force: true }); } }] : []),
        iSubmitted,
        skip
      ], { attention: true });
      watchStep(kind, again);
    };

    let waitedForPage = false;
    for (let step = 0; step < 15; step++) {
      const page = detectStep();
      switch (page.kind) {
        case 'confirmation':
          // Counts against today's auto limit only when Orvanta pressed Submit itself.
          await reportSubmitted(auto && pressedOnce(submitKey(application)));
          return;

        case 'captcha':
          return actionRequired(page.kind, 'Solve the CAPTCHA ("I\'m not a robot") on this page. Orvanta never solves CAPTCHAs.');

        case 'otp':
          await chrome.runtime.sendMessage({ type: 'account-step' });
          return actionRequired(page.kind, `Enter the one-time code (OTP) that ${site} sent to your email or phone, then press its Verify or Continue button.`);

        case 'sign-up':
          await chrome.runtime.sendMessage({ type: 'account-step' });
          fill(application.answers, '');
          return actionRequired(page.kind, `${site} needs an account first. Orvanta filled your details: choose a password, accept the site's terms if you agree, and create the account. Orvanta never types passwords.`);

        case 'sign-in':
          await chrome.runtime.sendMessage({ type: 'account-step' });
          return actionRequired(page.kind, `Sign in to ${site} (or use "Continue with Google / LinkedIn" if that's how you joined). Orvanta never types passwords.`);

        case 'check-email':
          await chrome.runtime.sendMessage({ type: 'account-step' });
          return actionRequired(page.kind, `Open the email from ${site} and click its verification link, then come back to this tab.`);

        case 'email-step': {
          await chrome.runtime.sendMessage({ type: 'account-step' });
          if (!page.email.value && application.answers.email) setValue(page.email, application.answers.email);
          const go = findButton(page.root, EMAIL_CONTINUE);
          const key = `orvanta-email-step:${application.id}:${location.pathname}`;
          if (!auto || !go || !page.email.value || pressedOnce(key)) {
            return actionRequired(page.kind, `${site} asks for your email first. Orvanta filled it in: check it and press <b>Continue</b>. ` +
              'If a code is sent, Orvanta will ask you for it.');
          }
          markPressed(key);
          await humanPause();
          go.click();
          await sleep(3000);
          continue;
        }

        case 'apply-entry': {
          const label = escapeHtml(plainLabel(page.entry));
          // Normal mode never presses buttons that might send the application (some sites apply in one
          // click); links only open the next page.
          if (!auto && page.entry.tagName !== 'A') {
            return actionRequired(page.kind, `Press <b>${label}</b>. Orvanta takes over from the next step.`);
          }
          const { allowed } = await chrome.runtime.sendMessage({ type: 'follow-apply' });
          if (!allowed) {
            return actionRequired(page.kind, `Orvanta pressed "${label}" several times but didn't reach an application form. ` +
              'Continue by hand; Orvanta picks up on the next step.', { backToJob: true });
          }
          banner(`${heading}<br>Opening the application…`);
          pressEntry(page.entry);
          await sleep(3000); // a new page restarts the agent; a dialog continues here
          continue;
        }

        case 'form': {
          if (!auto) return guideForm(task, again, iSubmitted, skip);
          if (pressedOnce(submitKey(application))) {
            // Never press Submit twice: a second press could send a duplicate application.
            return actionRequired(page.kind, 'Orvanta pressed Submit but did not see a confirmation. If the application went through, ' +
              'click "I submitted it"; if the page shows an error, fix it and press Submit yourself.');
          }
          const root = formRoot();
          fill(application.answers, application.coverLetter);
          await attachCv();
          await humanPause();

          const missing = blockers(root);
          if (missing.length) {
            return actionRequired('form', 'Answer these, then press the site\'s Next or Submit button:<br>• ' +
              missing.slice(0, 4).map(escapeHtml).join('<br>• '));
          }
          const submit = findButton(root, SUBMIT);
          if (submit) {
            markPressed(submitKey(application)); // before clicking: the page may reload straight away
            submit.click();
            await sleep(4000);
            continue;
          }
          const next = findButton(root, NEXT);
          if (next) {
            next.click();
            await humanPause();
            continue;
          }
          return actionRequired('form', 'Orvanta couldn\'t find the Next or Submit button. Press it yourself; Orvanta continues on the next step.');
        }

        default: {
          // Still loading? Look once more before deciding.
          if (!waitedForPage) {
            waitedForPage = true;
            await sleep(2500);
            continue;
          }
          // After signing in, many sites land on a dashboard instead of the job: go back to it.
          const { reopened } = await chrome.runtime.sendMessage({ type: 'reopen-job' });
          if (reopened) return;
          return actionRequired('unknown', 'Orvanta can\'t tell what to do on this page. Do this step yourself, or go back to the job; ' +
            'Orvanta continues on the next step.', { backToJob: true });
        }
      }
    }

    actionRequired(detectStep().kind, 'This application has more steps than expected. Finish it yourself; Orvanta notices the confirmation.', { backToJob: true });
  }

  /** Normal mode on a form: fill everything known; the user reviews and presses the site's buttons. */
  async function guideForm(task, again, iSubmitted, skip) {
    const { application, position, total } = task;
    let filled = fill(application.answers, application.coverLetter);
    const cv = await attachCv();

    const isLinkedIn = location.hostname.endsWith('linkedin.com');
    const render = () => banner(
      `<b>Job ${position} of ${total}:</b> ${escapeHtml(application.job.title)} at ${escapeHtml(application.job.company)}<br>` +
      (isLinkedIn && filled === 0
        ? 'Click <b>Easy Apply</b> (or <b>Apply</b>). Orvanta fills each step as it appears; you press <b>Next</b> / <b>Submit</b>.'
        : `Filled ${filled} field${filled === 1 ? '' : 's'}${cv ? ' and attached your CV' : ''}. ` +
          'Answer anything left, review, then press the site\'s <b>Submit</b> button.'),
      [
        { ...iSubmitted, primary: true },
        { label: 'Fill again', onClick: async () => { filled += fill(application.answers, application.coverLetter); await attachCv(); render(); } },
        skip
      ]
    );
    render();

    // Multi-step forms add fields as you go: fill each new step.
    let pending = null;
    const observer = new MutationObserver(() => {
      clearTimeout(pending);
      pending = setTimeout(async () => {
        const added = fill(application.answers, application.coverLetter);
        const attached = await attachCv();
        if (added > 0 || attached) {
          filled += added;
          render();
        }
      }, 700);
    });
    observer.observe(document.body, { childList: true, subtree: true });
    watchers.push(() => { observer.disconnect(); clearTimeout(pending); });

    // After the user's Submit the site may confirm, or ask for a code, a sign-in, a CAPTCHA: handle it.
    watchStep('form', again);
  }

  function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text ?? '';
    return div.innerHTML;
  }

  // "Fill this page" from the popup (any form, e.g. LinkedIn Easy Apply).
  chrome.runtime.onMessage.addListener(message => {
    if (message.type === 'fill-now') {
      const filled = fill(message.answers, '');
      attachCv().then(cv =>
        banner(`Filled ${filled} field${filled === 1 ? '' : 's'}${cv ? ' and attached your CV' : ''}. Review and submit when ready.`, [
          { label: 'Close', onClick: () => document.getElementById('orvanta-banner')?.remove() }
        ])
      );
    }
  });

  runForQueuedTask().catch(() => { /* not part of a run */ });
})();
