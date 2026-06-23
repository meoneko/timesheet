---
name: TTMS Project Guide
description: Conventions and workflows for the TTMS (Task and Time Management System) project
---

# CONTINUE.md - Task and Time Management System (TTMS)

> Note: This file lives at `.continue/rules/CONTINUE.md` and is automatically loaded into context by Continue when working in this project. Edit it as the source of truth for project conventions.

---

## 1. Project Overview

### Purpose
TTMS (Task and Time Management System) is a lightweight internal web application that replaces Excel-based task and timesheet tracking. It provides user authentication, project management, task tracking, manual time logging, rich documentation, file attachments, full audit history, reporting, and Excel export.

### MVP Philosophy
- Simplicity over features - explicitly NOT a Jira/Azure DevOps/OpenProject replacement.
- Fast implementation and easy maintenance.
- Designed for non-technical end users.
- Hosted on IIS / Windows Server.

### Key Technologies
- Backend: ASP.NET Core 8 MVC
- ORM: Entity Framework Core
- Auth: ASP.NET Core Identity
- Frontend: Razor Views + Bootstrap 5 + minimal JavaScript
- Rich Text: TinyMCE (CKEditor accepted alternative)
- Database: SQL Server
- File Storage: Local disk under /uploads/
- Excel Export: ClosedXML
- Hosting: IIS on Windows Server

### High-Level Architecture
A classic server-rendered MVC monolith:
- Razor views render server-side; minimal JS for UX niceties.
- EF Core talks to SQL Server.
- Identity manages users/roles; an additional Project Membership layer (Owner/Member/Viewer) controls per-project access.
- Files stored on local disk, not in the database.
- A single Histories table records an audit trail for Task / TimeEntry / Attachment changes.

---

## 2. Getting Started

> Warning: Sections below assume the standard project layout. Verify against the actual csproj, Program.cs, and appsettings.json once generated.

### Prerequisites
- .NET 8 SDK (`dotnet --version` should report 8.x)
- SQL Server (2019+ recommended; LocalDB is fine for dev)
- IIS with the ASP.NET Core Hosting Bundle (for production deploys)
- A code editor (Visual Studio 2022 / VS Code / Rider)
- PowerShell or a POSIX shell

### Installation (expected)
```
dotnet restore
dotnet ef database update
dotnet run
```
Open https://localhost:5001 (or the port printed in the console). The first run will seed the default Admin user via Identity.

### First-Time Setup Checklist
- [ ] Confirm appsettings.json connection string points to your SQL Server.
- [ ] Confirm the uploads root folder (/uploads/) is writable by the IIS app pool identity.
- [ ] Log in as the seeded admin and change the default password.
- [ ] Create the first project, then add project members.
- [ ] Create a task and log a time entry to verify the audit history writes correctly.

### Running Tests
```
dotnet test
```
The MVP spec does not mandate a test project, but unit tests for services (time calculation, history writing, sanitization) are recommended.

---

## 3. Project Structure

> Warning: Verify against the actual folder layout once scaffolding is complete. The layout below is the conventional ASP.NET Core MVC structure consistent with the spec.

