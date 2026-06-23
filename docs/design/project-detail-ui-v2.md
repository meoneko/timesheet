# Project Detail UI/UX Redesign (v2)

> Design proposal for `/Projects/{id}` (currently `Views/Projects/Details.cshtml`).
> Author: TTMS dev team. Status: **Draft — awaiting sign-off**.
> Last updated: see git log.

---

## 1. Goals & non-goals

### Goals
1. Show project **status at a glance** — a viewer should understand health, progress and recent activity within 5 seconds of landing on the page.
2. Surface **key metrics** without making the user click into sub-pages (open tasks, time logged this week, members, last activity).
3. Make **permissioned actions obvious** — Edit/Delete/Members must never appear for a Viewer; the Tasks CTA must always be visible.
4. Support **URL deep-linking** for each tab so links are shareable and browser Back/Forward behaves as expected.
5. Stay within the **MVP philosophy** (simplicity over features) — no charts libs, no animation libs, no JS frameworks. Bootstrap 5 + a few lines of vanilla JS only.

### Non-goals
- Adding new domain data. We will not introduce "project health score" or "team velocity" — out of scope per spec section 20.
- Real-time updates. The page is server-rendered; refresh to see new data.
- Mobile-first redesign. Mobile gets a usable experience but desktop is the primary surface.
- Replacing the dedicated `Members` page. That page stays for bulk operations; this design adds an inline-add capability inside the Members tab.

---

## 2. Information architecture

The page is split into four horizontal bands. From top to bottom:

```
+---------------------------------------------------------------+
| BAND 1 — Breadcrumb                                          |
+---------------------------------------------------------------+
| BAND 2 — Project header card (identity + KPI strip)          |
+---------------------------------------------------------------+
| BAND 3 — Sticky tab bar (Overview / Members / History)       |
+---------------------------------------------------------------+
| BAND 4 — Tab content panel                                   |
+---------------------------------------------------------------+
```

Sticky behavior: Band 1 is not sticky (it scrolls away with the page). Band 2 is not sticky (it would steal too much viewport on small screens). Band 3 IS sticky (`position: sticky; top: <topbar-height>`) so the user can switch tabs without scrolling back up.

---

## 3. Wireframe — Band 2 (Project header card)

The current page puts every meta field (status, task count, creator, role) on a single line. It is hard to scan and the role badge gets visually buried. The redesigned header is a Bootstrap card with two rows: an identity row and a KPI strip.

### 3.1. Desktop (≥ md)

```
+--------------------------------------------------------------------------+
| +-------------------------------------------------------------+ [ ... ]  |
| | [ACME-123]  Project Northwind Migration              [Active] |  |        |
| | Project description preview · one line, truncated.         |  |        |
| | Your role: Owner                                            |  |        |
| +-------------------------------------------------------------+ |        |
|                                                                          |
| +-------------+ +-------------+ +-------------+ +-------------------+   |
| | OPEN TASKS  | | TIME (WEEK) | | TIME (TOTAL)| | MEMBERS           |   |
| |     12      | |    18.5 h   | |   142.0 h   | |     5            |   |
| | of 27 total | | this week   | | all-time    | | 2 owners, 3 mems  |   |
| +-------------+ +-------------+ +-------------+ +-------------------+   |
|                                                                          |
| [ + New task ]  [ View tasks ]  [ Edit ]  [ Members ]  [ ... ]  [ Del ] |
+--------------------------------------------------------------------------+
```

The right-aligned `[ ... ]` slot in the identity row holds the kebab dropdown (Export, Copy code, Archive if allowed).

### 3.2. Mobile (< md)

The header collapses to a stacked card. KPI tiles arrange 2x2. Action buttons wrap into a single full-width row with the primary action (`New task`) above the rest.

