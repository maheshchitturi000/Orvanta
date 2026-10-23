// Opened when the web app starts applying but Chrome hasn't granted access to all https sites yet.
// Permission prompts need a click inside the extension, which is what this page provides.
async function start(withPermission) {
  if (withPermission) {
    try {
      await chrome.permissions.request({ origins: ['https://*/*'] });
    } catch {
      /* declined: known sites still work */
    }
  }
  const result = await chrome.runtime.sendMessage({ type: 'start' });
  if (result?.error || result?.started === false) {
    document.getElementById('error').textContent = result.error || result.message;
    return;
  }
  window.close(); // The first job is open in another tab.
}

document.getElementById('allow').onclick = () => start(true);
document.getElementById('skip').onclick = () => start(false);