```
/
|-- .continue/
|   `-- rules/
|       `-- CONTINUE.md          # This file
|-- docs/
|   `-- spec.md                  # Authoritative product specification (MVP v1.0)
|-- src/
|   |-- TTMS.Web/                # ASP.NET Core MVC project (entry point)
|   |   |-- Controllers/         # MVC controllers (Projects, Tasks, TimeEntries, Reports, ...)
|   |   |-- Models/              # EF Core entities + view models
|   |   |-- Views/               # Razor views (.cshtml)
|   |   |-- wwwroot/             # Static assets (Bootstrap, TinyMCE, JS, CSS)
|   |   |-- Services/            # Business logic (HistoryService, ExportService, SanitizationService)
|   |   |-- Data/                # DbContext, migrations, Identity config
|   |   |-- Areas/Identity/      # Identity UI scaffolding
|   |   |-- Program.cs           # App bootstrap
|   |   |-- appsettings.json     # Connection strings, file paths
|   |   `-- TTMS.Web.csproj
|   `-- TTMS.Tests/              # Optional: unit/integration tests
|-- uploads/                     # Local file storage (gitignored)
|   |-- tasks/{taskId}/
|   `-- timeentries/{timeEntryId}/
|-- .gitignore
`-- README.md
```

### Key Files and Their Roles
- docs/spec.md: Source of truth for MVP scope, fields, rules, and out-of-scope items. Update spec first, then code.
- Program.cs: DI registration, Identity, EF Core, middleware pipeline.
- appsettings.json: Connection strings, upload root, auth/cookie settings.
- Data/ApplicationDbContext.cs: EF Core DbContext with entities: Project, ProjectMember, Task, TimeEntry, Attachment, History.
- Services/HistoryService.cs: Centralized audit-trail writer. Call it on every mutation.
- Services/HtmlSanitizationService.cs: Sanitizes TinyMCE output (strips script, onclick, onload).
- Services/ExcelExportService.cs: ClosedXML-based exporters for the four report types.
- Controllers/ReportsController.cs: Serves My Timesheet, Team Timesheet, Project Summary, User Summary.
- Views/Shared/_Layout.cshtml: Top-level layout with Bootstrap 5 + TinyMCE includes.

### Configuration Files
- appsettings.json: connection strings, file storage root, cookie/auth settings.
- appsettings.Development.json: local overrides (do not commit secrets).
- csproj: pinned to net8.0; references EF Core, Identity, ClosedXML, HtmlSanitizer.

---

## 4. Development Workflow

### Coding Standards
- C# / .NET 8 conventions (nullable reference types enabled, file-scoped namespaces, primary constructors where appropriate).
- Follow standard ASP.NET Core MVC patterns: thin controllers, fat services, view models in Models/ViewModels/.
- Use async/await for all EF Core and I/O calls.
- Use soft delete (e.g., IsDeleted, DeletedAt) for Tasks, TimeEntries, Attachments - never hard-delete.
- HTML is always sanitized before persistence. Store both Html and a plain Text projection for searching.
- Every mutation must write a History record (Created/Updated/Deleted/Restored/StatusChanged/AssignedChanged for tasks; Created/Updated/Deleted/Restored for time entries; Added/Removed for attachments).
- Time durations are persisted as minutes internally (decimal hours on input/output).

### Branching and Commits (verify with team)
- Feature branches off main (e.g., feature/task-priority-filter).
- Conventional Commits encouraged: feat:, fix:, docs:, refactor:, test:.
- Small, focused PRs. Reference the spec section being implemented.

### Testing Approach
- Unit tests for: time to minutes conversion, history record shape, HTML sanitization rules, variance calculations (Estimated vs Actual), export column shape.
- Integration tests for: Identity registration/login, project membership authorization checks.
- Manual smoke test checklist per PR:
  1. Create a project, add a member, log in as that member and confirm visibility.
  2. Create a task, log a time entry, confirm Histories table has the expected rows.
  3. Upload an attachment to a task, confirm file is on disk under /uploads/tasks/{taskId}/.
  4. Export each of the 4 reports to Excel, confirm totals row matches DB.
  5. Edit a time entry, confirm OldValue/NewValue are written.

### Build and Deployment
```
dotnet build -c Release
dotnet publish -c Release -o ./publish
```
Deploy: copy ./publish to IIS site root; ensure ASP.NET Core Hosting Bundle is installed.
- Database: daily backups required.
- Uploads folder: daily backups required.
- Connection string and any secrets must come from environment variables or appsettings.Production.json (which is not committed).

### Contribution Guidelines
1. Read docs/spec.md first. If a change expands scope, update the spec and get sign-off before coding.
2. Keep changes in scope - see section 20 Out Of Scope in the spec.
3. Do not introduce: Kanban boards, timers, notifications, mobile app, REST API, AI features, PDF export, video upload.
4. PR must include: description, screenshots (if UI), migration notes, and a test/manual-verification note.

---

## 5. Key Concepts

### Roles
- Admin (system-wide): manages users, all projects, all tasks, all time entries, audit history, exports.
- User (system-wide): accesses only assigned projects; creates tasks; logs time; uploads files; exports own reports.

### Project Membership (per project)
- Owner: manages the project, its members, and all its tasks.
- Member: creates tasks, logs time, uploads files.
- Viewer: read-only.

### Domain Model
```
Project ---+- Task ---+- TimeEntry ---+- Attachment
          |         |              `- History
          |         |- Attachment
          |         `- History
          `- ProjectMember (UserId, Role)
```
History is a polymorphic audit table: Entity + EntityId + ChangedBy + ChangedAt + OldValue + NewValue.

### Task Statuses
- Todo: Not started
- InProgress: Actively being worked on
- Pending: Waiting on QA / Client / DevOps / etc.
- Blocked: Cannot continue - missing requirements, env, or permissions
- Done: Completed (still editable)
- Cancelled: Cancelled

### Priorities
Low, Medium, High, Critical

### Project Statuses
Active, Paused, Completed, Archived
- Only Active projects accept new tasks.
- Archived projects are read-only.
- Code and Name must be unique.

### Estimated vs Actual
- EstimatedHours is a per-task decimal.
- Time entries sum to Actual Hours (internally minutes, displayed as decimal hours).
- Variance = Actual - Estimate. Surfaced on dashboards.

### History Events
- Task: Created, Updated, Deleted, Restored, StatusChanged, AssignedChanged
- TimeEntry: Created, Updated, Deleted, Restored
- Attachment: Added, Removed

### Out of Scope (do NOT build)
Sprint, Epic, Story Points, Kanban Board, Calendar View, Timer Tracking, Approval Workflow, Notifications, Email Reminders, Mobile App, Multi-Company, REST API, Jira/Git Integrations, PDF Export, Video Upload, Task Templates, Recurring Tasks, AI Features.