```
+----------------------------------+
| [ACME-123]  Project Northwind    |
| Migration                  [Active]|
| Your role: Owner                  |
+----------------------------------+
| +-------------+ +-------------+  |
| | OPEN TASKS  | | TIME (WEEK) |  |
| |   12/27     | |   18.5 h    |  |
| +-------------+ +-------------+  |
| +-------------+ +-------------+  |
| | TIME (TOTAL)| | MEMBERS     |  |
| |   142.0 h   | |     5       |  |
| +-------------+ +-------------+  |
+----------------------------------+
| [ + New task ]                    |
| [ View tasks ]  [ Edit ]         |
| [ Members ]    [ Delete ]        |
+----------------------------------+
```

### 3.3. Action button grouping (rationale)

Currently the page has one button group with Edit / Members / Tasks / Delete all side-by-side. The redesign groups them by intent:

| Group        | Buttons                          | Visibility            |
|--------------|----------------------------------|-----------------------|
| Primary      | `+ New task`, `View tasks`       | Always                |
| Manage       | `Edit`, `Members`                | Owner / Admin         |
| Destructive  | `Delete`                         | Owner / Admin         |
| Overflow     | `Export to Excel`, `Copy code`   | Always (in kebab)     |

Visual treatment: primary actions are `btn-primary` filled; manage actions are `btn-outline-secondary`; destructive is `btn-outline-danger` and moved into its own slot so it can never be confused with a navigation action. The kebab uses Bootstrap `btn-group` with `dropdown-toggle-split`.

---

## 4. Wireframe — Band 3 & 4 (Tabs + tab content)

### 4.1. Tab bar (sticky)

```
+---------------------------------------------------------------------+
| ( Overview )  Members (5)   History          ? Help                |
+---------------------------------------------------------------------+
```

Tabs are real anchor links to the same page with `?tab=...` rather than Bootstrap JS toggles. This makes them bookmarkable, shareable, and Back/Forward-safe.

* `?tab=overview` (default, omitted from URL).
* `?tab=members`.
* `?tab=history`.

A small JS snippet reads `tab` from the query string on load and shows the matching pane. On tab click it pushes a `history.pushState` entry instead of doing a full page reload. A `popstate` handler restores the right pane.

### 4.2. Overview tab

The current Overview tab is just a description block. The redesign adds three sub-sections so a viewer can answer "what is the state of this project?" without leaving the page.

```
+---------------------------------------------------------------------+
| Description                                                          |
| Rich-text content from TinyMCE, sanitized.                          |
| If empty and viewer can manage: "No description yet. [ Add one ]"  |
+---------------------------------------------------------------------+

| At a glance                                                          |
| +---------------+ +----------------+ +---------------------------+   |
| | TASKS         | | TIME THIS WEEK | | MEMBERS                   |   |
| | [bar]         | | [mini bars]    | | [avatar][avatar][avatar]  |   |
| | 5 todo        | | M T W T F S S  | | + 2 more                  |   |
| | 4 in progress | | 4 6 2 0 4 2 0  | |                           |   |
| | 2 blocked     | |                 | |                           |   |
| | 1 done today  | |                 | |                           |   |
| +---------------+ +----------------+ +---------------------------+   |
+---------------------------------------------------------------------+

| Recent tasks                                  [ See all tasks → ]   |
| +---+-----------+----------+--------+--------+--------+            |
| | # | Title     | Assignee | Status | Due     | Logged |           |
| +---+-----------+----------+--------+--------+--------+            |
| | 12| Migrate   | Alice    | InPrg  | 2026-07 |  3.5 h |            |
| | 11| Set up CI | Bob      | Todo   | 2026-08 |  0.0 h |            |
| | 10| Audit log | Carol    | Pend.  | 2026-07 |  1.0 h |            |
| | 9 | Backup    | Dave     | Done   | 2026-06  |  2.0 h |            |
| +---+-----------+----------+--------+--------+--------+            |
+---------------------------------------------------------------------+

| Recent activity                              [ See full history → ] |
| 5 history rows, same shape as the History tab.                      |
+---------------------------------------------------------------------+
```

