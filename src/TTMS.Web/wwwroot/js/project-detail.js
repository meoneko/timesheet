/* ===========================================================================
   Project Detail UI Scripts
   =========================================================================== */

(function () {
    'use strict';

    // Helper: Parse Project ID from the URL or fallback to DOM
    function getProjectId() {
        const match = window.location.pathname.match(/\/Details\/(\d+)/i);
        if (match) return match[1];

        // Fallbacks
        const form = document.querySelector('form[action*="/AddMember/"]');
        if (form) {
            const actionMatch = form.getAttribute('action').match(/\/AddMember\/(\d+)/);
            if (actionMatch) return actionMatch[1];
        }

        const taskLink = document.querySelector('a[href*="/Tasks?projectId="], a[href*="/Tasks/Create?projectId="]');
        if (taskLink) {
            const urlParams = new URLSearchParams(taskLink.href.split('?')[1]);
            if (urlParams.has('projectId')) return urlParams.get('projectId');
        }

        return null;
    }

    // =======================================================================
    // 1) Tab Navigation & Deep Linking
    // =======================================================================

    document.addEventListener('DOMContentLoaded', () => {
        const params = new URLSearchParams(window.location.search);
        const tabName = params.get('tab') || 'overview';

        // Initialize active tab based on query param
        const tabEl = document.querySelector(`#projectDetailTabs button[data-bs-target="#${tabName}"]`);
        if (tabEl) {
            const tab = new bootstrap.Tab(tabEl);
            tab.show();
        }

        // Synchronize tab clicks with URL
        const tabButtons = document.querySelectorAll('#projectDetailTabs button[data-bs-toggle="tab"]');
        tabButtons.forEach(btn => {
            btn.addEventListener('shown.bs.tab', (e) => {
                const targetId = e.target.getAttribute('data-bs-target').substring(1);
                const currentParams = new URLSearchParams(window.location.search);
                if (currentParams.get('tab') !== targetId) {
                    currentParams.set('tab', targetId);
                    const newUrl = `${window.location.pathname}?${currentParams.toString()}`;
                    window.history.pushState({ tab: targetId }, '', newUrl);
                }
            });
        });
    });

    // Handle back/forward buttons
    window.addEventListener('popstate', () => {
        const params = new URLSearchParams(window.location.search);
        const tabName = params.get('tab') || 'overview';
        const tabEl = document.querySelector(`#projectDetailTabs button[data-bs-target="#${tabName}"]`);
        if (tabEl) {
            const tab = new bootstrap.Tab(tabEl);
            tab.show();
        }
    });

    // Global helper to activate tabs programmatically
    window.activateProjectTab = function (tabName) {
        const tabEl = document.querySelector(`#projectDetailTabs button[data-bs-target="#${tabName}"]`);
        if (tabEl) {
            const tab = new bootstrap.Tab(tabEl);
            tab.show();
            tabEl.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    };

    // =======================================================================
    // 2) Project Code Clipboard Copy
    // =======================================================================

    window.copyProjectCode = function (code) {
        if (!code) return;
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(code).then(() => {
                alert(`Project code "${code}" copied to clipboard.`);
            }).catch(err => {
                fallbackCopy(code);
            });
        } else {
            fallbackCopy(code);
        }
    };

    function fallbackCopy(code) {
        const textarea = document.createElement('textarea');
        textarea.value = code;
        textarea.style.position = 'fixed';
        document.body.appendChild(textarea);
        textarea.select();
        try {
            document.execCommand('copy');
            alert(`Project code "${code}" copied to clipboard.`);
        } catch (err) {
            console.error('Failed to copy project code:', err);
            alert('Failed to copy project code.');
        }
        document.body.removeChild(textarea);
    }

    // =======================================================================
    // 3) AJAX History Pagination & Filters
    // =======================================================================

    let historySkip = 5; // details initial model renders 5 rows
    const historyTake = 5;
    let isLoadingHistory = false;

    async function loadHistory(append = true) {
        if (isLoadingHistory) return;
        isLoadingHistory = true;

        const btn = document.getElementById('btnLoadMoreHistory');
        if (btn) {
            btn.disabled = true;
            btn.textContent = 'Loading...';
        }

        const projectId = getProjectId();
        if (!projectId) {
            console.error('Project ID not found.');
            isLoadingHistory = false;
            if (btn) {
                btn.disabled = false;
                btn.textContent = 'Load more';
            }
            return;
        }

        const eventFilter = document.getElementById('historyEventFilter')?.value || '';
        const userFilter = document.getElementById('historyUserFilter')?.value || '';

        const url = `/Projects/History/${projectId}?skip=${historySkip}&take=${historyTake}&eventFilter=${encodeURIComponent(eventFilter)}&userFilter=${encodeURIComponent(userFilter)}`;

        try {
            const response = await fetch(url, {
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            const html = await response.text();
            const tbody = document.getElementById('historyTableBody');

            if (!append) {
                tbody.innerHTML = '';
            }

            // Parse HTML to count returned rows
            const tempDiv = document.createElement('div');
            tempDiv.innerHTML = `<table><tbody>${html}</tbody></table>`;
            const newRows = tempDiv.querySelectorAll('tr');
            const rowsCount = newRows.length;

            if (html.trim().length > 0) {
                tbody.insertAdjacentHTML('beforeend', html);
            }

            if (append) {
                historySkip += rowsCount;
            } else {
                historySkip = rowsCount;
            }

            const totalRows = tbody.querySelectorAll('tr').length;
            const counter = document.getElementById('historyCounter');
            if (counter) {
                counter.textContent = `Showing ${totalRows} rows`;
            }

            // Hide or show the Load More button based on how many rows the server returned.
            // When appending, we got a page-sized slice; if it's smaller than the page
            // size we've reached the end. When replacing (filter changed), zero results
            // also means there's nothing more to load.
        const btnLoadMore = document.getElementById('btnLoadMoreHistory');
            const noMoreContainer = document.getElementById('noMoreHistory');
            if (rowsCount < historyTake) {
                if (btnLoadMore) btnLoadMore.style.display = 'none';
                if (noMoreContainer) noMoreContainer.style.display = '';
            } else {
        if (btnLoadMore) {
                    btnLoadMore.style.display = 'inline-block';
                    btnLoadMore.disabled = false;
                    btnLoadMore.textContent = 'Load more';
        }
                if (noMoreContainer) noMoreContainer.style.display = 'none';
            }
        } catch (err) {
            console.error('Error fetching history:', err);
            alert('Failed to load history.');
            if (btn) {
                btn.disabled = false;
                btn.textContent = 'Load more';
            }
        } finally {
            isLoadingHistory = false;
        }
    }

    function filterHistory() {
        historySkip = 0;
        loadHistory(false);
    }

    document.addEventListener('DOMContentLoaded', () => {
        const btnLoadMore = document.getElementById('btnLoadMoreHistory');
        if (btnLoadMore) {
            btnLoadMore.addEventListener('click', () => loadHistory(true));
        }

        const eventFilterEl = document.getElementById('historyEventFilter');
        const userFilterEl = document.getElementById('historyUserFilter');
        if (eventFilterEl) {
            eventFilterEl.addEventListener('change', filterHistory);
        }
        if (userFilterEl) {
            userFilterEl.addEventListener('change', filterHistory);
        }
    });

})();

