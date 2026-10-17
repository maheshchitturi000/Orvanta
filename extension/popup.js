const content = document.getElementById('content');

function send(type) {
  return chrome.runtime.sendMessage({ type });
}

function render(html) {
  content.innerHTML = html;
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

  if (status.running) {
    render(`
      <div class="card">
        <div class="big">${status.position} / ${status.total}</div>
        <small>Applying… review each form and press the site's Submit button.</small>
      </div>
      <button id="stop">Stop</button>`);
    document.getElementById('stop').onclick = async () => { await send('stop'); refresh(); };
    return;
  }

  render(`
    <div class="card">
      <div class="big">${status.ready}</div>
      <small>approved application${status.ready === 1 ? '' : 's'} ready to submit</small>
    </div>
    <button id="start" class="primary" ${status.ready ? '' : 'disabled'}>Start applying</button>
    <button id="fill">Fill this page</button>
    <p><small>Use “Fill this page” on any application form, e.g. a LinkedIn Easy Apply step.</small></p>`);

  document.getElementById('start').onclick = async () => {
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
