/* ===========================================================================
   Jira-style Kanban Board UI Logic
   =========================================================================== */

(function () {
    'use strict';

    document.addEventListener('DOMContentLoaded', () => {
        const board = document.getElementById('kanban-board');
        if (!board) return;

        const isReadOnly = board.classList.contains('readonly');
        const projectId = board.dataset.projectId;

        // Modal elements
        const blockedReasonModalEl = document.getElementById('blockedReasonModal');
        let blockedReasonModal = null;
        if (blockedReasonModalEl) {
            blockedReasonModal = new bootstrap.Modal(blockedReasonModalEl);
        }

        const blockedForm = document.getElementById('blockedReasonForm');
        const blockedInput = document.getElementById('blockedReasonInput');
        const submitBlockedBtn = document.getElementById('submitBlockedReason');

        // State to keep track of card being changed to Blocked
        let pendingState = null;

        // Setup Drag and Drop event listeners if board is editable
        if (!isReadOnly) {
            setupDragAndDrop();
        }

        // Setup select dropdown fallbacks for mobile/a11y users
        setupMobileFallback();

        // Setup card details preview drawer
        setupCardPreview();

        // 1) Drag and Drop Implementation
        function setupDragAndDrop() {
            const cards = board.querySelectorAll('.kanban-card:not(.readonly)');
            const containers = board.querySelectorAll('.kanban-cards-container');

            cards.forEach(card => {
                card.setAttribute('draggable', 'true');

                card.addEventListener('dragstart', (e) => {
                    if (card.classList.contains('saving')) {
                        e.preventDefault();
                        return;
                    }
                    card.classList.add('dragging');
                    e.dataTransfer.setData('text/plain', card.dataset.taskId);
                    
                    // Track original position for rollback
                    card.dataset.originalColumn = card.parentElement.id;
                    // Store next sibling to restore exact order
                    const sibling = card.nextElementSibling;
                    card.dataset.originalSiblingId = sibling ? sibling.id : '';
                });

                card.addEventListener('dragend', () => {
                    card.classList.remove('dragging');
                });
            });

            containers.forEach(container => {
                container.addEventListener('dragover', (e) => {
                    e.preventDefault();
                    // Identify the element being dragged
                    const dragging = board.querySelector('.dragging');
                    if (!dragging) return;

                    container.classList.add('drag-over');

                    // Determine drop position (between cards or at the end)
                    const afterElement = getDragAfterElement(container, e.clientY);
                    if (afterElement == null) {
                        container.appendChild(dragging);
                    } else {
                        container.insertBefore(dragging, afterElement);
                    }
                });

                container.addEventListener('dragleave', () => {
                    container.classList.remove('drag-over');
                });

                container.addEventListener('drop', async (e) => {
                    e.preventDefault();
                    container.classList.remove('drag-over');

                    const taskId = e.dataTransfer.getData('text/plain');
                    const card = board.querySelector(`.kanban-card[data-task-id="${taskId}"]`);
                    if (!card) return;

                    const targetStatus = container.dataset.status;
                    const originalStatus = card.dataset.originalColumn.replace('column-', '');

                    if (targetStatus === originalStatus) {
                        return; // dropped in the same column
                    }

                    // Perform change transition
                    handleStatusChange(card, targetStatus, originalStatus);
                });
            });
        }

        // Helper to determine vertical placement when dragging card over a column
        function getDragAfterElement(container, y) {
            const draggableElements = [...container.querySelectorAll('.kanban-card:not(.dragging)')];

            return draggableElements.reduce((closest, child) => {
                const box = child.getBoundingClientRect();
                const offset = y - box.top - box.height / 2;
                if (offset < 0 && offset > closest.offset) {
                    return { offset: offset, element: child };
                } else {
                    return closest;
                }
            }, { offset: Number.NEGATIVE_INFINITY }).element;
        }

        // 2) Mobile Status Fallback Dropdown Handler
        function setupMobileFallback() {
            board.addEventListener('change', (e) => {
                if (e.target && e.target.classList.contains('kanban-card-select-fallback')) {
                    const select = e.target;
                    const card = select.closest('.kanban-card');
                    if (!card) return;

                    if (card.classList.contains('saving')) {
                        select.value = card.dataset.status;
                        return;
                    }

                    const targetStatus = select.value;
                    const originalStatus = card.dataset.status;

                    if (targetStatus === originalStatus) return;

                    // Save original state for rollback
                    card.dataset.originalColumn = `column-${originalStatus}`;
                    const sibling = card.nextElementSibling;
                    card.dataset.originalSiblingId = sibling ? sibling.id : '';

                    // Optimistically move card to target column container in the UI
                    const targetContainer = document.getElementById(`column-${targetStatus}`);
                    if (targetContainer) {
                        targetContainer.appendChild(card);
                    }

                    handleStatusChange(card, targetStatus, originalStatus, select);
                }
            });
        }

        // 3) Common status change workflow (including Modal gating & AJAX requests)
        function handleStatusChange(card, targetStatus, originalStatus, selectEl = null) {
            const taskId = card.dataset.taskId;
            const rowVersion = card.dataset.rowVersion;

            if (selectEl) {
                updateStatusSelectStyle(selectEl);
            }

            if (targetStatus === 'Blocked') {
                // Show modal for blocked reason input
                pendingState = { card, targetStatus, originalStatus, selectEl };
                
                if (blockedInput) {
                    blockedInput.value = '';
                }
                
                if (blockedReasonModal) {
                    blockedReasonModal.show();
                } else {
                    // Fallback to JS prompt if modal isn't available
                    const reason = prompt("Enter Blocked Reason:");
                    if (reason && reason.trim()) {
                        submitStatusChange(card, targetStatus, reason.trim(), rowVersion, selectEl);
                    } else {
                        revertCard(card);
                        if (selectEl) {
                            selectEl.value = originalStatus;
                            updateStatusSelectStyle(selectEl);
                        }
                    }
                }
            } else {
                submitStatusChange(card, targetStatus, null, rowVersion, selectEl);
            }
        }

        // Handle Blocked Modal Cancel/Close Events to revert positions immediately
        if (blockedReasonModalEl) {
            blockedReasonModalEl.addEventListener('hidden.bs.modal', () => {
                if (pendingState) {
                    revertCard(pendingState.card);
                    if (pendingState.selectEl) {
                        pendingState.selectEl.value = pendingState.originalStatus;
                        updateStatusSelectStyle(pendingState.selectEl);
                    }
                    pendingState = null;
                }
            });
        }

        if (submitBlockedBtn && blockedForm) {
            blockedForm.addEventListener('submit', (e) => {
                e.preventDefault();
                if (!pendingState) return;

                const reason = blockedInput.value.trim();
                if (!reason) {
                    alert('A blocked reason is required.');
                    return;
                }

                if (reason.length > 500) {
                    alert('Blocked reason cannot exceed 500 characters.');
                    return;
                }

                const state = pendingState;
                pendingState = null; // Clear pending state so modal hide listener doesn't revert card

                if (blockedReasonModal) {
                    blockedReasonModal.hide();
                }

                submitStatusChange(state.card, state.targetStatus, reason, state.card.dataset.rowVersion, state.selectEl);
            });
        }

        // 4) Submit AJAX Mutation
        async function submitStatusChange(card, targetStatus, blockedReason, rowVersion, selectEl) {
            card.classList.add('saving');
            if (selectEl) selectEl.disabled = true;

            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
            const url = `/Projects/${projectId}/Tasks/ChangeStatusAjax`;

            const formData = new FormData();
            formData.append('taskId', card.dataset.taskId);
            formData.append('status', targetStatus);
            formData.append('blockedReason', blockedReason || '');
            formData.append('rowVersion', rowVersion);
            if (token) {
                formData.append('__RequestVerificationToken', token);
            }

            try {
                const response = await fetch(url, {
                    method: 'POST',
                    body: formData,
                    headers: {
                        'X-Requested-With': 'XMLHttpRequest'
                    }
                });

                if (response.ok) {
                    const updatedCard = await response.json();
                    
                    // Success updates
                    card.classList.remove('saving');
                    card.dataset.status = updatedCard.status;
                    card.dataset.rowVersion = updatedCard.rowVersion;

                    // Update local DOM element attributes & fallback select state
                    const cardSelect = card.querySelector('.kanban-card-select-fallback');
                    if (cardSelect) {
                        cardSelect.disabled = false;
                        cardSelect.value = updatedCard.status;
                    }

                    // Update Blocked Reason badge if present
                    let blockedBanner = card.querySelector('.kanban-card-blocked-banner');
                    if (updatedCard.status === 'Blocked') {
                        if (!blockedBanner) {
                            blockedBanner = document.createElement('div');
                            blockedBanner.className = 'kanban-card-blocked-banner';
                            card.querySelector('.kanban-card-title').after(blockedBanner);
                        }
                        blockedBanner.textContent = `Blocked: ${updatedCard.blockedReason}`;
                    } else {
                        if (blockedBanner) {
                            blockedBanner.remove();
                        }
                    }

                    // Update column statistics counters
                    updateColumnStatistics();

                    // Refresh the drawer details to fetch updated history, blockedreason etc.
                    const drawer = document.getElementById('task-preview-drawer');
                    if (drawer && drawer.classList.contains('open') && drawer.querySelector('.drawer-status-select')?.dataset.taskId === card.dataset.taskId) {
                        openDrawer(card.dataset.taskId, false);
                    }
                } else {
                    const errData = await response.json().catch(() => ({}));
                    const errMsg = errData.error || `Failed to change task status (HTTP ${response.status}).`;

                    if (response.status === 409) {
                        // Concurrency conflict
                        alert('Conflict: This task was modified by another user. The board will now reload.');
                        window.location.reload();
                        return;
                    }

                    alert(errMsg);
                    revertCard(card);
                    if (selectEl) {
                        selectEl.disabled = false;
                        selectEl.value = card.dataset.status;
                        updateStatusSelectStyle(selectEl);
                    }
                    card.classList.remove('saving');
                }
            } catch (err) {
                console.error('Error updating task status:', err);
                alert('An error occurred while updating the task status.');
                revertCard(card);
                if (selectEl) {
                    selectEl.disabled = false;
                    selectEl.value = card.dataset.status;
                    updateStatusSelectStyle(selectEl);
                }
                card.classList.remove('saving');
            }
        }

        // Revert card back to its original location
        function revertCard(card) {
            const originalColumnId = card.dataset.originalColumn;
            const originalSiblingId = card.dataset.originalSiblingId;
            const container = document.getElementById(originalColumnId);

            if (container) {
                if (originalSiblingId) {
                    const sibling = document.getElementById(originalSiblingId);
                    if (sibling) {
                        container.insertBefore(card, sibling);
                        return;
                    }
                }
                container.appendChild(card);
            }
        }

        // Calculate and update count labels in headers dynamically
        function updateColumnStatistics() {
            const columns = board.querySelectorAll('.kanban-column');
            columns.forEach(col => {
                const container = col.querySelector('.kanban-cards-container');
                const badge = col.querySelector('.kanban-column-count');
                if (container && badge) {
                    const count = container.querySelectorAll('.kanban-card').length;
                    badge.textContent = count;
                }
            });
        }

        // 5) Task Details Preview Side Drawer Implementation
        function setupCardPreview() {
            const drawer = document.getElementById('task-preview-drawer');
            const backdrop = document.getElementById('drawer-backdrop');
            if (!drawer || !backdrop) return;

            board.addEventListener('click', (e) => {
                const card = e.target.closest('.kanban-card');
                if (!card) return;

                // Exclude dropdown select fallback
                if (e.target.closest('.kanban-card-select-fallback')) {
                    return;
                }

                // Intercept click: stop default action (like following the title link)
                e.preventDefault();

                const taskId = card.dataset.taskId;
                openDrawer(taskId);
            });

            // Close events
            backdrop.addEventListener('click', closeDrawer);
            document.addEventListener('keydown', (e) => {
                if (e.key === 'Escape') {
                    closeDrawer();
                }
            });
        }

        async function openDrawer(taskId, showSpinner = true) {
            const drawer = document.getElementById('task-preview-drawer');
            const backdrop = document.getElementById('drawer-backdrop');
            if (!drawer || !backdrop) return;

            if (showSpinner) {
                drawer.innerHTML = `
                    <div class="drawer-loading">
                        <div class="spinner-border text-primary" role="status">
                            <span class="visually-hidden">Loading...</span>
                        </div>
                    </div>
                `;
            }
            drawer.classList.add('open');
            backdrop.classList.add('show');

            try {
                const response = await fetch(`/Projects/${projectId}/Tasks/${taskId}/Preview`);
                if (response.ok) {
                    const html = await response.text();
                    // Preserve active tab before replacing content
                    const activeTabId = drawer.querySelector('#drawerActivityTabs .active')?.id;

                    drawer.innerHTML = html;
                    setupDrawerInteractions(drawer, taskId);

                    // Restore active tab
                    if (activeTabId) {
                        const tabEl = drawer.querySelector(`#${activeTabId}`);
                        if (tabEl) {
                            bootstrap.Tab.getOrCreateInstance(tabEl).show();
                        }
                    }
                } else {
                    if (showSpinner) {
                        drawer.innerHTML = `
                            <div class="p-3">
                                <div class="alert alert-danger">Failed to load task preview.</div>
                                <button type="button" class="btn btn-secondary btn-sm" id="close-preview-drawer-err">Close</button>
                            </div>
                        `;
                        document.getElementById('close-preview-drawer-err')?.addEventListener('click', closeDrawer);
                    }
                }
            } catch (err) {
                console.error('Error loading task preview:', err);
                if (showSpinner) {
                    drawer.innerHTML = `
                        <div class="p-3">
                            <div class="alert alert-danger">An error occurred while loading preview.</div>
                            <button type="button" class="btn btn-secondary btn-sm" id="close-preview-drawer-err">Close</button>
                        </div>
                    `;
                    document.getElementById('close-preview-drawer-err')?.addEventListener('click', closeDrawer);
                }
            }
        }

        function updateStatusSelectStyle(select) {
            if (!select) return;
            const val = select.value;
            select.classList.remove('status-todo', 'status-inprogress', 'status-done', 'status-blocked', 'status-pending', 'status-cancelled');
            
            let cls = 'status-todo';
            if (val === 'InProgress') cls = 'status-inprogress';
            else if (val === 'Done') cls = 'status-done';
            else if (val === 'Blocked') cls = 'status-blocked';
            else if (val === 'Pending') cls = 'status-pending';
            else if (val === 'Cancelled') cls = 'status-cancelled';
            
            select.classList.add(cls);
        }

        function closeDrawer() {
            const drawer = document.getElementById('task-preview-drawer');
            const backdrop = document.getElementById('drawer-backdrop');
            if (!drawer || !backdrop) return;

            drawer.classList.remove('open');
            backdrop.classList.remove('show');
        }

        function setupDrawerInteractions(drawer, taskId) {
            // Close button
            const closeBtn = drawer.querySelector('#close-preview-drawer');
            if (closeBtn) {
                closeBtn.addEventListener('click', closeDrawer);
            }

            // Status Select
            const statusSelect = drawer.querySelector('.drawer-status-select');
            if (statusSelect) {
                statusSelect.addEventListener('change', async (e) => {
                    const card = document.getElementById(`card-${taskId}`);
                    if (!card) return;

                    const targetStatus = statusSelect.value;
                    const originalStatus = card.dataset.status;

                    if (targetStatus === originalStatus) return;

                    card.dataset.originalColumn = `column-${originalStatus}`;
                    const sibling = card.nextElementSibling;
                    card.dataset.originalSiblingId = sibling ? sibling.id : '';

                    const targetContainer = document.getElementById(`column-${targetStatus}`);
                    if (targetContainer) {
                        targetContainer.appendChild(card);
                    }

                    handleStatusChange(card, targetStatus, originalStatus, statusSelect);
                });
            }

            // Log Time form submit
            const logTimeForm = drawer.querySelector('#drawerLogTimeForm');
            if (logTimeForm) {
                logTimeForm.addEventListener('submit', async (e) => {
                    e.preventDefault();
                    const submitBtn = logTimeForm.querySelector('button[type="submit"]');
                    if (submitBtn) submitBtn.disabled = true;

                    const action = logTimeForm.getAttribute('action');
                    const formData = new FormData(logTimeForm);

                    try {
                        const response = await fetch(action, {
                            method: 'POST',
                            body: new URLSearchParams(formData),
                            headers: {
                                'Content-Type': 'application/x-www-form-urlencoded',
                                'X-Requested-With': 'XMLHttpRequest'
                            }
                        });

                        if (response.ok) {
                            await openDrawer(taskId, false);
                            updateBoardCardHours(taskId);
                        } else {
                            alert('Failed to log time. Please check your inputs.');
                            if (submitBtn) submitBtn.disabled = false;
                        }
                    } catch (err) {
                        console.error('Error logging time:', err);
                        alert('An error occurred while logging time.');
                        if (submitBtn) submitBtn.disabled = false;
                    }
                });
            }

            // Upload Attachment form submit
            const uploadForm = drawer.querySelector('#drawerUploadAttachmentForm');
            if (uploadForm) {
                uploadForm.addEventListener('submit', async (e) => {
                    e.preventDefault();
                    const submitBtn = uploadForm.querySelector('button[type="submit"]');
                    if (submitBtn) submitBtn.disabled = true;

                    const action = uploadForm.getAttribute('action');
                    const formData = new FormData(uploadForm);

                    try {
                        const response = await fetch(action, {
                            method: 'POST',
                            body: formData,
                            headers: {
                                'X-Requested-With': 'XMLHttpRequest'
                            }
                        });

                        if (response.ok) {
                            await openDrawer(taskId, false);
                        } else {
                            alert('Upload failed. Check file size (max 50MB) and format.');
                            if (submitBtn) submitBtn.disabled = false;
                        }
                    } catch (err) {
                        console.error('Error uploading file:', err);
                        alert('An error occurred during file upload.');
                        if (submitBtn) submitBtn.disabled = false;
                    }
                });
            }

            // Delete Time Entry form submit
            const deleteTimeForms = drawer.querySelectorAll('.drawer-delete-time-form');
            deleteTimeForms.forEach(form => {
                form.addEventListener('submit', async (e) => {
                    e.preventDefault();
                    const action = form.getAttribute('action');
                    const formData = new FormData(form);

                    try {
                        const response = await fetch(action, {
                            method: 'POST',
                            body: new URLSearchParams(formData),
                            headers: {
                                'Content-Type': 'application/x-www-form-urlencoded',
                                'X-Requested-With': 'XMLHttpRequest'
                            }
                        });

                        if (response.ok) {
                            await openDrawer(taskId, false);
                            updateBoardCardHours(taskId);
                        } else {
                            alert('Failed to remove time entry.');
                        }
                    } catch (err) {
                        console.error('Error deleting time entry:', err);
                        alert('An error occurred.');
                    }
                });
            });

            // Delete Attachment form submit
            const deleteAttachForms = drawer.querySelectorAll('.drawer-delete-attachment-form');
            deleteAttachForms.forEach(form => {
                form.addEventListener('submit', async (e) => {
                    e.preventDefault();
                    const action = form.getAttribute('action');
                    const formData = new FormData(form);

                    try {
                        const response = await fetch(action, {
                            method: 'POST',
                            body: new URLSearchParams(formData),
                            headers: {
                                'Content-Type': 'application/x-www-form-urlencoded',
                                'X-Requested-With': 'XMLHttpRequest'
                            }
                        });

                        if (response.ok) {
                            await openDrawer(taskId, false);
                        } else {
                            alert('Failed to remove attachment.');
                        }
                    } catch (err) {
                        console.error('Error deleting attachment:', err);
                        alert('An error occurred.');
                    }
                });
            });
        }

        function updateBoardCardHours(taskId) {
            const card = document.getElementById(`card-${taskId}`);
            const drawer = document.getElementById('task-preview-drawer');
            if (!card || !drawer) return;

            const textEl = drawer.querySelector('.drawer-time-tracking-text');
            if (!textEl) return;

            const match = textEl.textContent.match(/([\d.]+)h\s+logged\s*\/\s*([\d.]+)h\s+est/);
            if (match) {
                const logged = parseFloat(match[1]);
                const est = parseFloat(match[2]);

                const cardHours = card.querySelector('.kanban-card-hours');
                if (cardHours) {
                    cardHours.textContent = `\n                                ${logged}h / ${est}h\n                            `;
                }

                const cardProgressBar = card.querySelector('.kanban-card-progress-bar');
                if (cardProgressBar) {
                    const percent = est > 0 ? Math.min(100, Math.round((logged / est) * 100)) : 0;
                    cardProgressBar.style.width = `${percent}%`;
                    cardProgressBar.setAttribute('aria-valuenow', percent);

                    if (percent >= 100 && est > 0 && logged > est) {
                        cardProgressBar.classList.remove('bg-primary');
                        cardProgressBar.classList.add('bg-danger');
                    } else {
                        cardProgressBar.classList.remove('bg-danger');
                        cardProgressBar.classList.add('bg-primary');
                    }
                }
            }
        }
    });
})();