---

## 6. Common Tasks

### Add a new project
1. As Admin, go to Projects then New.
2. Fill Name, Code (unique), Description, Status = Active.
3. Add at least one Owner in Project Membership.
4. Verify in list view; confirm only Active projects accept new tasks downstream.

### Add a project member
1. Open project, go to Members tab.
2. Select user, pick role (Owner / Member / Viewer), save.
3. Re-login as that user to confirm they see the project.

### Create a task
1. Open a project, go to Tasks then New.
2. Required: Title, DescriptionHtml (TinyMCE), Status, Priority, Assignee.
3. Optional: EstimatedHours, DueDate.
4. Submit, a History row of type Created is written.

### Log a time entry
1. Open task, click Log Time.
2. WorkDate, DurationHours (decimal, > 0), WorkLogHtml (TinyMCE).
3. Internally stored as minutes; rendered back as X.Yh.
4. History row of type Created is written.

### Edit a time entry
- Users can edit own entries.
- Admin can edit all entries.
- An Updated history row must capture OldValue to NewValue for the Duration field at minimum.

### Upload an attachment
- Allowed types: jpg, jpeg, png, gif, webp, pdf, docx, xlsx, txt, csv, zip, rar, 7z.
- Default size cap: 50MB.
- Path: /uploads/tasks/{taskId}/ or /uploads/timeentries/{timeEntryId}/.
- Authorization required to download.
- Soft delete on removal.

### Export a report to Excel
- Supported: My Timesheet, Team Timesheet, Project Summary, User Summary.
- Honors active filters and includes totals.
- Built with ClosedXML; company-compatible format.

### Add a new field to Task
1. Update docs/spec.md first.
2. Add the column to the Task entity in Data/ApplicationDbContext.cs.
3. Generate EF migration: `dotnet ef migrations add AddTaskXField`.
4. Update view models + Razor forms.
5. Update history capture to include the new field when it changes.

### Recover a soft-deleted record
- Soft-deleted Tasks/TimeEntries/Attachments can be Restored; this writes a Restored history row.

---

## 7. Troubleshooting

### EF Core migration fails
- Confirm SQL Server is reachable and the connection string in appsettings.json is correct.
- Run `dotnet ef migrations list` to inspect state.
- If multiple developers share a DB, regenerate migrations on a single branch to avoid drift.

### TinyMCE content is stripped on save
- The HTML sanitizer is intentionally strict. Forbidden by default: script, onclick, onload, inline event handlers.
- Whitelist additional tags/attributes in HtmlSanitizationService only after a security review.

### Uploaded file returns 404
- Confirm the file is on disk under the expected /uploads/{entity}/{id}/ path.
- Confirm the IIS app pool identity has read access to that folder.
- Confirm the controller authorization check (logged-in user + access to the parent task/time entry) is passing.

### Excel export is empty or missing totals
- Verify the same filter set is being applied to the export query as the on-screen report.
- Confirm ClosedXML is being disposed / used inside a `using` block to avoid file locks.

### History is missing entries
- The mutation must go through the service layer (which calls HistoryService). Direct DbContext.SaveChangesAsync from a controller bypasses audit logging.
- Check for swallowed exceptions in the history writer - they should at minimum be logged.

### Project shows read-only unexpectedly
- Only Active projects accept new tasks. Paused, Completed, and Archived are read-only.

### Performance: pages feel slow
- Target: <2s page load, <1s search, 50 concurrent users.
- Likely culprits: N+1 queries (use .Include), unindexed filter columns (ProjectId, AssigneeId, Status, WorkDate), and the full-text search on Text columns - add a full-text index in SQL Server if needed.

### Identity: cannot log in after deploy
- Confirm the ASP.NET Core Hosting Bundle version matches the .NET 8 runtime on the server.
- Confirm DataProtection keys are persisted (otherwise cookies invalidate on app restart).

---

## 8. References

- Product spec: docs/spec.md - authoritative MVP definition (v1.0).
- ASP.NET Core 8 docs: https://learn.microsoft.com/aspnet/core/?view=aspnetcore-8.0
- EF Core docs: https://learn.microsoft.com/ef/core/
- ASP.NET Core Identity: https://learn.microsoft.com/aspnet/core/security/authentication/identity
- ClosedXML: https://github.com/ClosedXML/ClosedXML
- TinyMCE: https://www.tiny.cloud/docs/
- Bootstrap 5: https://getbootstrap.com/docs/5.3/
- HtmlSanitizer (recommended): https://github.com/mganss/HtmlSanitizer

### Internal conventions to verify
- Branching model and PR template (team to confirm).
- Coding style rules (editorconfig, dotnet format settings).
- Test framework choice (xUnit recommended).
- Localization/i18n policy (currently out of scope).
- Specific connection string conventions per environment.

---

Last updated: see git log for docs/spec.md and this file. Keep this guide in sync with the spec - the spec is the source of truth.
