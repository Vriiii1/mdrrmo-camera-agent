// Tab switching
document.querySelectorAll('.tab').forEach(btn => {
  btn.addEventListener('click', () => {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    document.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('active'));
    btn.classList.add('active');
    document.getElementById('tab-' + btn.dataset.tab).classList.add('active');
  });
});

// Health check badge
async function checkHealth() {
  const badge = document.getElementById('status-badge');
  try {
    const res = await fetch('/health');
    if (res.ok) {
      badge.textContent = 'Agent online';
      badge.className = 'badge badge-online';
    } else {
      badge.textContent = 'Agent error';
      badge.className = 'badge badge-error';
    }
  } catch {
    badge.textContent = 'Unreachable';
    badge.className = 'badge badge-error';
  }
}
checkHealth();

// Generic RTSP form
document.getElementById('form-generic').addEventListener('submit', async e => {
  e.preventDefault();
  const fd = new FormData(e.target);
  const result = document.getElementById('result-generic');
  result.textContent = 'Adding camera…';
  result.className = 'result pending';
  try {
    const res = await fetch('/cameras', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        rtsp_url:     fd.get('rtsp_url'),
        label:        fd.get('label'),
        public_label: fd.get('public_label') || null,
        audio_enabled: fd.get('audio_enabled') === 'on',
      }),
    });
    const body = await res.json();
    if (res.ok) {
      result.textContent = `✓ Camera added — stream path: ${body.stream_path}`;
      result.className = 'result success';
      e.target.reset();
    } else {
      result.textContent = `Error: ${body.error ?? res.statusText}`;
      result.className = 'result error';
    }
  } catch (err) {
    result.textContent = `Network error: ${err.message}`;
    result.className = 'result error';
  }
});

// Hikvision channel list form
document.getElementById('form-hikvision-list').addEventListener('submit', async e => {
  e.preventDefault();
  const fd = new FormData(e.target);
  const result = document.getElementById('result-hikvision');
  const channelSection = document.getElementById('channel-list');
  result.textContent = 'Querying NVR…';
  result.className = 'result pending';
  channelSection.style.display = 'none';
  try {
    const res = await fetch('/cameras/hikvision', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ nvr_ip: fd.get('nvr_ip'), user: fd.get('user'), pass: fd.get('pass') }),
    });
    const body = await res.json();
    if (res.ok) {
      if (body.default_creds_warning) {
        result.textContent = '⚠ ' + body.default_creds_warning;
        result.className = 'result warning';
      } else {
        result.textContent = `Found ${body.channels.length} channel(s).`;
        result.className = 'result success';
      }
      const ul = document.getElementById('channels');
      ul.innerHTML = '';
      body.channels.forEach(ch => {
        const li = document.createElement('li');
        li.innerHTML = `<label><input type="checkbox" value="${ch.id}"> CH${ch.id} — ${ch.name}</label>`;
        ul.appendChild(li);
      });
      channelSection.style.display = '';
    } else {
      result.textContent = `Error: ${body.error ?? res.statusText}`;
      result.className = 'result error';
    }
  } catch (err) {
    result.textContent = `Network error: ${err.message}`;
    result.className = 'result error';
  }
});
