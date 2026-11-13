'use strict';

(function () {
  const DATA_REFRESH_MS = 30000;
  const STATUS_REFRESH_MS = 10000;

  const state = {
    range: '90',
    scale: 'day',
    data: null,
    status: null,
    dataLoadedAt: 0,
    statusLoadedAt: 0,
    dataLoading: false,
    statusLoading: false,
    nextDataRefreshAt: Date.now() + DATA_REFRESH_MS,
    dataError: null,
    statusError: null
  };

  const el = {
    livePill: document.getElementById('livePill'),
    livePillText: document.getElementById('livePillText'),
    clock: document.getElementById('clock'),
    refreshBtn: document.getElementById('refreshBtn'),
    notice: document.getElementById('notice'),
    noticeText: document.getElementById('noticeText'),

    statusCard: document.getElementById('statusCard'),
    statusIconSvg: document.getElementById('statusIconSvg'),
    statusTitle: document.getElementById('statusTitle'),
    statusDetail: document.getElementById('statusDetail'),
    latencyBadge: document.getElementById('latencyBadge'),
    hostBadge: document.getElementById('hostBadge'),
    outageBadge: document.getElementById('outageBadge'),
    trackerState: document.getElementById('trackerState'),
    statusFileState: document.getElementById('statusFileState'),
    lastOutageState: document.getElementById('lastOutageState'),

    kpiAvailability: document.getElementById('kpiAvailability'),
    kpiAvailabilitySub: document.getElementById('kpiAvailabilitySub'),
    kpiOutages: document.getElementById('kpiOutages'),
    kpiOutagesSub: document.getElementById('kpiOutagesSub'),
    kpiDowntime: document.getElementById('kpiDowntime'),
    kpiDowntimeSub: document.getElementById('kpiDowntimeSub'),
    kpiLongest: document.getElementById('kpiLongest'),
    kpiLongestSub: document.getElementById('kpiLongestSub'),
    kpiAverage: document.getElementById('kpiAverage'),
    kpiAverageSub: document.getElementById('kpiAverageSub'),
    kpiLast: document.getElementById('kpiLast'),
    kpiLastSub: document.getElementById('kpiLastSub'),

    rangeControl: document.getElementById('rangeControl'),
    scaleControl: document.getElementById('scaleControl'),
    availabilitySub: document.getElementById('availabilitySub'),
    availabilityEmpty: document.getElementById('availabilityEmpty'),
    incidentsSub: document.getElementById('incidentsSub'),
    incidentsEmpty: document.getElementById('incidentsEmpty'),
    recentList: document.getElementById('recentList'),
    recentCount: document.getElementById('recentCount'),
    sourcePath: document.getElementById('sourcePath'),
    refreshInfo: document.getElementById('refreshInfo')
  };

  const ICONS = {
    online: `
      <path d="M2.5 9.5a15 15 0 0 1 19 0"/>
      <path d="M5.5 13a10.5 10.5 0 0 1 13 0"/>
      <path d="M8.5 16.5a6 6 0 0 1 7 0"/>
      <circle cx="12" cy="20" r="1.15" fill="currentColor" stroke="none"/>`,
    offline: `
      <path d="M2.5 9.5a15 15 0 0 1 4.2-2.8"/>
      <path d="M15.7 6.9a15 15 0 0 1 5.8 2.6"/>
      <path d="M5.5 13a10.5 10.5 0 0 1 3.4-1.9"/>
      <path d="M15.1 11.3a10.5 10.5 0 0 1 3.4 1.7"/>
      <path d="M8.5 16.5a6 6 0 0 1 7 0"/>
      <circle cx="12" cy="20" r="1.15" fill="currentColor" stroke="none"/>
      <path d="m3 3 18 18"/>`,
    unknown: `
      <circle cx="12" cy="12" r="9"/>
      <path d="M9.6 9.1a2.5 2.5 0 0 1 4.9.7c0 1.7-2.5 2-2.5 3.5"/>
      <circle cx="12" cy="17" r=".85" fill="currentColor" stroke="none"/>`
  };

  const CHART_COLORS = {
    grid: 'rgba(148, 163, 184, 0.075)',
    tick: '#7587a3',
    tickSoft: 'rgba(131, 148, 174, 0.95)',
    green: '#35e0a1',
    greenSoft: 'rgba(53, 224, 161, 0.14)',
    red: 'rgba(255, 95, 124, 0.78)',
    redHover: 'rgba(255, 95, 124, 1)',
    grayBar: 'rgba(148, 163, 184, 0.16)',
    grayBarHover: 'rgba(148, 163, 184, 0.3)'
  };

  let availabilityChart = null;
  let incidentsChart = null;
  let dataRequestSeq = 0;
  let statusRequestSeq = 0;

  function fmtInt(value) {
    return Number(value || 0).toLocaleString();
  }

  function fmtPct(value) {
    if (value === null || value === undefined || !Number.isFinite(Number(value))) return '—';
    const number = Number(value);
    if (number >= 99.999) return '100%';
    return `${number.toFixed(2)}%`;
  }

  function formatDuration(seconds) {
    const total = Math.max(0, Math.round(Number(seconds) || 0));
    if (total < 60) return `${total}s`;

    const minutes = Math.floor(total / 60);
    const secs = total % 60;
    if (minutes < 60) return secs ? `${minutes}m ${secs}s` : `${minutes}m`;

    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    if (hours < 24) return mins ? `${hours}h ${mins}m` : `${hours}h`;

    const days = Math.floor(hours / 24);
    const hrs = hours % 24;
    if (days < 365) return hrs ? `${days}d ${hrs}h` : `${days}d`;

    const years = Math.floor(days / 365);
    const remaining = days % 365;
    return remaining ? `${years}y ${remaining}d` : `${years}y`;
  }

  function formatRelative(ms) {
    if (!ms || !Number.isFinite(Number(ms))) return '—';
    const diff = Date.now() - Number(ms);
    if (diff < 0) return 'just now';
    if (diff < 30000) return 'just now';

    const seconds = Math.floor(diff / 1000);
    if (seconds < 60) return `${seconds}s ago`;

    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes}m ago`;

    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;

    const days = Math.floor(hours / 24);
    if (days < 30) return `${days}d ago`;

    return formatDate(ms);
  }

  function formatDate(ms) {
    if (!ms) return '—';
    return new Date(Number(ms)).toLocaleDateString(undefined, {
      day: 'numeric',
      month: 'short',
      year: 'numeric'
    });
  }

  function formatDateTime(ms) {
    if (!ms) return '—';
    return new Date(Number(ms)).toLocaleString(undefined, {
      day: 'numeric',
      month: 'short',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
  }

  function formatTime(ms) {
    if (!ms) return '—';
    return new Date(Number(ms)).toLocaleTimeString(undefined, {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    });
  }

  function rangeLabel(key) {
    const range = key || state.data?.range?.key || state.range;
    if (range === 'all') return 'All recorded data';
    if (range === '365') return 'Last 12 months';
    return `Last ${range} days`;
  }

  function bucketTitle(point) {
    const bucket = state.data?.availability?.bucket || 'day';
    if (bucket === 'year') return String(new Date(point.startMs).getFullYear());
    if (bucket === 'month') {
      return new Date(point.startMs).toLocaleDateString(undefined, {
        month: 'long',
        year: 'numeric'
      });
    }
    const start = new Date(point.startMs).toLocaleDateString(undefined, {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      year: 'numeric'
    });
    return bucket === 'week' ? `Week of ${start}` : start;
  }

  function incidentTitle(point) {
    const scale = state.data?.incidents?.scale || 'day';
    if (scale === 'year') return String(new Date(point.startMs).getFullYear());
    if (scale === 'month') {
      return new Date(point.startMs).toLocaleDateString(undefined, {
        month: 'long',
        year: 'numeric'
      });
    }
    const start = new Date(point.startMs).toLocaleDateString(undefined, {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      year: 'numeric'
    });
    return scale === 'week' ? `Week of ${start}` : start;
  }

  function setActive(container, attribute, value) {
    if (!container) return;
    for (const button of container.querySelectorAll('button')) {
      button.classList.toggle('active', button.dataset[attribute] === String(value));
    }
  }

  function setNoticeMessages(messages) {
    const unique = [...new Set(messages.filter(Boolean))];
    el.notice.hidden = unique.length === 0;
    el.noticeText.textContent = unique.join(' ');
  }

  function refreshNotices() {
    const messages = [];
    if (state.data?.source?.warnings) messages.push(...state.data.source.warnings);
    if (state.dataError) messages.push(state.dataError);
    if (state.statusError) messages.push(state.statusError);
    if (state.status?.tracker?.running === false) {
      messages.push(
        `The InternetUptimeTracker service (${state.status.tracker.processName || 'InternetUptimeTracker.exe'}) is not running, so live status and logging may be stale.`
      );
    }
    if (typeof window.Chart === 'undefined') {
      messages.push('The chart library failed to load. Restart the dashboard or re-copy public/vendor/chart.umd.js.');
    }
    setNoticeMessages(messages);
  }

  async function fetchJson(url) {
    const response = await fetch(url, { cache: 'no-store' });
    const text = await response.text();
    let payload;
    try {
      payload = JSON.parse(text);
    } catch {
      throw new Error(`The dashboard returned an invalid response (${response.status}).`);
    }
    if (!response.ok) {
      throw new Error(payload.error || `Request failed (${response.status}).`);
    }
    return payload;
  }

  /* ---------- Status ---------- */

  function setStatusVisual(kind) {
    el.statusCard.classList.remove('online', 'offline', 'unknown');
    el.statusCard.classList.add(kind);
    el.livePill.classList.remove('online', 'offline', 'unknown');
    el.livePill.classList.add(kind);
    el.statusIconSvg.innerHTML = ICONS[kind] || ICONS.unknown;
  }

  function renderTrackerState(tracker) {
    if (!tracker) {
      el.trackerState.innerHTML = '<i class="status-dot neutral"></i>Unknown';
      return;
    }
    if (tracker.running === false) {
      el.trackerState.innerHTML = '<i class="status-dot bad"></i>Stopped';
      return;
    }
    if (tracker.running === null || tracker.running === undefined) {
      el.trackerState.innerHTML = '<i class="status-dot warn"></i>Unknown';
      return;
    }
    if (tracker.offline === true) {
      el.trackerState.innerHTML = '<i class="status-dot warn"></i>Running · offline';
      return;
    }
    el.trackerState.innerHTML = '<i class="status-dot good"></i>Running';
  }

  function renderStatusFileState(status) {
    const tracker = status?.tracker;
    if (!tracker || !tracker.exists) {
      el.statusFileState.textContent = 'Not found';
      return;
    }
    if (tracker.offline === true) {
      el.statusFileState.textContent = `Offline · detected ${formatRelative(tracker.lastStatusMs)}`;
      return;
    }
    if (Number.isFinite(tracker.lastStatusAgeSeconds) && tracker.lastStatusAgeSeconds < 10) {
      el.statusFileState.textContent = 'Live · updated just now';
      return;
    }
    el.statusFileState.textContent = `Updated ${formatRelative(tracker.lastStatusMs)}`;
  }

  function updateLastOutageSide() {
    const recent = state.data?.recent;
    if (!recent || recent.length === 0) {
      el.lastOutageState.textContent = '—';
      return;
    }
    const latest = recent[0];
    el.lastOutageState.textContent = `${formatDuration(latest.durationSeconds)} · ${formatRelative(latest.recoveredMs)}`;
  }

  function renderStatus(status) {
    if (!status) return;
    state.status = status;

    const online = Boolean(status.online);
    const kind = online ? 'online' : 'offline';
    setStatusVisual(kind);

    el.livePillText.textContent = online ? 'Internet online' : 'Internet offline';
    document.title = `${online ? '● Online' : '● Offline'} · Internet Uptime`;

    const host = status.host || '8.8.8.8';
    const ping = status.ping || {};
    const tracker = status.tracker || {};
    const ongoing = status.ongoingOutageSeconds;
    const trackerStopped = tracker.running === false;

    if (online) {
      const latency = Number.isFinite(ping.ms) ? ` in ${ping.ms} ms` : '';
      el.statusTitle.textContent = 'Internet is online';
      el.statusDetail.textContent =
        `Live ICMP probe to ${host} succeeded${latency}. ` +
        `Checked ${formatRelative(status.checkedAt)}.` +
        (trackerStopped ? ' The tracker service is stopped, so outages are not currently being logged.' : '');
    } else {
      el.statusTitle.textContent = 'Internet connection is down';
      if (Number.isFinite(ongoing) && ongoing >= 0) {
        el.statusDetail.textContent =
          `Offline for ${formatDuration(ongoing)}. ` +
          'This outage will be written to the CSV log as soon as connectivity returns.' +
          (trackerStopped ? ' The tracker service is stopped, so it may not be recorded.' : '');
      } else {
        el.statusDetail.textContent =
          `The live ICMP probe to ${host} failed. ` +
          'Waiting for connectivity to return.' +
          (trackerStopped ? ' The tracker service is stopped, so this outage may not be recorded.' : '');
      }
    }

    const latencyStrong = el.latencyBadge.querySelector('strong');
    if (online) {
      latencyStrong.textContent = Number.isFinite(ping.ms) ? `${ping.ms} ms` : 'Reachable';
    } else {
      latencyStrong.textContent = 'No reply';
    }

    const hostStrong = el.hostBadge.querySelector('strong');
    hostStrong.textContent = `ICMP · ${host}`;

    el.outageBadge.hidden = online;
    if (!online) {
      const strong = el.outageBadge.querySelector('strong');
      strong.textContent = Number.isFinite(ongoing) && ongoing >= 0
        ? formatDuration(ongoing)
        : 'Active';
    }

    renderTrackerState(tracker);
    renderStatusFileState(status);
    updateLastOutageSide();
    refreshNotices();
  }

  async function loadStatus() {
    const requestSeq = ++statusRequestSeq;
    state.statusLoading = true;
    try {
      const status = await fetchJson('/api/status');
      if (requestSeq !== statusRequestSeq) return;
      state.statusError = null;
      state.statusLoadedAt = Date.now();
      renderStatus(status);
    } catch (err) {
      if (requestSeq !== statusRequestSeq) return;
      state.statusError = `Live status unavailable: ${err.message}`;
      state.status = null;
      setStatusVisual('unknown');
      el.livePillText.textContent = 'Status unknown';
      el.statusTitle.textContent = 'Connection status unavailable';
      el.statusDetail.textContent = 'The dashboard could not reach its local status service.';
      el.latencyBadge.querySelector('strong').textContent = '—';
      el.trackerState.innerHTML = '<i class="status-dot warn"></i>Unknown';
      el.statusFileState.textContent = '—';
      el.outageBadge.hidden = true;
      refreshNotices();
    } finally {
      if (requestSeq === statusRequestSeq) state.statusLoading = false;
    }
  }

  /* ---------- Data / summaries ---------- */

  function renderSummary(data) {
    const summary = data.summary;
    const all = data.allTime;
    const range = rangeLabel(data.range?.key);

    el.kpiAvailability.textContent = fmtPct(summary.uptimePct);
    el.kpiAvailabilitySub.textContent = `${range} · ${formatDuration(summary.downtimeSeconds)} down`;

    el.kpiOutages.textContent = fmtInt(summary.outageCount);
    el.kpiOutagesSub.textContent = `${range} · ${fmtInt(all.outageCount)} all-time`;

    el.kpiDowntime.textContent = formatDuration(summary.downtimeSeconds);
    el.kpiDowntimeSub.textContent = `${(summary.downtimeSeconds / 60).toFixed(1)} minutes in range`;

    if (summary.longest) {
      el.kpiLongest.textContent = formatDuration(summary.longest.durationSeconds);
      el.kpiLongestSub.textContent = `Ended ${formatDateTime(summary.longest.recoveredMs)}`;
    } else {
      el.kpiLongest.textContent = '—';
      el.kpiLongestSub.textContent = 'No outages in range';
    }

    if (summary.outageCount > 0) {
      el.kpiAverage.textContent = formatDuration(summary.averageOutageSeconds);
      el.kpiAverageSub.textContent = `Across ${fmtInt(summary.outageCount)} outages`;
    } else {
      el.kpiAverage.textContent = '—';
      el.kpiAverageSub.textContent = 'No outages in range';
    }

    if (all.lastOutageMs) {
      el.kpiLast.textContent = formatRelative(all.lastOutageMs);
      el.kpiLastSub.textContent = formatDateTime(all.lastOutageMs);
    } else {
      el.kpiLast.textContent = 'Never';
      el.kpiLastSub.textContent = 'No outages recorded';
    }

    el.recentCount.textContent = `${fmtInt(all.outageCount)} total`;
    updateLastOutageSide();
  }

  function renderRecent(data) {
    const recent = data.recent || [];
    if (recent.length === 0) {
      el.recentList.innerHTML =
        '<div class="recent-empty">No outages have been logged yet.<br>That is a good sign.</div>';
      return;
    }

    el.recentList.innerHTML = recent
      .map((outage) => {
        const recovered = Number(outage.recoveredMs);
        const started = Number(outage.startMs);
        return `
          <div class="recent-item" title="Started ${formatDateTime(started)} and recovered ${formatDateTime(recovered)}">
            <div class="recent-main">
              <div class="recent-title">${formatDate(recovered)} · ${formatTime(recovered)}</div>
              <div class="recent-meta">${formatTime(started)} → ${formatTime(recovered)}</div>
            </div>
            <span class="recent-duration">${formatDuration(outage.durationSeconds)}</span>
          </div>`;
      })
      .join('');
  }

  /* ---------- Charts ---------- */

  function chartFont(size, weight) {
    return {
      family: getComputedStyle(document.body).fontFamily,
      size,
      weight: weight || 'normal'
    };
  }

  function createAvailabilityChart(ctx) {
    return new Chart(ctx, {
      type: 'bar',
      data: {
        labels: [],
        datasets: [
          {
            type: 'bar',
            label: 'Downtime',
            data: [],
            yAxisID: 'y',
            backgroundColor: CHART_COLORS.red,
            hoverBackgroundColor: CHART_COLORS.redHover,
            borderRadius: 4,
            borderSkipped: false,
            maxBarThickness: 26,
            order: 2
          },
          {
            type: 'line',
            label: 'Availability',
            data: [],
            yAxisID: 'y1',
            borderColor: CHART_COLORS.green,
            backgroundColor: CHART_COLORS.greenSoft,
            borderWidth: 2,
            tension: 0.32,
            pointRadius: 0,
            pointHoverRadius: 4,
            pointHoverBackgroundColor: CHART_COLORS.green,
            pointHoverBorderColor: '#07110d',
            pointHoverBorderWidth: 2,
            fill: false,
            order: 1
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        animation: { duration: 450 },
        plugins: {
          legend: { display: false },
          tooltip: {
            backgroundColor: 'rgba(8, 13, 24, 0.96)',
            borderColor: 'rgba(148, 163, 184, 0.2)',
            borderWidth: 1,
            titleColor: '#e9effc',
            bodyColor: '#b8c4d8',
            titleFont: chartFont(12, '600'),
            bodyFont: chartFont(11.5),
            padding: 11,
            cornerRadius: 9,
            displayColors: true,
            callbacks: {
              title: (items) => {
                const point = state.data?.availability?.points?.[items[0].dataIndex];
                return point ? bucketTitle(point) : '';
              },
              label: (context) => {
                const point = state.data?.availability?.points?.[context.dataIndex];
                if (!point) return '';
                if (context.dataset.yAxisID === 'y1') {
                  return `Availability: ${fmtPct(point.uptimePct)}`;
                }
                const count = Number(point.count || 0);
                return [
                  `Downtime: ${formatDuration(point.downtimeSeconds)}`,
                  `${fmtInt(count)} outage${count === 1 ? '' : 's'} recovered`
                ];
              }
            }
          }
        },
        scales: {
          x: {
            grid: { display: false },
            border: { display: false },
            ticks: {
              color: CHART_COLORS.tick,
              font: chartFont(10.5),
              maxRotation: 0,
              autoSkip: true,
              maxTicksLimit: window.innerWidth < 640 ? 6 : 12
            }
          },
          y: {
            beginAtZero: true,
            position: 'left',
            border: { display: false },
            grid: { color: CHART_COLORS.grid },
            ticks: {
              color: CHART_COLORS.tick,
              font: chartFont(10.5),
              maxTicksLimit: 6,
              callback: (value) => Number(value).toLocaleString()
            },
            title: {
              display: window.innerWidth >= 640,
              text: 'Downtime (min)',
              color: CHART_COLORS.tickSoft,
              font: chartFont(10.5, '600')
            }
          },
          y1: {
            position: 'right',
            min: 99.5,
            max: 100,
            border: { display: false },
            grid: { display: false },
            ticks: {
              color: 'rgba(53, 224, 161, 0.72)',
              font: chartFont(10.5),
              maxTicksLimit: 5,
              callback: (value) => `${Number(Number(value).toFixed(2))}%`
            },
            title: {
              display: window.innerWidth >= 640,
              text: 'Availability',
              color: 'rgba(53, 224, 161, 0.72)',
              font: chartFont(10.5, '600')
            }
          }
        }
      }
    });
  }

  function createIncidentsChart(ctx) {
    return new Chart(ctx, {
      type: 'bar',
      data: {
        labels: [],
        datasets: [
          {
            label: 'Outages',
            data: [],
            backgroundColor: CHART_COLORS.red,
            hoverBackgroundColor: CHART_COLORS.redHover,
            borderRadius: 5,
            borderSkipped: false,
            maxBarThickness: 38
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        animation: { duration: 450 },
        plugins: {
          legend: { display: false },
          tooltip: {
            backgroundColor: 'rgba(8, 13, 24, 0.96)',
            borderColor: 'rgba(148, 163, 184, 0.2)',
            borderWidth: 1,
            titleColor: '#e9effc',
            bodyColor: '#b8c4d8',
            titleFont: chartFont(12, '600'),
            bodyFont: chartFont(11.5),
            padding: 11,
            cornerRadius: 9,
            callbacks: {
              title: (items) => {
                const point = state.data?.incidents?.points?.[items[0].dataIndex];
                if (!point) return '';
                return incidentTitle(point);
              },
              label: (context) => {
                const point = state.data?.incidents?.points?.[context.dataIndex];
                if (!point) return '';
                return [
                  `${fmtInt(point.count)} outage${point.count === 1 ? '' : 's'}`,
                  `Downtime: ${formatDuration(point.downtimeSeconds)}`
                ];
              }
            }
          }
        },
        scales: {
          x: {
            grid: { display: false },
            border: { display: false },
            ticks: {
              color: CHART_COLORS.tick,
              font: chartFont(10.5),
              maxRotation: 0,
              autoSkip: true,
              maxTicksLimit: window.innerWidth < 640 ? 6 : 10
            }
          },
          y: {
            beginAtZero: true,
            border: { display: false },
            grid: { color: CHART_COLORS.grid },
            ticks: {
              color: CHART_COLORS.tick,
              font: chartFont(10.5),
              precision: 0,
              maxTicksLimit: 6
            },
            title: {
              display: window.innerWidth >= 640,
              text: 'Outages',
              color: CHART_COLORS.tickSoft,
              font: chartFont(10.5, '600')
            }
          }
        }
      }
    });
  }

  function renderAvailability(animate) {
    const data = state.data;
    if (!data || typeof window.Chart === 'undefined') return;

    const availability = data.availability;
    const points = availability.points || [];
    const labels = points.map((point) => point.label);
    const downtimeMinutes = points.map((point) => Number((point.downtimeSeconds / 60).toFixed(2)));
    const uptime = points.map((point) => point.uptimePct);

    const canvas = document.getElementById('availabilityChart');
    if (!availabilityChart) availabilityChart = createAvailabilityChart(canvas.getContext('2d'));

    availabilityChart.data.labels = labels;
    availabilityChart.data.datasets[0].data = downtimeMinutes;
    availabilityChart.data.datasets[1].data = uptime;

    const pcts = uptime.filter((value) => value !== null && Number.isFinite(value));
    const minPct = pcts.length ? Math.min(...pcts) : 100;
    availabilityChart.options.scales.y1.min = Math.max(
      0,
      Math.min(99.5, Math.floor(minPct * 10) / 10 - 0.15)
    );

    const maxDown = downtimeMinutes.length ? Math.max(...downtimeMinutes) : 0;
    availabilityChart.options.scales.y.suggestedMax = Math.max(1, maxDown * 1.18);

    const bucketWord =
      availability.bucket === 'month' ? 'Monthly' : availability.bucket === 'week' ? 'Weekly' : 'Daily';
    el.availabilitySub.textContent =
      `${bucketWord} availability · ${rangeLabel(data.range?.key)} · ${formatDuration(data.summary.downtimeSeconds)} down`;

    el.availabilityEmpty.hidden = data.allTime.outageCount > 0;

    availabilityChart.update(animate ? undefined : 'none');
  }

  function renderIncidents(animate) {
    const data = state.data;
    if (!data || typeof window.Chart === 'undefined') return;

    const incidents = data.incidents;
    const points = incidents.points || [];
    const labels = points.map((point) => point.label);
    const counts = points.map((point) => point.count);
    const colors = points.map((point) =>
      point.count > 0 ? CHART_COLORS.red : CHART_COLORS.grayBar
    );
    const hoverColors = points.map((point) =>
      point.count > 0 ? CHART_COLORS.redHover : CHART_COLORS.grayBarHover
    );

    const canvas = document.getElementById('incidentsChart');
    if (!incidentsChart) incidentsChart = createIncidentsChart(canvas.getContext('2d'));

    incidentsChart.data.labels = labels;
    incidentsChart.data.datasets[0].data = counts;
    incidentsChart.data.datasets[0].backgroundColor = colors;
    incidentsChart.data.datasets[0].hoverBackgroundColor = hoverColors;

    const maxCount = counts.length ? Math.max(...counts) : 0;
    incidentsChart.options.scales.y.suggestedMax = Math.max(4, Math.ceil(maxCount * 1.18));

    const total = counts.reduce((sum, value) => sum + value, 0);
    el.incidentsSub.textContent =
      `${incidents.title} · ${incidents.label} · ${fmtInt(total)} outage${total === 1 ? '' : 's'}`;
    el.incidentsEmpty.hidden = total > 0;

    incidentsChart.update(animate ? undefined : 'none');
  }

  function renderAll(animate) {
    if (!state.data) return;
    renderSummary(state.data);
    renderAvailability(animate);
    renderIncidents(animate);
    renderRecent(state.data);
    const source = state.data.source?.file;
    el.sourcePath.textContent = source?.path || 'downtime_log.csv';
    refreshNotices();
  }

  async function loadData(options = {}) {
    const requestSeq = ++dataRequestSeq;
    state.dataLoading = true;
    const animate = options.animate !== false;

    if (animate) {
      el.refreshBtn.classList.add('spinning');
      window.setTimeout(() => el.refreshBtn.classList.remove('spinning'), 650);
    }

    try {
      const query = new URLSearchParams({ range: state.range, scale: state.scale });
      const data = await fetchJson(`/api/data?${query.toString()}`);
      if (requestSeq !== dataRequestSeq) return;
      state.data = data;
      state.dataLoadedAt = Date.now();
      state.nextDataRefreshAt = Date.now() + DATA_REFRESH_MS;
      state.dataError = null;
      renderAll(animate);
    } catch (err) {
      if (requestSeq !== dataRequestSeq) return;
      state.dataError = `Could not load outage data: ${err.message}`;
      refreshNotices();
    } finally {
      if (requestSeq === dataRequestSeq) state.dataLoading = false;
    }
  }

  /* ---------- Controls ---------- */

  if (el.rangeControl) {
    el.rangeControl.addEventListener('click', (event) => {
      const button = event.target.closest('button[data-range]');
      if (!button) return;
      state.range = button.dataset.range;
      setActive(el.rangeControl, 'range', state.range);
      loadData({ animate: true });
    });
  }

  if (el.scaleControl) {
    el.scaleControl.addEventListener('click', (event) => {
      const button = event.target.closest('button[data-scale]');
      if (!button) return;
      state.scale = button.dataset.scale;
      setActive(el.scaleControl, 'scale', state.scale);
      loadData({ animate: true });
    });
  }

  el.refreshBtn.addEventListener('click', () => {
    loadData({ animate: true });
    loadStatus();
  });

  /* ---------- Clock / footer / polling ---------- */

  function updateClock() {
    el.clock.textContent = new Date().toLocaleTimeString(undefined, { hour12: false });
  }

  function updateFooter() {
    const remaining = Math.max(
      0,
      Math.ceil((state.nextDataRefreshAt - Date.now()) / 1000)
    );
    if (state.dataLoadedAt) {
      el.refreshInfo.textContent =
        `Updated ${formatRelative(state.dataLoadedAt)} · auto-refresh in ${remaining}s`;
    } else {
      el.refreshInfo.textContent = 'Loading outage data…';
    }
  }

  function scheduleDataRefresh() {
    window.setTimeout(async () => {
      if (!document.hidden) await loadData({ animate: false });
      state.nextDataRefreshAt = Date.now() + DATA_REFRESH_MS;
      scheduleDataRefresh();
    }, DATA_REFRESH_MS);
  }

  function scheduleStatusRefresh() {
    window.setTimeout(async () => {
      if (!document.hidden) await loadStatus();
      scheduleStatusRefresh();
    }, STATUS_REFRESH_MS);
  }

  document.addEventListener('visibilitychange', () => {
    if (document.hidden) return;
    const now = Date.now();
    if (now - state.statusLoadedAt > STATUS_REFRESH_MS) loadStatus();
    if (now - state.dataLoadedAt > DATA_REFRESH_MS) loadData({ animate: false });
  });

  function applyChartResponsiveOptions() {
    const small = window.innerWidth < 640;
    const tickLimit = small ? 6 : 12;

    if (availabilityChart) {
      availabilityChart.options.scales.x.ticks.maxTicksLimit = tickLimit;
      availabilityChart.options.scales.y.title.display = !small;
      availabilityChart.options.scales.y1.title.display = !small;
      availabilityChart.update('none');
    }
    if (incidentsChart) {
      incidentsChart.options.scales.x.ticks.maxTicksLimit = small ? 6 : 10;
      incidentsChart.options.scales.y.title.display = !small;
      incidentsChart.update('none');
    }
  }

  window.addEventListener('resize', applyChartResponsiveOptions);

  if (typeof window.Chart !== 'undefined') {
    Chart.defaults.color = CHART_COLORS.tick;
    Chart.defaults.borderColor = CHART_COLORS.grid;
    Chart.defaults.font.family = getComputedStyle(document.body).fontFamily;
  }

  setActive(el.rangeControl, 'range', state.range);
  setActive(el.scaleControl, 'scale', state.scale);

  updateClock();
  window.setInterval(updateClock, 1000);
  window.setInterval(updateFooter, 1000);

  loadData({ animate: false });
  loadStatus();
  scheduleDataRefresh();
  scheduleStatusRefresh();
})();
