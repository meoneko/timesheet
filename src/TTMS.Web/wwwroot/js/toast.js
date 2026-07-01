/* ===========================================================================
   Toast Notification System
   Thread-safe, auto-dismissing, stackable toast notifications.
   Replaces raw alert() calls everywhere.
   =========================================================================== */

(function () {
    'use strict';

    const CONTAINER_ID = 'ttms-toast-container';
    const DEFAULT_DURATION = 4000;

    const ICONS = {
        success: 'bi-check-circle-fill',
        error: 'bi-x-circle-fill',
        warning: 'bi-exclamation-triangle-fill',
        info: 'bi-info-circle-fill'
    };

    const COLORS = {
        success: { bg: '#d1e7dd', border: '#a3cfbb', text: '#0f5132', icon: '#198754' },
        error: { bg: '#f8d7da', border: '#f1aeb5', text: '#842029', icon: '#dc3545' },
        warning: { bg: '#fff3cd', border: '#ffe69c', text: '#664d03', icon: '#ffc107' },
        info: { bg: '#cff4fc', border: '#9eeaf9', text: '#055160', icon: '#0dcaf0' }
    };

    function getOrCreateContainer() {
        let el = document.getElementById(CONTAINER_ID);
        if (!el) {
            el = document.createElement('div');
            el.id = CONTAINER_ID;
            el.style.cssText = 'position:fixed;top:1rem;right:1rem;z-index:9999;display:flex;flex-direction:column;gap:0.5rem;max-width:400px;';
            document.body.appendChild(el);
        }
        return el;
    }

    function createToast(message, type) {
        const color = COLORS[type] || COLORS.info;
        const icon = ICONS[type] || ICONS.info;

        const toast = document.createElement('div');
        toast.className = 'ttms-toast';
        toast.setAttribute('role', 'alert');
        toast.setAttribute('aria-live', 'assertive');
        toast.style.cssText = `
            display:flex;align-items:flex-start;gap:0.75rem;
            padding:0.875rem 1rem;border-radius:0.5rem;border:1px solid ${color.border};
            background:${color.bg};color:${color.text};font-size:0.875rem;
            box-shadow:0 4px 12px rgba(0,0,0,0.15);
            opacity:0;transform:translateX(2rem);transition:opacity 0.3s,transform 0.3s;
            pointer-events:auto;word-break:break-word;
        `;

        toast.innerHTML = `
            <i class="bi ${icon}" style="font-size:1.25rem;flex-shrink:0;color:${color.icon};"></i>
            <span style="flex:1;">${escapeHtml(message)}</span>
            <button type="button" class="btn-close btn-close-sm" aria-label="Close" style="flex-shrink:0;margin-top:0.125rem;"></button>
        `;

        return toast;
    }

    function escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    function showToast(toast, duration) {
        const container = getOrCreateContainer();
        container.appendChild(toast);

        // Animate in
        requestAnimationFrame(() => {
            toast.style.opacity = '1';
            toast.style.transform = 'translateX(0)';
        });

        // Close button
        const closeBtn = toast.querySelector('.btn-close');
        if (closeBtn) {
            closeBtn.addEventListener('click', () => dismiss(toast));
        }

        // Auto-dismiss
        if (duration > 0) {
            setTimeout(() => dismiss(toast), duration);
        }
    }

    function dismiss(toast) {
        if (toast._dismissed) return;
        toast._dismissed = true;
        toast.style.opacity = '0';
        toast.style.transform = 'translateX(2rem)';
        setTimeout(() => {
            if (toast.parentNode) {
                toast.parentNode.removeChild(toast);
            }
        }, 300);
    }

    // Public API
    window.Toast = {
        /**
         * Show a toast notification.
         * @param {string} message - The message text.
         * @param {'success'|'error'|'warning'|'info'} type - Toast type.
         * @param {number} [duration=4000] - Auto-dismiss duration in ms. 0 = sticky.
         */
        show: function (message, type, duration) {
            type = type || 'info';
            duration = duration !== undefined ? duration : DEFAULT_DURATION;
            const toast = createToast(message, type);
            showToast(toast, duration);
        },

        success: function (message, duration) { this.show(message, 'success', duration); },
        error: function (message, duration) { this.show(message, 'error', duration); },
        warning: function (message, duration) { this.show(message, 'warning', duration); },
        info: function (message, duration) { this.show(message, 'info', duration); }
    };
})();