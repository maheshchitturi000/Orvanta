import { TestBed } from '@angular/core/testing';
import { ExtensionBridge } from './extension-bridge.service';

/**
 * Stand-in for extension/bridge.js: answers pings and start requests on this window. Replies are
 * dispatched with the same origin/source a real browser sets for window.postMessage (jsdom leaves them
 * empty, and the service rightly ignores messages without them).
 */
function fakeExtension(options: { startResult?: Record<string, unknown> } = {}): () => void {
  const reply = (data: Record<string, unknown>) =>
    setTimeout(() =>
      window.dispatchEvent(new MessageEvent('message', { data: { source: 'orvanta-extension', ...data }, origin: window.location.origin, source: window }))
    );
  const listener = (event: MessageEvent) => {
    const data = event.data;
    if (data?.source !== 'orvanta-app') return;
    if (data.type === 'ping') {
      reply({ type: 'ready', version: '1.1.0' });
    }
    if (data.type === 'start-applying') {
      reply({ type: 'start-result', requestId: data.requestId, ...(options.startResult ?? { started: true, total: 5 }) });
    }
  };
  window.addEventListener('message', listener);
  return () => window.removeEventListener('message', listener);
}

describe('ExtensionBridge', () => {
  let bridge: ExtensionBridge;

  beforeEach(() => {
    bridge = TestBed.inject(ExtensionBridge);
  });

  it('reports the version when the extension answers', async () => {
    const remove = fakeExtension();
    expect(await bridge.detect()).toBe('1.1.0');
    remove();
  });

  it('reports null when the extension is missing or turned off', async () => {
    expect(await bridge.detect(100)).toBeNull();
  });

  it('waits until the extension is turned on', async () => {
    const controller = new AbortController();
    let remove = () => {};
    setTimeout(() => (remove = fakeExtension()), 150); // user enables it a moment later
    await expect(bridge.waitForExtension(controller.signal)).resolves.toBe('1.1.0');
    remove();
  });

  it('stops waiting when the dialog closes', async () => {
    const controller = new AbortController();
    const waiting = bridge.waitForExtension(controller.signal);
    controller.abort();
    await expect(waiting).rejects.toThrow('Stopped waiting');
  });

  it('passes on the start result, including the permission step', async () => {
    let remove = fakeExtension();
    expect(await bridge.startApplying(true)).toEqual(expect.objectContaining({ started: true, total: 5 }));
    remove();

    remove = fakeExtension({ startResult: { started: false, needsPermission: true } });
    expect(await bridge.startApplying(false)).toEqual(expect.objectContaining({ started: false, needsPermission: true }));
    remove();
  });
});