**Notes:**
* "At a glance" cards are deliberately simple HTML/CSS — no chart library. The task-status card is a vertical stacked bar showing the ratio of each status. The time-this-week card is 7 bars drawn with inline `<div>` heights.
* "Recent tasks" uses the existing `Tasks/Index` page query — we already filter by project + soft-delete. We add a `Take(5)` and order by `UpdatedAt desc`.
* If the project has zero tasks, the "Recent tasks" section hides itself with a one-line message ("No tasks yet.") instead of an empty table.

### 4.3. Members tab

The current Members tab is a read-only table with a link to a separate Members management page. The redesign makes adding a member a one-step inline form when the viewer can manage.

```
+---------------------------------------------------------------------+
|  ( Owner-only: )  [ Add user v ]   [ Role v ]   [ + Add ]           |
+---------------------------------------------------------------------+
| +--+-----------+-------------------+----------+--------+--------+ |
| |  | Name      | Email             | Role     | Joined | Actions | |
| +--+-----------+-------------------+----------+--------+--------+ |
| |AA| Alice N.  | alice@example.com | [Owner]  | 2026-01| (you)   | |
| |BB| Bob K.    | bob@example.com   | [Owner]  | 2026-01| Promote | |
| |CC| Carol P.  | carol@example.com | [Member] | 2026-03| Demote  | |
| |  |           |                   |          |        | Remove  | |
| +--+-----------+-------------------+----------+--------+--------+ |
```

**Changes from current:**
* Owner-row actions are inline: a dropdown with **Promote / Demote / Make Viewer / Remove**.
* Last Owner cannot be demoted or removed — the service already returns `LastOwner`; we surface it as a disabled option in the dropdown plus an inline alert.
* The separate `/Projects/{id}/Members` page is kept and linked from the page-level kebab ("Manage members") for future bulk operations (out of MVP scope but the link is there).

### 4.4. History tab

The current History tab shows raw Old/New columns. The redesign adds visual diff, filtering and proper empty states.

```
+---------------------------------------------------------------------+
|  Filter: [ All events v ]   [ All users v ]    Showing 10 of 47    |
+---------------------------------------------------------------------+
| When          | Who     | Event          | Change                  |
|---------------|---------|----------------|-------------------------|
| 2 min ago     | Alice   | [+] Created    | -  →  ACME-123          |
| 1 h ago       | Bob     | [~] Updated    | Name: ~~foo~~ ACME-123  |
| 2026-06-22    | Alice   | [o] StatusChg  | ~~Draft~~ Active        |
| 2026-06-21    | System  | [+] MemberAdd  | Bob as Member           |
+---------------------------------------------------------------------+
|                       [ Load more (37 remaining) ]                   |
+---------------------------------------------------------------------+
```

**Diff rendering rule:**
* Single-value field (Code, Name, Status, Priority, Role): render Old and New as two pills with a `<del>` and `<ins>` style.
* Long text (Description): truncate to 80 chars and add a "view diff" popover with the full content.
* Created event: only the New pill is shown.
* Deleted event: only the Old pill is shown, faded out.

**Load-more pattern:** initial server render includes the first 10 rows. The "Load more" button calls a new endpoint `GET /Projects/{id}/History?skip=10&take=10` and appends rows. No infinite scroll (it is hostile to screen readers and to the browser Back button).

---

## 5. Data and service changes

### 5.1. ViewModel additions

`ProjectDetailViewModel` is extended (additive only — no breaking changes to `Edit`, `Delete`, `Restore` views).

```csharp
// New properties on ProjectDetailViewModel
public int TotalTimeLoggedMinutes { get; set; }            // all-time
public int TimeLoggedMinutesThisWeek { get; set; }         // last 7 days, rolling
public DateTime? LastActivityAt { get; set; }              // max(Histories.ChangedAt)
public List<TaskSummaryItem> RecentTasks { get; set; } = new();   // top 5
public Dictionary<TaskItemStatus, int> TaskStatusCounts { get; set; } = new();
public List<int> TimeLoggedPerDayThisWeek { get; set; } = new();  // 7 ints, Mon..Sun
```

