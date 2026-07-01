# TTMS — User Guide (v1.0)

A walkthrough for non-technical end users.

> **Version:** 1.0 MVP | **Date:** 2026-07-01 | **Tests:** 267 passed

## Getting Started

### Login
1. Visit the TTMS URL (provided by your admin)
2. Enter your email and password
3. On first login with the default admin account, you'll be prompted to change your password

### Navigation
- **Dashboard** — Your personal overview (today's hours, weekly/monthly stats, open/completed tasks)
- **Admin Dashboard** (Admin only) — System-wide KPIs, user activity, top users, paged users table
- **Projects** — List of projects you have access to
- **Reports** — Timesheet and summary reports with Excel export
- **Recycle Bin** — View and restore deleted items
- **Search** — Search across all tasks and time entries from the nav bar

---

## Projects

### Viewing a Project
Click any project on the Projects page to see:
- **Overview tab:** KPI cards, task breakdown, weekly activity chart, recent tasks and history
- **Members tab:** See who's on the project (Owners, Members, Viewers)
- **History tab:** Full audit trail with filters by event type and user

### Project Roles
- **Owner** — Can edit project details, manage members, delete the project
- **Member** — Can create tasks, log time, upload files
- **Viewer** — Read-only access

---

## Tasks

### Creating a Task
1. Open a project
2. Click **New Task**
3. Fill in:
   - **Title** (required)
   - **Description** (rich text — bold, lists, tables supported)
   - **Assignee** (required — must be a project member)
   - **Status** (default: Todo)
   - **Priority** (Low / Medium / High / Critical)
   - **Estimated Hours** (optional)
   - **Due Date** (optional)
4. Click **Create**

### Task Board (Kanban View)
On the Tasks page, toggle to **Board** view to see a Jira-style Kanban.
- **6 columns:** Todo / InProgress / Pending / Blocked / Done / Cancelled
- **Drag & drop:** Move a card to change status instantly (desktop)
- **Mobile fallback:** Use the dropdown in each card on phones/tablets
- **Click a card** to open the side preview drawer with full details, time entries, attachments, and history
- **Blocked status:** A modal asks for a reason (required, max 500 chars)
- **Concurrency protection:** If another user changed the same task, you'll get a notification and the board refreshes

### Editing a Task
1. Open a task → click **Edit**
2. Make changes → click **Save**

### Status Changes
- Use the dropdown on the task detail page for quick status updates
- **Blocked** status requires a reason (e.g., "Waiting for API access")

---

## Logging Time

1. Open a task → go to the **Time Entries** tab
2. Click **Log Time**
3. Fill in:
   - **Work Date** (defaults to today)
   - **Duration** (decimal hours, e.g. 2.5 = 2 hours 30 minutes)
   - **Work Log** (rich text — describe what you worked on)
4. Click **Create**

### Editing/Deleting Time Entries
- You can edit or delete your own entries
- Admin can edit/delete any entry
- Deleted entries go to the Recycle Bin

---

## Attachments

### Uploading Files
1. Open a task → scroll to the Attachments tab
2. Click **Choose File** → select a file (max 50 MB)
3. Allowed types: images (jpg, png, gif, webp), documents (pdf, docx, xlsx, txt, csv), archives (zip, rar, 7z)

### Downloading
Click the file name — opens/downloads in your browser.

### Deleting
Only the uploader, a Project Owner, or an Admin can remove attachments.

---

## Reports

Navigate to **Reports** from the sidebar. Four reports available:

### My Timesheet
Your own time entries, filterable by date range and project.

### Team Timesheet (Admin only)
All users' time entries, grouped by user then by project.

### Project Summary
Per-task estimated vs actual hours with variance.

### User Summary (Admin only)
Per-user breakdown with project buckets.

### Excel Export
All reports have an **Export to Excel** button — downloads as `.xlsx` with totals.

---

## Recycle Bin

Access from the sidebar. Shows soft-deleted Projects, Tasks, and Time Entries.

- **Restore:** Click the restore button to bring an item back
- **Permission:** Admin can restore anything; for projects, only the Owner can restore

---

## Search

Use the search box in the navigation bar. Results show matching:
- **Tasks** — by title or description
- **Time Entries** — by work log content

Type and either wait (auto-submits after a short delay) or press Enter.

---

## Tips

- **Estimated vs Actual** is shown on every task list and detail page — green means under budget, red means over
- **History** is tracked for every change — see who changed what and when on the detail pages
- **Soft delete** keeps data recoverable — items go to the Recycle Bin, not permanently gone