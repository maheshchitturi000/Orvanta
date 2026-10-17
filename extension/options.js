const $ = id => document.getElementById(id);

chrome.storage.sync.get(['apiBase', 'autoSubmit', 'autoSubmitAcknowledged', 'dailyLimit']).then(s => {
  $('api').value = s.apiBase || 'http://localhost:5095/api';
  $('auto').checked = !!s.autoSubmit;
  $('ack').checked = !!s.autoSubmitAcknowledged;
  $('limit').value = s.dailyLimit || 15;
});

$('save').onclick = async () => {
  const saved = $('saved');
  const apiBase = $('api').value.trim().replace(/\/$/, '');
  if (!/^https?:\/\/.+\/api$/.test(apiBase)) {
    saved.textContent = 'Enter an API URL ending in /api';
    return;
  }

  const autoSubmit = $('auto').checked;
  const acknowledged = $('ack').checked;
  if (autoSubmit && !acknowledged) {
    saved.textContent = 'Tick the acknowledgement to turn on auto-submit.';
    return;
  }

  const dailyLimit = Math.min(Math.max(parseInt($('limit').value, 10) || 15, 1), 50);
  await chrome.storage.sync.set({ apiBase, autoSubmit, autoSubmitAcknowledged: acknowledged, dailyLimit });
  $('limit').value = dailyLimit;
  saved.textContent = autoSubmit ? `Saved ✓ Auto-submit on (up to ${dailyLimit}/day)` : 'Saved ✓';
};