A new lightweight `TaskSummaryItem` (lives next to `ProjectViewModels.cs`):

```csharp
public class TaskSummaryItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? AssigneeName { get; set; }
    public TaskItemStatus Status { get; set; }
    public DateTime? DueDate { get; set; }
    public int TotalLoggedMinutes { get; set; }
}
```

### 5.2. `ProjectService.GetDetailAsync` changes

The current implementation already pulls members and recent history. We add four small queries inside the same call so we still hit the DB once for the hot path. Order:

1. Project (already).
2. Members (already).
3. Recent history (already, take 10).
4. **NEW**: `TaskStatusCounts` — `GroupBy(Status).Select(g => new { g.Key, Count = g.Count() })` on non-deleted tasks.
5. **NEW**: `RecentTasks` — `Where(ProjectId == id && !IsDeleted).OrderByDescending(UpdatedAt).Take(5).Select(...)`.
6. **NEW**: `TotalTimeLoggedMinutes` and `TimeLoggedMinutesThisWeek` — sum of `TimeEntry.DurationMinutes` joined to the project, with optional `Where(WorkDate >= DateTime.UtcNow.Date.AddDays(-7))` for the weekly slice.
7. **NEW**: `LastActivityAt` — `Max(ChangedAt)` over the project's history.

Performance budget: the spec target is <2s page load. Adding four aggregation queries that hit indexed columns (`ProjectId`, `IsDeleted`, `WorkDate`) keeps the total well under budget. We do **not** introduce a new endpoint — this is one round-trip with EF Core `AsNoTracking()` and explicit `Select` projections so we never pull full entities.

### 5.3. New endpoint (history pagination)

```
GET /Projects/{id}/History?skip={int}&take={int}&event={enum?}&userId={string?}
Authorization: CanViewProjectAsync (same as Details).
Response: PartialView of the next page of HistoryRowViewModel.
```

Returns 200 with rendered rows (HTML), not JSON. The view uses a partial (`_HistoryRowsPartial.cshtml`) to render the rows so both the initial load and the AJAX load share markup.

### 5.4. Indexes to add

`ProjectService.GetDetailAsync` runs the following new queries; we ensure the matching indexes exist in the EF migration that ships with this redesign:

* `IX_Tasks_ProjectId_IsDeleted_UpdatedAt` (covers RecentTasks + TaskStatusCounts).
* `IX_TimeEntries_TaskId_WorkDate` (already likely present from Step 8; verify and add if missing).
* `IX_Histories_Entity_EntityId_ChangedAt` (already present from Step 7 for the History tab; verify).

---

## 6. Accessibility, responsive and CSS

### 6.1. Accessibility checklist

* All tab buttons have `aria-controls` pointing to the matching pane `id`.
* The active tab button has `aria-selected="true"`; others `aria-selected="false"`.
* Each KPI tile is wrapped in a `<section>` with an `aria-labelledby` referencing the tile title (`<h2 id="kpi-tasks">Open tasks</h2>`). Screen readers read "Open tasks: 12 of 27" without ambiguity.
* Owner-only inline forms have `<fieldset disabled>` when the viewer cannot manage, instead of being hidden. This avoids the focus trap of "mystery disabled controls".
* All action buttons have descriptive text. Icons alone (`<i class="bi bi-trash">`) are given `aria-hidden="true"` and a `<span class="visually-hidden">Delete project</span>` sibling.
* Color is never the sole indicator. The status badge adds an icon (`bi-check-circle`, `bi-pause-circle`, `bi-archive`, ...). The "active" tab also gets an underline border in addition to background color.
* Focus order: breadcrumb → identity → KPI tiles (skip-link target with `tabindex="-1"` so screen readers can jump past them) → tab bar → tab content.

### 6.2. Responsive breakpoints

