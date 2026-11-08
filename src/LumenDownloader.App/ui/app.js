(() => {
  'use strict';

  const webview = window.chrome && window.chrome.webview ? window.chrome.webview : null;
  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => Array.from(root.querySelectorAll(selector));
  const defaultUserAgent = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36';
  const validThemes = new Set(['dark', 'light', 'system']);
  const filterMeta = {
    all: { title: '全部下载', subtitle: '所有传输任务，一目了然。' },
    active: { title: '正在下载', subtitle: '实时查看极速传输进度。' },
    paused: { title: '已暂停', subtitle: '进度已保留，随时继续。' },
    completed: { title: '已完成', subtitle: '文件都在这里，随时打开。' },
    error: { title: '下载失败', subtitle: '看看哪里出了问题，再试一次。' },
  };
  const statusMeta = {
    queued: { label: '排队中', detail: '等待开始' },
    probing: { label: '解析中', detail: '正在连接服务器' },
    downloading: { label: '下载中', detail: '正在传输' },
    paused: { label: '已暂停', detail: '进度已保留' },
    completed: { label: '已完成', detail: '下载完成' },
    error: { label: '下载失败', detail: '传输中断' },
  };
  const kindMeta = {
    video: { icon: 'film', label: 'VIDEO' },
    audio: { icon: 'music', label: 'AUDIO' },
    image: { icon: 'image', label: 'IMAGE' },
    archive: { icon: 'archive', label: 'ARCHIVE' },
    document: { icon: 'doc', label: 'DOC' },
    code: { icon: 'code', label: 'CODE' },
    app: { icon: 'app', label: 'APP' },
    generic: { icon: 'file', label: 'FILE' },
  };
  const extensionKinds = {
    video: new Set(['mp4', 'mkv', 'avi', 'mov', 'wmv', 'flv', 'webm', 'm4v', 'ts', 'm2ts', 'rmvb', '3gp']),
    audio: new Set(['mp3', 'flac', 'wav', 'm4a', 'aac', 'ogg', 'wma', 'opus', 'ape', 'aiff']),
    image: new Set(['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg', 'tiff', 'tif', 'ico', 'heic', 'avif']),
    archive: new Set(['zip', 'rar', '7z', 'tar', 'gz', 'bz2', 'xz', 'tgz', 'iso', 'cab', 'zst']),
    document: new Set(['pdf', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx', 'txt', 'md', 'rtf', 'csv', 'epub', 'mobi', 'azw3']),
    code: new Set(['js', 'ts', 'jsx', 'tsx', 'py', 'java', 'c', 'cpp', 'cs', 'go', 'rs', 'json', 'xml', 'html', 'css', 'sh', 'bat', 'ps1', 'yaml', 'yml']),
    app: new Set(['exe', 'msi', 'apk', 'dmg', 'pkg', 'deb', 'rpm', 'appimage', 'jar']),
  };
  const themeNames = { dark: '深色', light: '浅色', system: '跟随系统' };

  const app = {
    data: null,
    filter: 'all',
    search: '',
    themeMode: 'dark',
    effectiveTheme: 'dark',
    cards: new Map(),
    speedHistory: new Array(24).fill(0),
    lastSparkAt: 0,
    clipboardSuggestion: '',
    settingsOpen: false,
    settingsDraft: null,
    settingsSaveTimer: 0,
    optionsOpen: false,
    optionsConnections: 16,
    confirmResolver: null,
    pendingRequests: new Map(),
    requestSeq: 0,
    emptyAction: 'focus',
    lastFocused: null,
    activeLayer: null,
    currentOptionsFolder: '',
    toastId: 0,
    hydrated: false,
  };

  const els = {};

  function cacheElements() {
    els.body = document.body;
    els.main = $('#mainScroll');
    els.taskList = $('#taskList');
    els.taskTemplate = $('#taskTemplate');
    els.emptyState = $('#emptyState');
    els.emptyTitle = $('#emptyTitle');
    els.emptyText = $('#emptyText');
    els.emptyCta = $('#emptyCta');
    els.emptyTips = $('#emptyTips');
    els.pageTitle = $('#pageTitle');
    els.pageSubtitle = $('#pageSubtitle');
    els.urlInput = $('#urlInput');
    els.startBtn = $('#startBtn');
    els.pasteBtn = $('#pasteBtn');
    els.advancedBtn = $('#advancedBtn');
    els.newDownloadBtn = $('#newDownloadBtn');
    els.searchInput = $('#searchInput');
    els.searchClear = $('#searchClear');
    els.searchBox = $('#searchBox');
    els.themeToggleBtn = $('#themeToggleBtn');
    els.openSettingsBtn = $('#openSettingsBtn');
    els.settingsSheet = $('#settingsSheet');
    els.settingsScrim = $('#settingsScrim');
    els.closeSettingsBtn = $('#closeSettingsBtn');
    els.settingsFolderText = $('#settingsFolderText');
    els.settingsChooseFolder = $('#settingsChooseFolder');
    els.settingsOpenFolder = $('#settingsOpenFolder');
    els.connectionsRange = $('#connectionsRange');
    els.connectionsValue = $('#connectionsValue');
    els.concurrentRange = $('#concurrentRange');
    els.concurrentValue = $('#concurrentValue');
    els.soundToggle = $('#soundToggle');
    els.clipboardToggle = $('#clipboardToggle');
    els.insecureToggle = $('#insecureToggle');
    els.uaInput = $('#uaInput');
    els.resetUaBtn = $('#resetUaBtn');
    els.aboutVersion = $('#aboutVersion');
    els.optionsLayer = $('#optionsLayer');
    els.optionsUrls = $('#optionsUrls');
    els.optionsFolderText = $('#optionsFolderText');
    els.optionsFilename = $('#optionsFilename');
    els.optionsReferer = $('#optionsReferer');
    els.optionsCookie = $('#optionsCookie');
    els.optionsHeaders = $('#optionsHeaders');
    els.submitOptionsBtn = $('#submitOptionsBtn');
    els.chooseOptionsFolder = $('#chooseOptionsFolder');
    els.connectionChips = $('#connectionChips');
    els.confirmLayer = $('#confirmLayer');
    els.confirmTitle = $('#confirmTitle');
    els.confirmMessage = $('#confirmMessage');
    els.confirmOkBtn = $('#confirmOkBtn');
    els.confirmCancelBtn = $('#confirmCancelBtn');
    els.confirmIcon = $('#confirmIcon');
    els.toastStack = $('#toastStack');
    els.dropOverlay = $('#dropOverlay');
    els.clipboardChip = $('#clipboardChip');
    els.clipboardChipUrl = $('#clipboardChipUrl');
    els.clipboardUse = $('#clipboardUse');
    els.clipboardDismiss = $('#clipboardDismiss');
    els.clearCompletedBtn = $('#clearCompletedBtn');
    els.listCount = $('#listCount');
    els.countAll = $('#countAll');
    els.countActive = $('#countActive');
    els.countPaused = $('#countPaused');
    els.countCompleted = $('#countCompleted');
    els.countError = $('#countError');
    els.totalSpeedValue = $('#totalSpeedValue');
    els.totalSpeedUnit = $('#totalSpeedUnit');
    els.speedConnections = $('#speedConnections');
    els.activeCount = $('#activeCount');
    els.queueLimit = $('#queueLimit');
    els.queuedCount = $('#queuedCount');
    els.pausedCount = $('#pausedCount');
    els.sessionBytes = $('#sessionBytes');
    els.completionBadge = $('#completionBadge');
    els.diskFree = $('#diskFree');
    els.diskBar = $('#diskBar');
    els.sideConnections = $('#sideConnections');
    els.sideDiskFree = $('#sideDiskFree');
    els.engineTrack = $('#engineTrack');
    els.engineStatus = $('#engineStatus');
    els.engineDot = $('#engineDot');
    els.liveBadge = $('#liveBadge');
    els.queueVisual = $('#queueVisual');
    els.sparkLine = $('#sparkLine');
    els.sparkArea = $('#sparkArea');
    els.sparkDot = $('#sparkDot');
    els.aboutVersion = $('#aboutVersion');
  }

  function post(type, payload = {}) {
    if (!webview) {
      return;
    }
    try {
      webview.postMessage({ type, ...payload });
    } catch (error) {
      console.error('Host message failed', error);
    }
  }

  function requestHost(type, payload = {}, timeout = 12000) {
    return new Promise((resolve, reject) => {
      if (!webview) {
        reject(new Error('Host bridge unavailable'));
        return;
      }
      const requestId = `req-${++app.requestSeq}`;
      const timer = setTimeout(() => {
        app.pendingRequests.delete(requestId);
        reject(new Error('请求超时'));
      }, timeout);
      app.pendingRequests.set(requestId, { resolve, reject, timer });
      post(type, { ...payload, requestId });
    });
  }

  function resolveRequest(requestId, value) {
    const pending = app.pendingRequests.get(requestId);
    if (!pending) {
      return;
    }
    clearTimeout(pending.timer);
    app.pendingRequests.delete(requestId);
    pending.resolve(value);
  }

  function handleHostMessage(event) {
    let message = event.data;
    if (typeof message === 'string') {
      try {
        message = JSON.parse(message);
      } catch (_) {
        return;
      }
    }
    if (!message || !message.type) {
      return;
    }

    switch (message.type) {
      case 'state':
        applyState(message.data);
        break;
      case 'toast':
        showToast(message.kind || 'info', message.title || '', message.message || '');
        break;
      case 'folderChosen':
        resolveRequest(message.requestId, { path: message.path || null, cancelled: Boolean(message.cancelled) });
        break;
      case 'clipboardText':
        resolveRequest(message.requestId, message.text || '');
        break;
      case 'clipboardSuggestion':
        if (message.url) {
          showClipboardSuggestion(message.url);
        }
        break;
      case 'settingsApplied':
        if (!message.ok && message.message) {
          showToast('error', '设置未保存', message.message);
        }
        break;
      case 'optionsConnections':
        app.optionsConnections = Number(message.value) || app.optionsConnections;
        break;
      default:
        break;
    }
  }

  function applyState(data) {
    if (!data || !Array.isArray(data.tasks)) {
      return;
    }
    const firstState = !app.hydrated;
    app.data = data;
    if (firstState) {
      app.hydrated = true;
      app.optionsConnections = Math.max(1, Math.min(32, Number(data.settings?.connections) || 16));
      document.body.classList.add('is-ready');
    }
    updateHeader();
    updateStats();
    updateNavCounts();
    renderTasks();
    updateAbout();
    if (app.hydrated && firstState) {
      requestAnimationFrame(() => document.body.classList.add('ui-entered'));
    }
  }

  function updateAbout() {
    if (!app.data) {
      return;
    }
    const version = app.data.version ? `v${app.data.version}` : 'v1.0.0';
    if (els.aboutVersion) {
      els.aboutVersion.textContent = version;
    }
    if (!app.settingsOpen) {
      // Keep a reference so opening the sheet starts from the latest values.
      app.settingsDraft = null;
    }
  }

  function updateHeader() {
    const meta = filterMeta[app.filter] || filterMeta.all;
    els.pageTitle.textContent = meta.title;
    els.pageSubtitle.textContent = meta.subtitle;
  }

  function taskCounts(tasks) {
    return tasks.reduce((acc, task) => {
      if (task.status === 'downloading' || task.status === 'probing') {
        acc.active += 1;
      } else if (task.status === 'queued') {
        acc.queued += 1;
      } else if (task.status === 'paused') {
        acc.paused += 1;
      } else if (task.status === 'completed') {
        acc.completed += 1;
      } else if (task.status === 'error') {
        acc.error += 1;
      }
      return acc;
    }, { active: 0, queued: 0, paused: 0, completed: 0, error: 0 });
  }

  function updateNavCounts() {
    if (!app.data) {
      return;
    }
    const counts = taskCounts(app.data.tasks);
    const values = {
      countAll: app.data.tasks.length,
      countActive: counts.active + counts.queued,
      countPaused: counts.paused,
      countCompleted: counts.completed,
      countError: counts.error,
    };
    Object.entries(values).forEach(([id, value]) => {
      const el = els[id];
      if (!el) {
        return;
      }
      el.textContent = String(value);
      el.dataset.empty = value === 0 ? 'true' : 'false';
    });
  }

  function updateStats() {
    if (!app.data) {
      return;
    }
    const tasks = app.data.tasks;
    const stats = app.data.stats || {};
    const settings = app.data.settings || {};
    const totalSpeed = Number(stats.totalSpeed) || 0;
    const activeConnections = Number(stats.runningConnections) || 0;
    const active = Number(stats.activeCount) || 0;
    const queued = Number(stats.queuedCount) || 0;
    const paused = Number(stats.pausedCount) || 0;
    const completed = Number(stats.completedCount) || 0;
    const limit = Math.max(1, Number(settings.maxConcurrent) || 3, active + queued);

    const rate = speedParts(totalSpeed);
    els.totalSpeedValue.textContent = rate.value;
    els.totalSpeedUnit.textContent = rate.unit;
    els.speedConnections.textContent = `${activeConnections} 个连接`;
    els.activeCount.textContent = String(active);
    els.queueLimit.textContent = String(settings.maxConcurrent || 3);
    els.queuedCount.textContent = String(queued);
    els.pausedCount.textContent = String(paused);
    els.sessionBytes.textContent = formatBytes(Number(app.data.sessionBytes) || 0);
    els.completionBadge.textContent = `+${completed} 完成`;
    const free = Number(stats.diskFreeBytes) || 0;
    const diskTotal = Number(stats.diskTotalBytes) || 0;
    els.diskFree.textContent = free > 0 ? formatBytes(free) : '—';
    els.sideDiskFree.textContent = free > 0 ? formatBytes(free) : '—';
    const usedPercent = diskTotal > 0 ? Math.max(0, Math.min(100, ((diskTotal - free) / diskTotal) * 100)) : 0;
    els.diskBar.style.width = `${usedPercent.toFixed(1)}%`;
    els.diskBar.parentElement.title = diskTotal > 0
      ? `已用 ${formatBytes(diskTotal - free)} / 共 ${formatBytes(diskTotal)}`
      : '磁盘信息暂不可用';

    els.liveBadge.classList.toggle('is-live', totalSpeed > 0 || active > 0);
    els.engineStatus.textContent = active > 0 ? '高速传输中' : '已就绪';
    els.engineDot.classList.toggle('is-busy', active > 0);
    els.sideConnections.textContent = String(activeConnections);
    const engineMax = Math.max(1, (Number(settings.maxConcurrent) || 3) * (Number(settings.connections) || 16));
    els.engineTrack.style.width = `${Math.min(100, (activeConnections / engineMax) * 100).toFixed(1)}%`;

    updateQueueVisual(active, limit, queued);
    updateSparkline(totalSpeed);
    els.clearCompletedBtn.disabled = completed === 0;
  }

  function updateQueueVisual(active, limit, queued) {
    if (els.queueVisual.childElementCount === 0) {
      for (let index = 0; index < 9; index += 1) {
        const bar = document.createElement('i');
        bar.style.height = `${8 + ((index * 7 + 5) % 24)}px`;
        els.queueVisual.appendChild(bar);
      }
    }
    const bars = Array.from(els.queueVisual.children);
    const filled = Math.round((Math.min(limit, active + queued) / limit) * bars.length);
    bars.forEach((bar, index) => {
      bar.classList.toggle('is-on', index < filled);
      const base = 8 + ((index * 11 + 4) % 22);
      bar.style.height = `${index < filled ? base + 7 : base}px`;
    });
  }

  function updateSparkline(speed) {
    const now = performance.now();
    if (now - app.lastSparkAt > 280) {
      app.speedHistory.push(Math.max(0, speed));
      if (app.speedHistory.length > 28) {
        app.speedHistory.shift();
      }
      app.lastSparkAt = now;
    } else {
      app.speedHistory[app.speedHistory.length - 1] = Math.max(0, speed);
    }

    const width = 168;
    const height = 50;
    const paddingTop = 5;
    const baseline = 48;
    const data = app.speedHistory.slice(-28);
    const max = Math.max(1, ...data);
    const step = width / Math.max(1, data.length - 1);
    const points = data.map((value, index) => ({
      x: index * step,
      y: baseline - (value / max) * (baseline - paddingTop),
    }));

    let d = '';
    points.forEach((point, index) => {
      if (index === 0) {
        d = `M ${point.x.toFixed(1)} ${point.y.toFixed(1)}`;
      } else {
        const previous = points[index - 1];
        const midX = ((previous.x + point.x) / 2).toFixed(1);
        d += ` C ${midX} ${previous.y.toFixed(1)}, ${midX} ${point.y.toFixed(1)}, ${point.x.toFixed(1)} ${point.y.toFixed(1)}`;
      }
    });
    if (!d) {
      d = `M 0 ${baseline} L ${width} ${baseline}`;
    }
    els.sparkLine.setAttribute('d', d);
    els.sparkArea.setAttribute('d', `${d} L ${width} ${height} L 0 ${height} Z`);
    const last = points[points.length - 1] || { x: width, y: baseline };
    els.sparkDot.setAttribute('cx', last.x.toFixed(1));
    els.sparkDot.setAttribute('cy', last.y.toFixed(1));
    els.sparkDot.style.opacity = speed > 0 ? '1' : '0.35';
  }

  function renderTasks() {
    if (!app.data) {
      return;
    }
    const query = app.search.trim().toLowerCase();
    let tasks = app.data.tasks.slice();

    if (app.filter === 'active') {
      tasks = tasks.filter((task) => ['downloading', 'probing', 'queued'].includes(task.status));
    } else if (app.filter !== 'all') {
      tasks = tasks.filter((task) => task.status === app.filter);
    }

    if (query) {
      tasks = tasks.filter((task) => {
        const haystack = `${task.fileName || ''} ${task.host || ''} ${task.url || ''} ${task.status || ''}`.toLowerCase();
        return haystack.includes(query);
      });
    }

    tasks.sort(compareTasks);

    const visibleIds = new Set(tasks.map((task) => task.id));
    for (const [id, element] of Array.from(app.cards.entries())) {
      if (!visibleIds.has(id)) {
        element.remove();
        app.cards.delete(id);
      }
    }

    const desired = tasks.map((task) => {
      let element = app.cards.get(task.id);
      if (!element) {
        element = createTaskCard(task);
        app.cards.set(task.id, element);
      }
      updateTaskCard(element, task);
      return element;
    });

    const current = Array.from(els.taskList.children);
    const orderChanged = current.length !== desired.length || current.some((element, index) => element !== desired[index]);
    if (orderChanged) {
      const fragment = document.createDocumentFragment();
      desired.forEach((element) => fragment.appendChild(element));
      els.taskList.appendChild(fragment);
    }

    els.listCount.textContent = String(tasks.length);
    const hasTasks = tasks.length > 0;
    els.emptyState.hidden = hasTasks;
    els.taskList.hidden = !hasTasks;
    updateEmptyState(tasks);

    if (app.filter === 'completed' && !hasTasks && query) {
      els.emptyCta.textContent = '清除搜索';
    }
  }

  function compareTasks(a, b) {
    const rank = { probing: 0, downloading: 0, queued: 1, paused: 2, error: 3, completed: 4 };
    const rankA = rank[a.status] ?? 5;
    const rankB = rank[b.status] ?? 5;
    if (rankA !== rankB) {
      return rankA - rankB;
    }
    if (rankA === 4) {
      return new Date(b.completedAt || b.createdAt).getTime() - new Date(a.completedAt || a.createdAt).getTime();
    }
    if (rankA === 0 || rankA === 1) {
      return new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime();
    }
    return new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime();
  }

  function createTaskCard(task) {
    const fragment = els.taskTemplate.content.cloneNode(true);
    const card = fragment.querySelector('.task-card');
    const refs = {
      fileVisual: $('.file-visual', card),
      fileIcon: $('.file-kind-icon use', card),
      fileExt: $('.file-ext', card),
      name: $('.task-name', card),
      statusChip: $('.status-chip', card),
      statusText: $('.status-chip span', card),
      toggle: $('[data-action="toggle"]', card),
      toggleIcon: $('[data-action="toggle"] use', card),
      folder: $('[data-action="folder"]', card),
      remove: $('[data-action="remove"]', card),
      host: $('[data-host]', card),
      detail: $('[data-detail]', card),
      messageSep: $('[data-message-sep]', card),
      message: $('[data-message]', card),
      progressTrack: $('[data-progress-track]', card),
      progressFill: $('[data-progress-fill]', card),
      progressText: $('[data-progress-text]', card),
      size: $('[data-size]', card),
      speed: $('[data-speed]', card),
      speedSep: $('[data-speed-sep]', card),
      eta: $('[data-eta]', card),
      etaSep: $('[data-eta-sep]', card),
      threads: $('[data-threads]', card),
      threadsText: $('[data-threads] span', card),
      openFile: $('.open-file-link', card),
    };
    card._refs = refs;
    card.dataset.taskId = task.id;

    return card;
  }

  function updateTaskCard(card, task) {
    const refs = card._refs;
    const knownStatuses = Object.keys(statusMeta);
    knownStatuses.forEach((status) => card.classList.remove(`status-${status}`));
    card.classList.add(`status-${statusMeta[task.status] ? task.status : 'queued'}`);

    const displayName = task.fileName || guessFileName(task.url) || '准备下载';
    const kind = fileKind(displayName, task.mimeType);
    const extension = extensionOf(displayName);

    refs.name.textContent = displayName;
    refs.name.title = task.status === 'completed' ? '点击打开文件' : task.url;
    refs.name.style.cursor = task.status === 'completed' ? 'pointer' : 'default';

    refs.fileVisual.dataset.kind = kind;
    refs.fileVisual.classList.toggle('is-probing', task.status === 'probing' || (!task.fileName && task.status !== 'completed'));
    refs.fileIcon.setAttribute('href', `#i-${kindMeta[kind].icon}`);
    refs.fileExt.textContent = extension || kindMeta[kind].label;

    const meta = statusMeta[task.status] || statusMeta.queued;
    refs.statusChip.dataset.status = task.status;
    refs.statusText.textContent = meta.label;

    refs.host.textContent = task.host || hostOf(task.url);
    refs.host.title = task.url || '';
    const folderName = leaf(task.saveDirectory) || '下载文件夹';
    refs.detail.textContent = taskDetail(task, folderName);
    refs.detail.title = task.saveDirectory || '';
    const hasError = task.status === 'error' && task.error;
    refs.message.hidden = !hasError;
    refs.messageSep.hidden = !hasError;
    if (hasError) {
      refs.message.textContent = task.error.replace(/\s+/g, ' ').trim();
      refs.message.title = task.error;
    }

    const percent = progressPercent(task);
    const indeterminate = task.status === 'probing' || (task.status === 'downloading' && !task.totalBytes);
    refs.progressTrack.classList.toggle('is-indeterminate', indeterminate);
    refs.progressFill.style.width = `${percent.toFixed(2)}%`;
    refs.progressText.textContent = progressLabel(task, percent);
    refs.progressTrack.setAttribute('aria-valuenow', String(Math.round(percent)));
    refs.progressTrack.setAttribute('role', 'progressbar');
    refs.progressTrack.setAttribute('aria-valuemin', '0');
    refs.progressTrack.setAttribute('aria-valuemax', '100');
    refs.progressTrack.setAttribute('aria-label', `${displayName} 下载进度`);

    refs.size.textContent = sizeLabel(task);
    const isActive = task.status === 'downloading';
    const speed = Number(task.speedBytesPerSecond) || 0;
    const showSpeed = isActive && speed > 0;
    refs.speed.hidden = !showSpeed;
    refs.speedSep.hidden = !showSpeed;
    if (showSpeed) {
      refs.speed.textContent = formatSpeed(speed);
    }
    const eta = Number(task.etaSeconds) || 0;
    const showEta = isActive && eta > 0 && task.totalBytes > 0;
    refs.eta.hidden = !showEta;
    refs.etaSep.hidden = !showEta;
    if (showEta) {
      refs.eta.textContent = `剩余 ${formatEta(eta)}`;
    }

    const showThreads = Boolean(task.supportsRanges) && ['downloading', 'paused', 'error', 'completed'].includes(task.status) && (task.totalSegments > 0 || task.status === 'completed');
    refs.threads.hidden = !showThreads;
    if (showThreads) {
      const connections = Number(task.activeConnections) || Number(task.requestedConnections) || 1;
      refs.threadsText.textContent = `${connections} 线程`;
      refs.threads.title = task.status === 'downloading'
        ? `${connections} 个连接正在传输`
        : `${task.requestedConnections || connections} 线程分段${task.isResumable && task.status !== 'completed' ? ' · 可断点续传' : ''}`;
    }

    const completed = task.status === 'completed';
    refs.openFile.hidden = !completed;

    const toggle = configureToggle(refs, task);
    refs.toggle.hidden = completed;
    if (!completed) {
      refs.toggle.title = toggle.title;
      refs.toggle.setAttribute('aria-label', toggle.label);
      refs.toggleIcon.setAttribute('href', toggle.icon);
    }
    refs.folder.title = completed ? '在文件夹中显示' : '打开下载文件夹';
    refs.remove.title = completed ? '移除记录' : '移除任务';

    card.title = `${displayName}\n${task.url}`;
  }

  function configureToggle(refs, task) {
    if (task.status === 'paused') {
      return { title: '继续下载', label: '继续下载', icon: '#i-play' };
    }
    if (task.status === 'error') {
      return { title: '重试下载', label: '重试下载', icon: '#i-rotate' };
    }
    return { title: '暂停下载', label: '暂停下载', icon: '#i-pause' };
  }

  function taskDetail(task, folderName) {
    if (task.status === 'probing') {
      return '正在获取文件信息';
    }
    if (task.status === 'queued') {
      return `排队中 · 保存至 ${folderName}`;
    }
    if (task.status === 'paused') {
      return task.isResumable ? `进度已保留 · ${folderName}` : `等待重新开始 · ${folderName}`;
    }
    if (task.status === 'completed') {
      return `保存至 ${folderName}`;
    }
    if (task.status === 'error') {
      return task.isResumable ? '可重试继续 · 断点已保留' : '等待重试';
    }
    return task.supportsRanges ? `多线程传输 · ${folderName}` : `单线程传输 · ${folderName}`;
  }

  function progressPercent(task) {
    if (task.status === 'completed') {
      return 100;
    }
    const total = Number(task.totalBytes) || 0;
    const downloaded = Number(task.downloadedBytes) || 0;
    if (total <= 0) {
      return 0;
    }
    return Math.max(0, Math.min(100, (downloaded / total) * 100));
  }

  function progressLabel(task, percent) {
    if (task.status === 'completed') {
      return '100%';
    }
    if (task.status === 'probing') {
      return '解析中';
    }
    if (task.status === 'queued') {
      return '等待';
    }
    if (!task.totalBytes && task.status === 'downloading') {
      return formatBytes(task.downloadedBytes || 0);
    }
    return percent >= 10 ? `${Math.floor(percent)}%` : `${percent.toFixed(1)}%`;
  }

  function sizeLabel(task) {
    const total = Number(task.totalBytes) || 0;
    const downloaded = Number(task.downloadedBytes) || 0;
    if (task.status === 'queued' && downloaded <= 0) {
      return total > 0 ? `${formatBytes(total)}` : '大小待解析';
    }
    if (task.status === 'probing') {
      return '大小待解析';
    }
    if (total > 0) {
      return downloaded > 0 || task.status === 'downloading'
        ? `${formatBytes(downloaded)} / ${formatBytes(total)}`
        : formatBytes(total);
    }
    return downloaded > 0 ? `${formatBytes(downloaded)} 已下载` : '大小未知';
  }

  function updateEmptyState(visibleTasks) {
    if (!app.data) {
      return;
    }
    const allTasks = app.data.tasks;
    const hasAny = allTasks.length > 0;
    const hasVisible = visibleTasks.length > 0;
    if (hasVisible) {
      return;
    }

    els.emptyTips.hidden = true;
    els.emptyCta.hidden = false;
    let title = '还没有下载任务';
    let text = '粘贴一个链接，剩下的交给流光。多线程加速，速度拉满。';
    let cta = '添加第一个下载';
    let action = 'focus';

    if (app.search.trim()) {
      title = '没有找到匹配的任务';
      text = `没有与“${app.search.trim()}”相关的下载，试试更短的关键词。`;
      cta = '清除搜索';
      action = 'clearSearch';
    } else if (!hasAny) {
      els.emptyTips.hidden = false;
      title = '还没有下载任务';
      text = '粘贴一个链接，剩下的交给流光。多线程加速，速度拉满。';
      cta = '添加第一个下载';
      action = 'focus';
    } else {
      const copy = {
        active: ['当前没有进行中的任务', '队列已经清空，所有任务都处理完了。', '查看全部任务'],
        paused: ['没有暂停的任务', '所有传输都在按计划进行。', '查看全部任务'],
        completed: ['还没有完成的任务', '下载完成的文件会整齐地收在这里。', '查看全部任务'],
        error: ['没有失败的任务', '网络通畅，队列也很健康。', '查看全部任务'],
        all: ['还没有下载任务', '粘贴一个链接，剩下的交给流光。', '添加第一个下载'],
      };
      const selected = copy[app.filter] || copy.all;
      title = selected[0];
      text = selected[1];
      cta = selected[2];
      action = app.filter === 'all' ? 'focus' : 'all';
    }

    els.emptyTitle.textContent = title;
    els.emptyText.textContent = text;
    els.emptyCta.innerHTML = `<svg class="icon"><use href="#${action === 'clearSearch' ? 'i-x' : 'i-link'}"/></svg>${escapeHtml(cta)}`;
    app.emptyAction = action;
  }

  function handleEmptyAction() {
    if (app.emptyAction === 'clearSearch') {
      clearSearch();
      return;
    }
    if (app.emptyAction === 'all') {
      setFilter('all');
      return;
    }
    els.urlInput.focus();
    els.main.scrollTo({ top: 0, behavior: 'smooth' });
  }

  function findTask(id) {
    return app.data?.tasks.find((task) => task.id === id);
  }

  function updateQuickState() {
    const value = els.urlInput.value;
    const hasText = splitUrls(value).length > 0;
    els.startBtn.disabled = !hasText;
    els.urlInput.style.height = 'auto';
    if (value.trim) {
      const target = Math.min(88, Math.max(23, els.urlInput.scrollHeight));
      els.urlInput.style.height = `${target}px`;
    }
  }

  function splitUrls(text) {
    return String(text || '')
      .split(/[\r\n\t ]+/)
      .map((part) => part.trim().replace(/^["']|["']$/g, ''))
      .filter((part) => part.length > 0);
  }

  function submitQuick() {
    const urls = splitUrls(els.urlInput.value);
    if (!urls.length) {
      shake(els.urlInput);
      els.urlInput.focus();
      return;
    }
    post('add', { urls });
    els.urlInput.value = '';
    updateQuickState();
    els.urlInput.focus();
  }

  function shake(element) {
    element.classList.remove('shake');
    void element.offsetWidth;
    element.classList.add('shake');
    window.setTimeout(() => element.classList.remove('shake'), 420);
  }

  function openOptions(prefill) {
    closeSettingsImmediate();
    app.lastFocused = document.activeElement;
    app.optionsOpen = true;
    app.currentOptionsFolder = app.data?.settings?.downloadDirectory || '';
    els.optionsUrls.value = typeof prefill === 'string' ? prefill : els.urlInput.value;
    els.optionsFolderText.textContent = app.currentOptionsFolder || '选择保存位置';
    els.optionsFolderText.title = app.currentOptionsFolder || '';
    els.optionsFilename.value = '';
    els.optionsReferer.value = '';
    els.optionsCookie.value = '';
    els.optionsHeaders.value = '';
    const preferred = Number(app.data?.settings?.connections) || app.optionsConnections || 16;
    app.optionsConnections = [8, 16, 24, 32].includes(preferred) ? preferred : 16;
    updateConnectionChips();
    updateOptionsNameState();
    els.optionsLayer.hidden = false;
    els.body.classList.add('overlay-open');
    document.querySelector('.advanced-settings.modal-advanced')?.removeAttribute('open');
    requestAnimationFrame(() => {
      els.optionsUrls.focus();
      app.activeLayer = els.optionsLayer;
    });
  }

  function closeOptions() {
    if (!app.optionsOpen) {
      return;
    }
    app.optionsOpen = false;
    els.optionsLayer.hidden = true;
    app.activeLayer = app.settingsOpen ? els.settingsSheet : null;
    els.body.classList.toggle('overlay-open', app.settingsOpen);
    if (app.lastFocused && typeof app.lastFocused.focus === 'function') {
      app.lastFocused.focus();
    }
  }

  function updateConnectionChips() {
    $$('#connectionChips button').forEach((button) => {
      button.classList.toggle('is-active', Number(button.dataset.connections) === app.optionsConnections);
    });
  }

  function updateOptionsNameState() {
    const count = splitUrls(els.optionsUrls.value).length;
    const multiple = count > 1;
    els.optionsFilename.disabled = multiple;
    els.optionsFilename.placeholder = multiple ? '多链接时自动命名' : '自动识别';
    els.optionsFilename.style.opacity = multiple ? '0.48' : '1';
  }

  async function submitOptions() {
    const urls = splitUrls(els.optionsUrls.value);
    if (!urls.length) {
      els.optionsUrls.focus();
      shake(els.optionsUrls);
      showToast('info', '还没有链接', '请先粘贴至少一条 HTTP / HTTPS 下载链接。');
      return;
    }
    const options = {
      saveDirectory: app.currentOptionsFolder || undefined,
      fileName: urls.length === 1 ? els.optionsFilename.value.trim() || undefined : undefined,
      connections: app.optionsConnections,
      referer: els.optionsReferer.value.trim() || undefined,
      cookie: els.optionsCookie.value.trim() || undefined,
      customHeaders: els.optionsHeaders.value.trim() || undefined,
    };
    post('add', { urls, options });
    els.optionsUrls.value = '';
    els.urlInput.value = '';
    updateQuickState();
    closeOptions();
  }

  async function chooseFolder(initial) {
    if (!webview) {
      showToast('info', '桌面应用功能', '文件夹选择仅在桌面应用内可用。');
      return null;
    }
    try {
      const result = await requestHost('pickFolder', { initial: initial || '' });
      return result?.path || null;
    } catch (error) {
      showToast('error', '无法打开文件夹选择器', error.message || '请稍后再试。');
      return null;
    }
  }

  async function pasteFromClipboard() {
    let text = '';
    if (webview) {
      try {
        text = await requestHost('getClipboard', {}, 5000);
      } catch (_) {
        text = '';
      }
    } else if (navigator.clipboard?.readText) {
      try {
        text = await navigator.clipboard.readText();
      } catch (_) {
        text = '';
      }
    }
    if (!text || !text.trim()) {
      showToast('info', '剪贴板是空的', '复制一个下载链接后再试一次。');
      return;
    }
    const existing = els.urlInput.value.trim();
    els.urlInput.value = existing ? `${existing}\n${text.trim()}` : text.trim();
    updateQuickState();
    els.urlInput.focus();
    hideClipboardSuggestion();
  }

  function showClipboardSuggestion(url) {
    if (!url || app.clipboardSuggestion === url) {
      return;
    }
    if (app.data?.tasks.some((task) => task.url === url)) {
      return;
    }
    app.clipboardSuggestion = url;
    els.clipboardChipUrl.textContent = url;
    els.clipboardChipUrl.title = url;
    els.clipboardChip.hidden = false;
  }

  function hideClipboardSuggestion() {
    app.clipboardSuggestion = '';
    els.clipboardChip.hidden = true;
  }

  function useClipboardSuggestion() {
    if (!app.clipboardSuggestion) {
      return;
    }
    els.urlInput.value = app.clipboardSuggestion;
    updateQuickState();
    hideClipboardSuggestion();
    els.urlInput.focus();
  }

  function setFilter(filter) {
    if (!filterMeta[filter]) {
      return;
    }
    app.filter = filter;
    $$('.nav-item').forEach((item) => item.classList.toggle('is-active', item.dataset.filter === filter));
    updateHeader();
    renderTasks();
    els.main.scrollTo({ top: 0, behavior: 'smooth' });
  }

  function clearSearch() {
    app.search = '';
    els.searchInput.value = '';
    els.searchClear.hidden = true;
    renderTasks();
  }

  function applySearch() {
    app.search = els.searchInput.value;
    els.searchClear.hidden = !app.search;
    renderTasks();
  }

  /* ---------- Settings ---------- */

  function draftFromState() {
    const settings = app.data?.settings || {};
    return {
      downloadDirectory: settings.downloadDirectory || '',
      connections: Number(settings.connections) || 16,
      maxConcurrent: Number(settings.maxConcurrent) || 3,
      watchClipboard: settings.watchClipboard !== false,
      completionSound: settings.completionSound !== false,
      allowInvalidCertificate: Boolean(settings.allowInvalidCertificate),
      userAgent: settings.userAgent || defaultUserAgent,
    };
  }

  function openSettings() {
    if (app.settingsOpen) {
      return;
    }
    app.lastFocused = document.activeElement;
    app.settingsDraft = draftFromState();
    app.settingsOpen = true;
    fillSettingsForm();
    updateThemeSegments();
    els.settingsSheet.inert = false;
    els.settingsScrim.hidden = false;
    els.settingsSheet.setAttribute('aria-hidden', 'false');
    els.body.classList.add('overlay-open');
    requestAnimationFrame(() => {
      els.settingsScrim.classList.add('is-visible');
      els.settingsSheet.classList.add('is-open');
      app.activeLayer = els.settingsSheet;
      els.closeSettingsBtn.focus();
    });
  }

  function closeSettings(immediate = false) {
    if (!app.settingsOpen) {
      return;
    }
    flushSettingsSave();
    app.settingsOpen = false;
    els.settingsSheet.classList.remove('is-open');
    els.settingsScrim.classList.remove('is-visible');
    els.settingsSheet.setAttribute('aria-hidden', 'true');
    els.settingsSheet.inert = true;
    app.activeLayer = app.optionsOpen ? els.optionsLayer : null;
    els.body.classList.toggle('overlay-open', app.optionsOpen);
    const finish = () => {
      if (!app.settingsOpen) {
        els.settingsScrim.hidden = true;
      }
    };
    if (immediate) {
      finish();
    } else {
      window.setTimeout(finish, 300);
    }
    if (!app.optionsOpen && app.lastFocused && typeof app.lastFocused.focus === 'function') {
      app.lastFocused.focus();
    }
  }

  function closeSettingsImmediate() {
    if (!app.settingsOpen) {
      return;
    }
    app.settingsOpen = false;
    els.settingsSheet.classList.remove('is-open');
    els.settingsScrim.classList.remove('is-visible');
    els.settingsScrim.hidden = true;
    els.settingsSheet.setAttribute('aria-hidden', 'true');
    els.settingsSheet.inert = true;
    app.activeLayer = null;
    els.body.classList.remove('overlay-open');
  }

  function fillSettingsForm() {
    const draft = app.settingsDraft || draftFromState();
    els.settingsFolderText.textContent = draft.downloadDirectory || '选择保存位置';
    els.settingsFolderText.title = draft.downloadDirectory || '';
    els.connectionsRange.value = String(draft.connections);
    els.connectionsValue.textContent = String(draft.connections);
    paintRange(els.connectionsRange);
    els.concurrentRange.value = String(draft.maxConcurrent);
    els.concurrentValue.textContent = String(draft.maxConcurrent);
    paintRange(els.concurrentRange);
    els.soundToggle.checked = Boolean(draft.completionSound);
    els.clipboardToggle.checked = Boolean(draft.watchClipboard);
    els.insecureToggle.checked = Boolean(draft.allowInvalidCertificate);
    els.uaInput.value = draft.userAgent || defaultUserAgent;
    updateThemeSegments();
  }

  function paintRange(input) {
    const min = Number(input.min) || 0;
    const max = Number(input.max) || 100;
    const value = Number(input.value) || 0;
    const percent = max === min ? 0 : ((value - min) / (max - min)) * 100;
    input.style.setProperty('--fill', `${percent}%`);
  }

  function scheduleSettingsSave(delay = 450) {
    if (!app.settingsOpen) {
      return;
    }
    window.clearTimeout(app.settingsSaveTimer);
    app.settingsSaveTimer = window.setTimeout(flushSettingsSave, delay);
  }

  function flushSettingsSave() {
    window.clearTimeout(app.settingsSaveTimer);
    app.settingsSaveTimer = 0;
    if (!app.settingsOpen || !app.settingsDraft) {
      return;
    }
    const settings = {
      downloadDirectory: els.settingsFolderText.title || app.settingsDraft.downloadDirectory,
      connections: Number(els.connectionsRange.value),
      maxConcurrent: Number(els.concurrentRange.value),
      watchClipboard: els.clipboardToggle.checked,
      completionSound: els.soundToggle.checked,
      allowInvalidCertificate: els.insecureToggle.checked,
      userAgent: els.uaInput.value.trim() || defaultUserAgent,
    };
    app.settingsDraft = { ...settings };
    post('updateSettings', { settings });
  }

  function updateThemeSegments() {
    $$('.theme-segments button').forEach((button) => {
      button.classList.toggle('is-active', button.dataset.themeChoice === app.themeMode);
    });
  }

  function handleSettingsInput(event) {
    const target = event.target;
    if (target === els.connectionsRange) {
      els.connectionsValue.textContent = els.connectionsRange.value;
      paintRange(els.connectionsRange);
      scheduleSettingsSave();
    } else if (target === els.concurrentRange) {
      els.concurrentValue.textContent = els.concurrentRange.value;
      paintRange(els.concurrentRange);
      scheduleSettingsSave();
    } else if (target === els.uaInput) {
      scheduleSettingsSave(700);
    }
  }

  function handleSettingsChange(event) {
    const target = event.target;
    if (target === els.soundToggle || target === els.clipboardToggle || target === els.insecureToggle) {
      scheduleSettingsSave(50);
    } else if (target === els.uaInput) {
      scheduleSettingsSave(50);
    }
  }

  function chooseSettingsFolder() {
    chooseFolder(app.settingsDraft?.downloadDirectory || app.data?.settings?.downloadDirectory || '').then((path) => {
      if (!path) {
        return;
      }
      app.settingsDraft = { ...(app.settingsDraft || draftFromState()), downloadDirectory: path };
      els.settingsFolderText.textContent = path;
      els.settingsFolderText.title = path;
      scheduleSettingsSave(50);
    });
  }

  function chooseOptionsFolder() {
    chooseFolder(app.currentOptionsFolder).then((path) => {
      if (!path) {
        return;
      }
      app.currentOptionsFolder = path;
      els.optionsFolderText.textContent = path;
      els.optionsFolderText.title = path;
    });
  }

  function openDownloadFolder() {
    post('openDownloadFolder', { directory: app.settingsDraft?.downloadDirectory || app.data?.settings?.downloadDirectory });
  }

  /* ---------- Theme ---------- */

  function resolveTheme(mode) {
    if (mode === 'light' || mode === 'dark') {
      return mode;
    }
    return matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
  }

  function applyTheme(mode, persist = true) {
    const chosen = validThemes.has(mode) ? mode : 'dark';
    app.themeMode = chosen;
    app.effectiveTheme = resolveTheme(chosen);
    document.documentElement.dataset.theme = app.effectiveTheme;
    document.documentElement.dataset.themeMode = chosen;
    if (persist) {
      try {
        localStorage.setItem('lg-theme', chosen);
      } catch (_) {
        // Local storage can be restricted; theming still works for this session.
      }
    }
    updateThemeSegments();
    els.themeToggleBtn.setAttribute('aria-label', `切换为${app.effectiveTheme === 'dark' ? '浅色' : '深色'}模式`);
    els.themeToggleBtn.title = `切换为${app.effectiveTheme === 'dark' ? '浅色' : '深色'}模式（Ctrl Shift L）`;
    post('theme', { mode: chosen, effective: app.effectiveTheme });
  }

  function toggleTheme() {
    applyTheme(app.effectiveTheme === 'dark' ? 'light' : 'dark');
  }

  function initTheme() {
    let stored = 'dark';
    try {
      stored = localStorage.getItem('lg-theme') || 'dark';
    } catch (_) {
      stored = 'dark';
    }
    const forced = new URLSearchParams(location.search).get('theme');
    app.themeMode = validThemes.has(forced) ? forced : (validThemes.has(stored) ? stored : 'dark');
    applyTheme(app.themeMode, !forced);
    const media = matchMedia('(prefers-color-scheme: light)');
    media.addEventListener('change', () => {
      if (app.themeMode === 'system') {
        app.effectiveTheme = resolveTheme('system');
        document.documentElement.dataset.theme = app.effectiveTheme;
        post('theme', { mode: 'system', effective: app.effectiveTheme });
        updateThemeSegments();
      }
    });
  }

  /* ---------- Toasts ---------- */

  function showToast(kind = 'info', title = '', message = '', duration = 4400) {
    if (!title && message) {
      title = message;
      message = '';
    }
    const toast = document.createElement('div');
    toast.className = `toast ${['success', 'error', 'info'].includes(kind) ? kind : 'info'}`;
    const icon = kind === 'success' ? 'i-check-circle' : kind === 'error' ? 'i-alert' : 'i-info';
    toast.innerHTML = `
      <div class="toast-icon"><svg class="icon"><use href="#${icon}"/></svg></div>
      <div class="toast-copy"><strong>${escapeHtml(title || '提示')}</strong>${message ? `<span>${escapeHtml(message)}</span>` : ''}</div>
      <button class="toast-close" type="button" aria-label="关闭提示"><svg class="icon"><use href="#i-x"/></svg></button>
    `;
    const close = () => {
      if (!toast.isConnected) {
        return;
      }
      toast.classList.add('is-leaving');
      window.setTimeout(() => toast.remove(), 240);
    };
    $('.toast-close', toast).addEventListener('click', close);
    els.toastStack.appendChild(toast);
    while (els.toastStack.childElementCount > 4) {
      els.toastStack.firstElementChild?.remove();
    }
    window.setTimeout(close, duration);
  }

  function escapeHtml(value) {
    return String(value)
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#039;');
  }

  /* ---------- Confirm ---------- */

  function askConfirm({ title, message, confirmText = '确认', danger = true }) {
    if (app.confirmResolver) {
      app.confirmResolver(false);
      app.confirmResolver = null;
    }
    els.confirmTitle.textContent = title;
    els.confirmMessage.textContent = message;
    els.confirmOkBtn.textContent = confirmText;
    els.confirmOkBtn.classList.toggle('danger-button', danger);
    els.confirmLayer.hidden = false;
    app.activeLayer = els.confirmLayer;
    return new Promise((resolve) => {
      app.confirmResolver = resolve;
      requestAnimationFrame(() => els.confirmOkBtn.focus());
    });
  }

  function settleConfirm(value) {
    if (!app.confirmResolver) {
      return;
    }
    const resolver = app.confirmResolver;
    app.confirmResolver = null;
    els.confirmLayer.hidden = true;
    app.activeLayer = app.optionsOpen ? els.optionsLayer : app.settingsOpen ? els.settingsSheet : null;
    resolver(value);
  }

  async function requestRemoveTask(task) {
    if (!task) {
      return;
    }
    const completed = task.status === 'completed';
    const ok = await askConfirm({
      title: completed ? '移除任务记录？' : '移除这个下载任务？',
      message: completed
        ? '记录会从列表中移除，已经下载好的文件会完整保留。'
        : '正在进行的传输会被停止，未完成的临时文件会被删除。',
      confirmText: completed ? '移除记录' : '移除任务',
    });
    if (!ok) {
      return;
    }
    post('remove', { id: task.id });
  }

  async function clearCompletedTasks() {
    const completed = app.data?.stats?.completedCount || 0;
    if (!completed) {
      return;
    }
    const ok = await askConfirm({
      title: '清除已完成记录？',
      message: `将从列表中移除 ${completed} 条已完成记录，文件不会被删除。`,
      confirmText: '清除记录',
    });
    if (ok) {
      post('clearCompleted');
    }
  }

  /* ---------- Task events ---------- */

  function handleTaskAction(event) {
    const button = event.target.closest('[data-action]');
    if (!button || !els.taskList.contains(button)) {
      return;
    }
    const card = button.closest('.task-card');
    const task = findTask(card?.dataset.taskId);
    if (!task) {
      return;
    }
    const action = button.dataset.action;
    event.stopPropagation();
    if (action === 'toggle') {
      if (task.status === 'paused' || task.status === 'error') {
        post('resume', { id: task.id });
      } else if (task.status === 'completed') {
        post('openFile', { id: task.id });
      } else {
        post('pause', { id: task.id });
      }
      return;
    }
    if (action === 'folder') {
      post('reveal', { id: task.id });
      return;
    }
    if (action === 'remove') {
      requestRemoveTask(task);
      return;
    }
    if (action === 'primary' && task.status === 'completed') {
      post('openFile', { id: task.id });
    }
  }

  /* ---------- Keyboard / focus ---------- */

  function focusableElements(container) {
    return $$('a[href], button:not([disabled]), input:not([disabled]), textarea:not([disabled]), select:not([disabled]), summary, [tabindex]:not([tabindex="-1"])', container)
      .filter((element) => element.offsetParent !== null);
  }

  function trapFocus(event) {
    if (event.key !== 'Tab' || !app.activeLayer) {
      return;
    }
    const focusable = focusableElements(app.activeLayer);
    if (!focusable.length) {
      return;
    }
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  function onKeyDown(event) {
    const mod = event.ctrlKey || event.metaKey;

    if (event.key === 'Escape') {
      if (!els.confirmLayer.hidden) {
        settleConfirm(false);
        return;
      }
      if (app.optionsOpen) {
        closeOptions();
        return;
      }
      if (app.settingsOpen) {
        closeSettings();
        return;
      }
      if (document.activeElement === els.searchInput && els.searchInput.value) {
        clearSearch();
        return;
      }
      if (document.activeElement === els.searchInput) {
        els.searchInput.blur();
      }
      return;
    }

    trapFocus(event);

    if (!els.confirmLayer.hidden) {
      return;
    }
    if (app.optionsOpen) {
      if (mod && event.key === 'Enter') {
        event.preventDefault();
        submitOptions();
      }
      return;
    }
    if (app.settingsOpen) {
      return;
    }

    if (mod && !event.altKey && event.key.toLowerCase() === 'n') {
      event.preventDefault();
      openOptions();
      return;
    }
    if (mod && event.key === ',') {
      event.preventDefault();
      openSettings();
      return;
    }
    if (mod && !event.shiftKey && event.key.toLowerCase() === 'f') {
      event.preventDefault();
      els.searchInput.focus();
      els.searchInput.select();
      return;
    }
    if (mod && !event.shiftKey && event.key.toLowerCase() === 'l') {
      event.preventDefault();
      els.urlInput.focus();
      return;
    }
    if (mod && event.shiftKey && event.key.toLowerCase() === 'l') {
      event.preventDefault();
      toggleTheme();
      return;
    }
    if (event.key === 'Enter' && document.activeElement === els.urlInput) {
      if (event.shiftKey) {
        return;
      }
      event.preventDefault();
      submitQuick();
    }
  }

  /* ---------- Formatting ---------- */

  function formatBytes(value) {
    const bytes = Number(value);
    if (!Number.isFinite(bytes) || bytes <= 0) {
      return '0 B';
    }
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    const exponent = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
    const amount = bytes / Math.pow(1024, exponent);
    let digits = 0;
    if (exponent > 0) {
      digits = amount >= 100 ? 0 : amount >= 10 ? 1 : 2;
    }
    return `${trimNumber(amount, digits)} ${units[exponent]}`;
  }

  function speedParts(bytesPerSecond) {
    const value = Number(bytesPerSecond) || 0;
    if (value <= 0) {
      return { value: '0.00', unit: 'MB/s' };
    }
    if (value >= 1024 * 1024 * 1024) {
      return { value: trimNumber(value / (1024 * 1024 * 1024), value < 10 ? 2 : 1), unit: 'GB/s' };
    }
    if (value >= 1024 * 1024) {
      const mb = value / (1024 * 1024);
      return { value: trimNumber(mb, mb >= 10 ? 1 : 2), unit: 'MB/s' };
    }
    if (value >= 1024) {
      return { value: trimNumber(value / 1024, 0), unit: 'KB/s' };
    }
    return { value: trimNumber(value, 0), unit: 'B/s' };
  }

  function formatSpeed(bytesPerSecond) {
    const parts = speedParts(bytesPerSecond);
    return `${parts.value} ${parts.unit}`;
  }

  function trimNumber(value, digits) {
    const fixed = Number(value).toFixed(digits);
    if (!fixed.includes('.')) {
      return fixed;
    }
    return fixed.replace(/0+$/, '').replace(/\.$/, '');
  }

  function formatEta(seconds) {
    const value = Math.max(0, Math.round(Number(seconds) || 0));
    if (value <= 1) {
      return '即将完成';
    }
    if (value < 60) {
      return `${value} 秒`;
    }
    if (value < 3600) {
      return `${Math.floor(value / 60)} 分 ${value % 60} 秒`;
    }
    if (value < 86400) {
      return `${Math.floor(value / 3600)} 小时 ${Math.floor((value % 3600) / 60)} 分`;
    }
    return `${Math.floor(value / 86400)} 天 ${Math.floor((value % 86400) / 3600)} 小时`;
  }

  function guessFileName(url) {
    try {
      const parsed = new URL(url);
      const last = decodeURIComponent(parsed.pathname.split('/').filter(Boolean).pop() || '');
      return last || parsed.hostname;
    } catch (_) {
      return '';
    }
  }

  function hostOf(url) {
    try {
      return new URL(url).host;
    } catch (_) {
      return url || '未知来源';
    }
  }

  function extensionOf(name) {
    const base = String(name || '').split(/[?#]/)[0];
    const lastDot = base.lastIndexOf('.');
    if (lastDot <= 0 || lastDot === base.length - 1) {
      return '';
    }
    const extension = base.slice(lastDot + 1).toLowerCase();
    if (/^[a-z0-9]{1,8}$/.test(extension)) {
      return extension.toUpperCase();
    }
    return '';
  }

  function fileKind(name, mime) {
    const extension = extensionOf(name).toLowerCase();
    for (const [kind, extensions] of Object.entries(extensionKinds)) {
      if (extensions.has(extension)) {
        return kind;
      }
    }
    if (mime) {
      const clean = String(mime).toLowerCase();
      if (clean.startsWith('video/')) return 'video';
      if (clean.startsWith('audio/')) return 'audio';
      if (clean.startsWith('image/')) return 'image';
      if (clean.includes('zip') || clean.includes('rar') || clean.includes('7z') || clean.includes('compressed')) return 'archive';
      if (clean.includes('pdf') || clean.includes('word') || clean.includes('excel') || clean.includes('powerpoint') || clean.startsWith('text/')) return 'document';
      if (clean.includes('javascript') || clean.includes('json') || clean.includes('xml')) return 'code';
      if (clean.includes('msdownload') || clean.includes('android.package')) return 'app';
    }
    return 'generic';
  }

  function leaf(path) {
    if (!path) {
      return '';
    }
    const parts = String(path).split(/[\\/]/).filter(Boolean);
    return parts[parts.length - 1] || path;
  }

  /* ---------- Wire-up ---------- */

  function bindEvents() {
    $$('.nav-item').forEach((item) => {
      item.addEventListener('click', () => setFilter(item.dataset.filter));
    });
    els.themeToggleBtn.addEventListener('click', toggleTheme);
    els.openSettingsBtn.addEventListener('click', openSettings);
    els.newDownloadBtn.addEventListener('click', () => openOptions());
    els.advancedBtn.addEventListener('click', () => openOptions());
    els.emptyCta.addEventListener('click', handleEmptyAction);
    els.clearCompletedBtn.addEventListener('click', clearCompletedTasks);
    els.taskList.addEventListener('click', handleTaskAction);

    els.urlInput.addEventListener('input', updateQuickState);
    els.urlInput.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
        event.preventDefault();
        submitQuick();
      }
    });
    els.startBtn.addEventListener('click', submitQuick);

    els.pasteBtn.addEventListener('click', pasteFromClipboard);
    els.clipboardUse.addEventListener('click', useClipboardSuggestion);
    els.clipboardDismiss.addEventListener('click', hideClipboardSuggestion);

    els.searchInput.addEventListener('input', applySearch);
    els.searchClear.addEventListener('click', () => {
      clearSearch();
      els.searchInput.focus();
    });
    els.searchInput.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        clearSearch();
      }
    });

    els.closeSettingsBtn.addEventListener('click', () => closeSettings());
    els.settingsScrim.addEventListener('click', () => closeSettings());
    els.settingsSheet.addEventListener('input', handleSettingsInput);
    els.settingsSheet.addEventListener('change', handleSettingsChange);
    els.settingsChooseFolder.addEventListener('click', chooseSettingsFolder);
    els.settingsOpenFolder.addEventListener('click', openDownloadFolder);
    els.resetUaBtn.addEventListener('click', () => {
      els.uaInput.value = defaultUserAgent;
      scheduleSettingsSave(50);
    });
    $$('.theme-segments button').forEach((button) => {
      button.addEventListener('click', () => applyTheme(button.dataset.themeChoice));
    });

    $$('[data-close-options]').forEach((element) => element.addEventListener('click', closeOptions));
    $('#closeOptionsBtn').addEventListener('click', closeOptions);
    $('#cancelOptionsBtn').addEventListener('click', closeOptions);
    els.optionsUrls.addEventListener('input', updateOptionsNameState);
    els.submitOptionsBtn.addEventListener('click', submitOptions);
    els.chooseOptionsFolder.addEventListener('click', chooseOptionsFolder);
    els.connectionChips.addEventListener('click', (event) => {
      const button = event.target.closest('button[data-connections]');
      if (!button) return;
      app.optionsConnections = Number(button.dataset.connections);
      updateConnectionChips();
    });

    $$('[data-close-confirm]').forEach((element) => element.addEventListener('click', () => settleConfirm(false)));
    els.confirmCancelBtn.addEventListener('click', () => settleConfirm(false));
    els.confirmOkBtn.addEventListener('click', () => settleConfirm(true));

    document.addEventListener('keydown', onKeyDown);

    // Drag and drop links onto the window.
    let dragDepth = 0;
    window.addEventListener('dragenter', (event) => {
      if (!event.dataTransfer?.types?.includes('text/plain') && !event.dataTransfer?.types?.includes('text/uri-list')) {
        return;
      }
      event.preventDefault();
      dragDepth += 1;
      els.dropOverlay.hidden = false;
    });
    window.addEventListener('dragover', (event) => {
      event.preventDefault();
      if (event.dataTransfer) {
        event.dataTransfer.dropEffect = 'copy';
      }
    });
    window.addEventListener('dragleave', (event) => {
      event.preventDefault();
      dragDepth = Math.max(0, dragDepth - 1);
      if (dragDepth === 0) {
        els.dropOverlay.hidden = true;
      }
    });
    window.addEventListener('drop', (event) => {
      event.preventDefault();
      dragDepth = 0;
      els.dropOverlay.hidden = true;
      const text = event.dataTransfer?.getData('text/plain') || event.dataTransfer?.getData('text/uri-list') || '';
      const urls = splitUrls(text);
      if (!urls.length) {
        showToast('info', '没有识别到链接', '请拖入 HTTP / HTTPS 下载链接。');
        return;
      }
      post('add', { urls });
    });

    window.addEventListener('online', () => showToast('success', '网络已恢复', '下载队列可以继续了。'));
    window.addEventListener('offline', () => showToast('error', '网络连接已断开', '恢复网络后，任务可以继续。'));

    els.settingsSheet.inert = true;
  }

  function hydrateTheme() {
    initTheme();
    updateThemeSegments();
  }

  function init() {
    cacheElements();
    bindEvents();
    hydrateTheme();
    updateQuickState();
    updateSparkline(0);

    if (webview) {
      webview.addEventListener('message', handleHostMessage);
      post('ready');
    } else {
      // A friendly no-bridge state for development previews.
      document.body.classList.add('is-ready');
      setTimeout(() => {
        if (!app.data) {
          showToast('info', '预览模式', '宿主桥接不可用，当前界面正在独立预览。');
        }
      }, 500);
    }
  }

  init();
})();
