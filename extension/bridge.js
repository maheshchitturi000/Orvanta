// Runs on the Orvanta web app. Lets the page find out whether this extension is installed and enabled,
// and start applying to approved jobs from inside the app.
// Talks over window.postMessage only; never exposes tokens or extension APIs to the page.
(() => {
  // Injected twice (manifest + service worker start-up) is fine: just announce again.
  if (window.__orvantaBridge) {
    window.__orvantaBridge.announce();
    return;
  }

  // After the extension is disabled or reloaded this script stays in the page, but chrome.runtime.id
  // disappears. Answer only while the extension is really running.
  const alive = () => {
    try {
      return !!chrome.runtime?.id;
    } catch {
      return false;
    }
  };

  const reply = message => window.postMessage({ source: 'orvanta-extension', ...message }, location.origin);

  const announce = () => {
    if (alive()) reply({ type: 'ready', version: chrome.runtime.getManifest().version });
  };

  window.addEventListener('message', async event => {
    if (event.source !== window || event.origin !== location.origin || event.data?.source !== 'orvanta-app') return;

    if (event.data.type === 'ping') {
      announce();
    } else if (event.data.type === 'start-applying' && alive()) {
      let result;
      try {
        result = await chrome.runtime.sendMessage({ type: 'start-from-app', autoSubmit: !!event.data.autoSubmit });
      } catch {
        result = { error: 'The Orvanta extension was just reloaded. Refresh this page and try again.' };
      }
      reply({ type: 'start-result', requestId: event.data.requestId, ...result });
    }
  });

  window.__orvantaBridge = { announce };
  announce();
})();
