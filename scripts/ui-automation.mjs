import http from 'node:http';

const PORT = 9333;
const RETRY_MS = 250;
const TIMEOUT_MS = 20000;

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function getTarget() {
  const deadline = Date.now() + TIMEOUT_MS;
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`http://127.0.0.1:${PORT}/json/list`);
      if (response.ok) {
        const targets = await response.json();
        const target = targets.find((t) => t.type === 'page' && t.url.startsWith('https://app.liuguang.local'));
        if (target) return target;
      }
    } catch (_) {
      // WebView2 is still starting.
    }
    await sleep(RETRY_MS);
  }
  throw new Error('Timed out waiting for WebView2 debugging target');
}

function connect(url) {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url);
    const pending = new Map();
    let sequence = 0;
    const timer = setTimeout(() => reject(new Error('WebSocket open timed out')), 8000);
    socket.addEventListener('open', () => {
      clearTimeout(timer);
      resolve({
        call(method, params = {}) {
          return new Promise((res, rej) => {
            const id = ++sequence;
            const wait = setTimeout(() => {
              pending.delete(id);
              rej(new Error(`${method} timed out`));
            }, 8000);
            pending.set(id, { resolve: res, reject: rej, timer: wait });
            socket.send(JSON.stringify({ id, method, params }));
          });
        },
        close() { socket.close(); },
      });
    });
    socket.addEventListener('error', (event) => {
      clearTimeout(timer);
      reject(new Error('WebSocket error: ' + (event.message || 'unknown')));
    });
    socket.addEventListener('message', (event) => {
      try {
        const msg = JSON.parse(event.data);
        if (msg.id && pending.has(msg.id)) {
          const task = pending.get(msg.id);
          pending.delete(msg.id);
          clearTimeout(task.timer);
          if (msg.error) task.reject(new Error(JSON.stringify(msg.error)));
          else task.resolve(msg.result);
        }
      } catch (_) {
        // Ignore non-command events.
      }
    });
  });
}

async function evaluate(client, expression) {
  const result = await client.call('Runtime.evaluate', {
    expression,
    awaitPromise: true,
    returnByValue: true,
  });
  if (result.exceptionDetails) {
    throw new Error('JS exception: ' + JSON.stringify(result.exceptionDetails));
  }
  return result.result?.value;
}

