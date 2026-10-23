const content = document.getElementById('content');

function send(type) {
  return chrome.runtime.sendMessage({ type });
}

function render(html) {
  content.innerHTML = html;
}

function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text ?? '';
  return div.innerHTML;
}

async function refresh() {
  const status = await send('status');

  if (!status?.signedIn) {
    render(`
      <div class="card">
        <p class="error">${status?.error || 'Not signed in.'}</p>
        <small>Sign in at <a href="http://localhost:4200/login" target="_blank">Orvanta</a> in this browser, then reopen this popup.</small>
      </div>`);
    return;
  }

  if (status.running && status.blockedHost) {
    // The run's tab is on a site Orvanta may not access yet. Permission prompts must come from a click here.
    render(`
      <div class="card">
        <p class="error">Orvanta isn't allowed on <b>${status.blockedHost}</b>, so it can't fill this job.</p>
        <small>Allow Orvanta on job sites (company careers pages) to continue.</small>
      </div>
      <button id="allow" class="primary">Allow &amp; continue</button>
      <button id="stop">Stop</button>`);
    document.getElementById('allow').onclick = async () => {
      try {
        await chrome.permissions.request({ origins: ['https://*/*'] });
      } catch {
        /* declined */
      }
      await send('retry-blocked');
      window.close();
    };
    document.getElementById('stop').onclick = async () => { await send('stop'); refresh(); };
    return;
  }

  if (status.running) {
    const attention = status.attention
      ? `<div class="card attention"><b>Action required</b><br><small>${escapeHtml(status.attention)}</small></div>`
      : '';
    render(`
      <div class="card">
        <div class="big">${status.position} / ${status.total}</div>
        <small>${status.auto?.autoSubmit ? 'Auto-applying…' : 'Applying… review each form and press the site\'s Submit button.'}</small>
      </div>
      ${attention}
      <button id="stop">Stop</button>`);
    document.getElementById('stop').onclick = async () => { await send('stop'); refresh(); };
    return;
  }

  const auto = status.auto || {};
  const autoLine = auto.enabled
    ? (auto.remaining > 0 ? `Auto-submit ON · ${auto.remaining} left today` : 'Auto-submit paused · daily limit reached')
    : 'Auto-submit OFF · you press Submit (change in Options)';
  render(`
    <div class="card">
      <div class="big">${status.ready}</div>
      <small>approved application${status.ready === 1 ? '' : 's'} ready to submit</small>
    </div>
    <p><small>${autoLine}</small></p>
    <button id="start" class="primary" ${status.ready ? '' : 'disabled'}>${auto.autoSubmit ? 'Start auto-applying' : 'Start applying'}</button>
    <button id="fill">Fill this page</button>
    <button id="settings">Auto-submit settings</button>
    <p><small>Use “Fill this page” on any application form, e.g. a LinkedIn Easy Apply step.</small></p>`);

  document.getElementById('settings').onclick = () => chrome.runtime.openOptionsPage();

  document.getElementById('start').onclick = async () => {
    // Jobs found via LinkedIn/Indeed/Glassdoor can open on any company site. Ask once (must be in this
    // click handler) so the extension may fill forms there; if declined, known sites still work.
    try {
      await chrome.permissions.request({ origins: ['https://*/*'] });
    } catch {
      /* ignored */
    }
    const result = await send('start');
    if (result?.error) render(`<div class="card error">${result.error}</div>`);
    else window.close();
  };
  document.getElementById('fill').onclick = async () => {
    const result = await send('fill-active');
    if (result?.error) render(`<div class="card error">${result.error}</div>`);
    else window.close();
  };
}

refresh();