| Breakpoint | Behavior                                            |
|------------|-----------------------------------------------------|
| `< sm`     | Header stacks. KPI tiles 2x2. Action buttons stack full-width. Members table becomes card list (one member per card with stacked fields). |
| `sm - md`  | Header inline. KPI tiles 4 across. Members table compact (hide Joined column). |
| `≥ md`     | Full layout as in wireframes.                       |

### 6.3. New CSS classes (added to `wwwroot/css/project-detail.css`)

A new stylesheet is added (loaded only on this view via `ViewData["Styles"]`). It keeps the design tokens close to the existing `sidebar.css`:

```css
.ttms-pd-header { /* card wrapper */ }
.ttms-pd-identity { /* identity row */ }
.ttms-pd-kpi-grid { display: grid; gap: .75rem; grid-template-columns: repeat(4, 1fr); }
@media (max-width: 767.98px) { .ttms-pd-kpi-grid { grid-template-columns: repeat(2, 1fr); } }
.ttms-pd-kpi { /* KPI tile */ }
.ttms-pd-kpi-label { font-size: .75rem; text-transform: uppercase; letter-spacing: .05em; color: #6c757d; }
.ttms-pd-kpi-value { font-size: 1.75rem; font-weight: 600; line-height: 1.1; }
.ttms-pd-kpi-meta { font-size: .8rem; color: #6c757d; }
.ttms-pd-status-bar { /* stacked bar for task status counts */ display: flex; height: 8px; border-radius: 4px; overflow: hidden; }
.ttms-pd-status-bar > div { height: 100%; }
.ttms-pd-week-bars { display: flex; align-items: flex-end; gap: 4px; height: 60px; }
.ttms-pd-week-bars > div { flex: 1; background: #0a58ca; border-radius: 2px 2px 0 0; min-height: 2px; }
.ttms-pd-tabs { position: sticky; top: 56px; /* matches topbar height */ background: white; z-index: 10; }
.ttms-pd-diff-del { color: #b02a37; background: #f8d7da; text-decoration: line-through; padding: 0 .25rem; border-radius: 2px; }
.ttms-pd-diff-ins { color: #146c43; background: #d1e7dd; padding: 0 .25rem; border-radius: 2px; }
```

### 6.4. JS additions

A small inline `<script>` at the bottom of the view (or factored into `site.js`):

* On `DOMContentLoaded`: read `?tab=...` and activate that pane. If absent or unknown, default to Overview.
* On tab click: `event.preventDefault(); history.pushState({tab: id}, "", "?tab=" + id); activatePane(id);`.
* On `popstate`: re-read `?tab` and activate the matching pane.
* On filter change (History tab): replace the table body via `fetch` to `GET /Projects/{id}/History?event=...&userId=...&skip=0&take=10`. We use `fetch` (not jQuery) and return server-rendered HTML — no client-side templating.
* On "Load more": same endpoint with the current `skip += 10`. Append rows to the existing `<tbody>`.

All JS is defensive: if any element is missing, the script silently does nothing rather than throwing.

---

## 7. File touch list

### Files modified

| File                                                       | Change                                                                 |
|------------------------------------------------------------|------------------------------------------------------------------------|
| `src/TTMS.Web/Views/Projects/Details.cshtml`               | Full rewrite against new wireframe.                                    |
| `src/TTMS.Web/Views/Projects/_HistoryRowsPartial.cshtml`   | NEW. Renders rows for both initial render and AJAX load-more.          |
| `src/TTMS.Web/Views/Projects/Members.cshtml`               | Minor: remove the link "Go back to Overview" if it was there.          |
| `src/TTMS.Web/Models/ViewModels/ProjectViewModels.cs`      | Extend `ProjectDetailViewModel`. Add `TaskSummaryItem`.               |
| `src/TTMS.Web/Services/Interfaces/IProjectService.cs`      | New method: `Task<List<HistoryRowViewModel>> GetHistoryPageAsync(...)`. |
| `src/TTMS.Web/Services/ProjectService.cs`                  | Extend `GetDetailAsync` with new aggregations. Implement `GetHistoryPageAsync`. |
| `src/TTMS.Web/Controllers/ProjectsController.cs`           | New `History(int id, int skip, int take, ...)` action returning PartialView. Pass new `ViewData["Styles"]` on `Details`. |
| `src/TTMS.Web/wwwroot/css/project-detail.css`              | NEW. Stylesheet described in section 6.3.                              |
| `src/TTMS.Web/wwwroot/js/project-detail.js`                | NEW. Tab deep-link + load-more.                                        |
| `src/TTMS.Web/Views/Shared/_Layout.cshtml`                 | No structural change. Optionally include the new CSS/JS only on the project detail view via `ViewData["Styles"]` / `ViewData["Scripts"]` sections. |

