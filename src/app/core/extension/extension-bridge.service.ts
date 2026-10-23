import { isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject } from '@angular/core';

export interface StartResult {
  started: boolean;
  total?: number;
  /** Chrome needs the user's OK for all https sites; the extension opened a tab asking for it. */
  needsPermission?: boolean;
  message?: string;
  error?: string;
}

interface ExtensionMessage {
  source: 'orvanta-extension';
  type: 'ready' | 'start-result';
  version?: string;
  requestId?: string;
}

/**
 * Talks to the Orvanta Chrome extension through its bridge script (extension/bridge.js), which runs on
 * this site. Messages go over window.postMessage on this origin only; no tokens are exchanged.
 */
@Injectable({ providedIn: 'root' })
export class ExtensionBridge {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  /** Chromium browsers (Chrome, Edge, Brave…) can run the extension. */
  get browserSupported(): boolean {
    return this.isBrowser && /Chrome\//.test(navigator.userAgent);
  }

  /** Resolves with the extension version when it answers, or null when it's missing or turned off. */
  detect(timeoutMs = 700): Promise<string | null> {
    if (!this.isBrowser) {
      return Promise.resolve(null);
    }
    return new Promise(resolve => {
      const timer = setTimeout(() => finish(null), timeoutMs);
      const listener = (event: MessageEvent<ExtensionMessage>) => {
        if (this.fromExtension(event) && event.data.type === 'ready') {
          finish(event.data.version ?? 'unknown');
        }
      };
      const finish = (version: string | null) => {
        clearTimeout(timer);
        window.removeEventListener('message', listener);
        resolve(version);
      };
      window.addEventListener('message', listener);
      this.post({ type: 'ping' });
    });
  }

  /**
   * Waits until the extension appears (the user turns it on while this is pending), checking every
   * second. The extension also announces itself the moment it starts. Stops when `signal` aborts.
   */
  waitForExtension(signal: AbortSignal): Promise<string> {
    return new Promise((resolve, reject) => {
      const listener = (event: MessageEvent<ExtensionMessage>) => {
        if (this.fromExtension(event) && event.data.type === 'ready') {
          done();
          resolve(event.data.version ?? 'unknown');
        }
      };
      const poll = setInterval(() => this.post({ type: 'ping' }), 1000);
      const done = () => {
        clearInterval(poll);
        window.removeEventListener('message', listener);
      };
      signal.addEventListener('abort', () => {
        done();
        reject(new DOMException('Stopped waiting', 'AbortError'));
      });
      window.addEventListener('message', listener);
      this.post({ type: 'ping' });
    });
  }

  /** Asks the extension to open the approved jobs one by one and apply. */
  startApplying(autoSubmit: boolean, timeoutMs = 15000): Promise<StartResult> {
    const requestId = crypto.randomUUID();
    return new Promise(resolve => {
      const timer = setTimeout(() => {
        window.removeEventListener('message', listener);
        resolve({ started: false, error: 'The extension did not respond. Refresh this page and try again.' });
      }, timeoutMs);
      const listener = (event: MessageEvent<ExtensionMessage & StartResult>) => {
        if (this.fromExtension(event) && event.data.type === 'start-result' && event.data.requestId === requestId) {
          clearTimeout(timer);
          window.removeEventListener('message', listener);
          const { started, total, needsPermission, message, error } = event.data;
          resolve({ started: !!started, total, needsPermission, message, error });
        }
      };
      window.addEventListener('message', listener);
      this.post({ type: 'start-applying', autoSubmit, requestId });
    });
  }

  private post(message: Record<string, unknown>): void {
    window.postMessage({ source: 'orvanta-app', ...message }, window.location.origin);
  }

  private fromExtension(event: MessageEvent<ExtensionMessage>): boolean {
    return event.source === window && event.origin === window.location.origin && event.data?.source === 'orvanta-extension';
  }
}
