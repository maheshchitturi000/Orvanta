// Orvanta content script: fills an application form from prepared answers.
// It never clicks Submit — the user reviews and submits; we detect the confirmation page.
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

  const CONFIRMATION = /thank you for (applying|your application)|application (has been )?(submitted|received)|we('ve| have) received your application/i;

  function banner(html, actions = []) {
    document.getElementById('orvanta-banner')?.remove();
    const el = document.createElement('div');
    el.id = 'orvanta-banner';
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

  async function runForQueuedTask() {
    const task = await chrome.runtime.sendMessage({ type: 'task' });
    if (!task || task.error) return;

    const { application, position, total } = task;

    if (CONFIRMATION.test(document.body.innerText)) {
      banner(`<b>Submitted ✓</b> ${application.job.title} at ${application.job.company}. Moving to the next job…`);
      await chrome.runtime.sendMessage({ type: 'submitted' });
      return;
    }

    // Forms render client-side on some boards; give them a moment.
    await new Promise(resolve => setTimeout(resolve, 1200));
    const filled = fill(application.answers, application.coverLetter);
    const cv = await attachCv();

    banner(
      `<b>Job ${position} of ${total}:</b> ${application.job.title} at ${application.job.company}<br>` +
      `Filled ${filled} field${filled === 1 ? '' : 's'}${cv ? ' and attached your CV' : ''}. ` +
      `Answer anything left, review, then press the site's <b>Submit</b> button.`,
      [
        { label: 'I submitted it', primary: true, onClick: () => chrome.runtime.sendMessage({ type: 'submitted' }) },
        { label: 'Skip this job', onClick: () => chrome.runtime.sendMessage({ type: 'skip' }) }
      ]
    );

    // Detect single-page confirmations (no navigation after submit).
    const observer = new MutationObserver(() => {
      if (CONFIRMATION.test(document.body.innerText)) {
        observer.disconnect();
        banner(`<b>Submitted ✓</b> Moving to the next job…`);
        chrome.runtime.sendMessage({ type: 'submitted' });
      }
    });
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
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