### Files NOT modified

* `ProjectsController.{Create,Edit,Delete,Restore}` — unchanged.
* `ProjectService.{Create,Update,SoftDelete,Restore,AddMember,...}` — unchanged.
* `DbSeeder.cs` — unchanged.
* Any other view — unchanged.

### Migrations

A single EF migration `AddProjectDetailAggregations` adds the indexes listed in 5.4. It does not change schema shape (only `CreateIndex` operations).

---

## 8. Acceptance criteria

Before this redesign is merged, the following must hold:

### Functional
- [ ] All existing functionality still works: Edit, Members management, Delete, Restore, role guards.
- [ ] `?tab=members` and `?tab=history` deep-links open the correct tab.
- [ ] Browser Back/Forward navigates between tabs without a full page reload.
- [ ] Load-more on History tab fetches the next 10 rows without duplicating the first 10.
- [ ] Owner-only inline "Add member" form is hidden for Viewers and disabled (with explanation) for Members.
- [ ] Empty states show the right CTA: "Add description" for Owners, "No description yet." for Viewers.
- [ ] Last Owner cannot be demoted or removed; the service `LastOwner` error surfaces inline.
- [ ] KPI numbers match the raw numbers shown on the History tab and the Members tab (cross-checked).

### Non-functional
- [ ] Lighthouse a11y score ≥ 95 on the Details page.
- [ ] Page load < 2s on the dev DB with a project that has 100 tasks and 500 history rows.
- [ ] No new JS dependencies; total transferred JS for this view stays under 10 KB minified.
- [ ] No new CSS framework; project-detail.css is under 5 KB.
- [ ] Keyboard-only navigation can reach every interactive element and operate it.

### Visual
- [ ] Stakeholder review (project owner / admin user) signs off on the wireframe before implementation begins.
- [ ] Stakeholder signs off on the implemented result before merge.

---

## 9. Out of scope (deferred to a future design doc)

These are deliberately NOT part of this redesign to keep the diff small and aligned with the MVP spec:

* Drag-to-reorder tasks on the Overview tab.
* Inline-edit task title or status from the Recent tasks list.
* Project-level charts (velocity, burndown).
* Comments / discussion thread on the project.
* Notifications when a project is updated.
* Export the redesigned page to PDF.
* Customizable dashboard widgets.

Each of these should get its own design doc when prioritized.

---

## 10. Open questions for the reviewer

1. Should the "Time this week" KPI use a rolling 7-day window or the current ISO week (Mon-Sun)? The wireframe assumes rolling; the service is cheaper with ISO week but the meaning is less intuitive.
2. Should the Recent tasks list filter to "only assigned to me" by default, or always show the project-wide top 5?
3. Should we keep the existing `/Projects/{id}/Members` page as a thin alias that redirects to `?tab=members`, or leave it as-is?
4. Is the kebab overflow menu worth its weight given that the MVP philosophy is "simplicity over features"? It can be cut without losing any required function.
5. KPI tile click-throughs — should clicking "Open tasks" jump to the Tasks index pre-filtered by `status != Done && status != Cancelled`, or to the unfiltered index? The current spec does not specify.

---

*End of design doc. Awaiting review.*
