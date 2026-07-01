// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// ===========================================================================
// TTMS sidebar: persist the user's recent projects in localStorage and render
// them at the top of the sidebar. Per-user key, capped at MAX_RECENT entries.
// ===========================================================================
(function () {
    var MAX_RECENT = 5;
    var STORAGE_KEY_PREFIX = 'ttms:recent-projects:';

    function getUserKey() {
        // We tag <body data-user-id="..."> in _Layout.cshtml. Falls back to a
        // global key when no id is available (e.g. anonymous user).
        var uid = document.body && document.body.getAttribute('data-user-id');
        return STORAGE_KEY_PREFIX + (uid || 'anon');
    }

    function readRecent() {
        try {
            var raw = localStorage.getItem(getUserKey());
            if (!raw) return [];
            var arr = JSON.parse(raw);
            return Array.isArray(arr) ? arr : [];
        } catch (_) { return []; }
    }

    function writeRecent(items) {
        try { localStorage.setItem(getUserKey(), JSON.stringify(items.slice(0, MAX_RECENT))); }
        catch (_) { /* quota / disabled storage — silently skip */ }
    }

    function pushRecent(entry) {
        if (!entry || !entry.id) return;
        var items = readRecent().filter(function (x) { return x.id !== entry.id; });
        items.unshift(entry);
        writeRecent(items);
    }

    function renderRecent() {
        var list = document.getElementById('ttmsRecentProjects');
        if (!list) return;
        var items = readRecent();
        var recentLabel = document.getElementById('ttmsRecentProjectsLabel');
        var allLabel = document.getElementById('ttmsAllProjectsLabel');
        if (items.length === 0) {
            list.hidden = true;
            if (recentLabel) recentLabel.style.display = 'none';
            if (allLabel) allLabel.style.display = 'none';
            return;
        }
        list.hidden = false;
        if (recentLabel) recentLabel.style.display = 'flex';
        if (allLabel) allLabel.style.display = 'flex';
        // Clear existing dynamic rows (keep the empty-state marker hidden).
        list.innerHTML = '';
        items.forEach(function (p) {
            var li = document.createElement('li');
            li.className = 'nav-item';
            var a = document.createElement('a');
            a.className = 'nav-link';
            a.href = '/Projects/Details/' + encodeURIComponent(p.id);
            a.title = p.code + ' — ' + p.name;
            var badge = document.createElement('span');
            badge.className = 'badge bg-light text-dark border ttms-side-code';
            badge.textContent = p.code || '';
            var text = document.createElement('span');
            text.className = 'text-truncate';
            text.textContent = p.name || '';
            a.appendChild(badge);
            a.appendChild(text);
            li.appendChild(a);
            list.appendChild(li);
        });
    }

    // ===========================================================================
    // Form loading states: prevent double-submit and show spinner on submit
    // buttons that carry the data-loading attribute.
    // ===========================================================================
    (function () {
        document.addEventListener('DOMContentLoaded', function () {
            document.querySelectorAll('form[data-loading]').forEach(function (form) {
                form.addEventListener('submit', function () {
                    var btn = form.querySelector('button[type="submit"]');
                    if (btn && !btn.disabled) {
                        btn.disabled = true;
                        var original = btn.innerHTML;
                        btn.setAttribute('data-original-html', original);
                        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span>' + (btn.getAttribute('data-loading-text') || 'Saving...');
                    }
                });
            });
        });
    })();

    // Find the currently active project in the sidebar and push it to recent.
    function trackCurrent() {
        var active = document.querySelector('.ttms-side-projects .nav-item .nav-link.active');
        if (!active) return;
        var li = active.closest('.nav-item');
        if (!li) return;
        var id = li.getAttribute('data-project-id');
        var code = li.getAttribute('data-project-code');
        var name = li.getAttribute('data-project-name');
        if (!id) return;
        pushRecent({ id: id, code: code || '', name: name || '' });
    }

    document.addEventListener('DOMContentLoaded', function () {
        trackCurrent();
        renderRecent();
    });
})();