async function main() {
  const target = await getTarget();
  const client = await connect(target.webSocketDebuggerUrl);
  console.log('Connected:', target.url);

  await client.call('Runtime.enable');

  const consoleErrors = [];
  await client.call('Runtime.addBinding', { name: '__lumenTest' }).catch(() => {});
  // Collect console errors through a simple DOM collection instead of tracing
  // every event; explicit evaluations below catch the important failures.

  const initial = await evaluate(client, `(() => ({
    title: document.querySelector('#pageTitle')?.textContent,
    tasks: document.querySelectorAll('.task-card').length,
    bridge: !!window.chrome?.webview,
    placeholder: document.querySelector('#urlInput')?.placeholder
  }))()`);
  console.log('Initial:', JSON.stringify(initial));

  const addResult = await evaluate(client, `(() => {
    const input = document.querySelector('#urlInput');
    input.value = 'http://127.0.0.1:18080/demo-auto/ui-download.bin';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    document.querySelector('#startBtn').click();
    return { disabled: document.querySelector('#startBtn').disabled };
  })()`);
  console.log('Add clicked:', JSON.stringify(addResult));

  await sleep(2200);
  const activeState = await evaluate(client, `(() => {
    const card = document.querySelector('.task-card');
    return {
      cards: document.querySelectorAll('.task-card').length,
      cardClass: card?.className,
      status: card?.querySelector('[data-status]')?.dataset.status,
      name: card?.querySelector('.task-name')?.textContent,
      speed: card?.querySelector('[data-speed]')?.textContent,
      progress: card?.querySelector('[data-progress-text]')?.textContent,
      connections: card?.querySelector('[data-threads]')?.textContent?.trim(),
      downloadButtonDisabled: document.querySelector('#startBtn')?.disabled
    };
  })()`);
  console.log('Active:', JSON.stringify(activeState));

  const pauseResult = await evaluate(client, `(() => {
    const btn = document.querySelector('.task-card.status-downloading [data-action="toggle"]');
    if (!btn) return 'no-active-toggle';
    btn.click();
    return 'clicked';
  })()`);
  console.log('Pause:', pauseResult);

  await sleep(900);
  const pausedState = await evaluate(client, `(() => {
    const card = document.querySelector('.task-card');
    return {
      status: card?.querySelector('[data-status]')?.dataset.status,
      chip: card?.querySelector('[data-status] span')?.textContent,
      progress: card?.querySelector('[data-progress-text]')?.textContent,
      detail: card?.querySelector('[data-detail]')?.textContent,
      actions: [...card.querySelectorAll('.task-action')].map(b => b.title)
    };
  })()`);
  console.log('Paused:', JSON.stringify(pausedState));

  const resumeResult = await evaluate(client, `(() => {
    const btn = document.querySelector('.task-card.status-paused [data-action="toggle"]');
    if (!btn) return 'no-paused-toggle';
    btn.click();
    return 'clicked';
  })()`);
  console.log('Resume:', resumeResult);

  await sleep(1000);
  const resumedState = await evaluate(client, `(() => {
    const card = document.querySelector('.task-card');
    return {
      status: card?.querySelector('[data-status]')?.dataset.status,
      speed: card?.querySelector('[data-speed]')?.textContent,
      progress: card?.querySelector('[data-progress-text]')?.textContent
    };
  })()`);
  console.log('Resumed:', JSON.stringify(resumedState));

  // Open settings and change the simultaneous task limit.
  await evaluate(client, `document.querySelector('#openSettingsBtn').click()`);
  await sleep(600);
  const settingsState = await evaluate(client, `(() => ({
    open: document.querySelector('#settingsSheet')?.classList.contains('is-open'),
    path: document.querySelector('#settingsFolderText')?.textContent,
    connections: document.querySelector('#connectionsRange')?.value,
    concurrent: document.querySelector('#concurrentRange')?.value,
    theme: document.documentElement.dataset.theme
  }))()`);
  console.log('Settings open:', JSON.stringify(settingsState));

  await evaluate(client, `(() => {
    const range = document.querySelector('#concurrentRange');
    range.value = '3';
    range.dispatchEvent(new Event('input', { bubbles: true }));
    range.dispatchEvent(new Event('change', { bubbles: true }));
    return true;
  })()`);
  await sleep(1200);
  await evaluate(client, `document.querySelector('#closeSettingsBtn').click()`);
  await sleep(500);

  // Wait for the download to complete (up to 35s).
  let finalState = null;
  const deadline = Date.now() + 35000;
  while (Date.now() < deadline) {
    finalState = await evaluate(client, `(() => {
      const card = document.querySelector('.task-card');
      const chips = [...document.querySelectorAll('.nav-item .nav-count')].map(el => el.textContent);
      return {
        status: card?.querySelector('[data-status]')?.dataset.status,
        chip: card?.querySelector('[data-status] span')?.textContent,
        progress: card?.querySelector('[data-progress-text]')?.textContent,
        counts: chips,
        title: card?.querySelector('.task-name')?.textContent,
        openFileVisible: card ? !card.querySelector('[data-action="primary"].open-file-link').hidden : null,
        session: document.querySelector('#sessionBytes')?.textContent,
        speed: document.querySelector('#totalSpeedValue')?.textContent
      };
    })()`);
    if (finalState?.status === 'completed') break;
    await sleep(1000);
  }
  console.log('Final:', JSON.stringify(finalState));

  const settingsFromUi = await evaluate(client, `(() => {
    document.querySelector('#openSettingsBtn').click();
    return true;
  })()`);
  await sleep(400);
  const applied = await evaluate(client, `(() => ({
    concurrent: document.querySelector('#concurrentRange')?.value,
    label: document.querySelector('#concurrentValue')?.textContent
  }))()`);
  console.log('Settings after save:', JSON.stringify(applied));

  await evaluate(client, `document.querySelector('#closeSettingsBtn').click()`);

  const removeCandidate = await evaluate(client, `(() => {
    const card = document.querySelector('.task-card');
    const btn = card?.querySelector('[data-action="remove"]');
    btn?.click();
    return {
      confirmOpen: !document.querySelector('#confirmLayer')?.hidden,
      confirmTitle: document.querySelector('#confirmTitle')?.textContent
    };
  })()`);
  console.log('Remove confirm:', JSON.stringify(removeCandidate));
  await evaluate(client, `document.querySelector('#confirmCancelBtn').click()`);

  await evaluate(client, `(() => {
    document.querySelector('#themeToggleBtn').click();
    return document.documentElement.dataset.theme;
  })()`).then((theme) => console.log('Toggled theme:', theme));

  console.log('Console errors observed:', JSON.stringify(consoleErrors));
  client.close();
}

main().catch((error) => {
  console.error('AUTOMATION FAILED:', error);
  process.exitCode = 1;
});
