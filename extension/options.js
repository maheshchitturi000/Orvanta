const input = document.getElementById('api');

chrome.storage.sync.get('apiBase').then(({ apiBase }) => {
  input.value = apiBase || 'http://localhost:5095/api';
});

document.getElementById('save').onclick = async () => {
  const value = input.value.trim().replace(/\/$/, '');
  if (!/^https?:\/\/.+\/api$/.test(value)) {
    document.getElementById('saved').textContent = 'Enter a URL ending in /api';
    return;
  }
  await chrome.storage.sync.set({ apiBase: value });
  document.getElementById('saved').textContent = 'Saved ✓';
};
