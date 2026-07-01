# Task & Time Management System (TTMS)

Version: 1.0

Status: Approved MVP

---

# 1. Overview

## 1.1 Purpose

Task & Time Management System (TTMS) is a lightweight internal web application designed to replace Excel-based task and timesheet tracking.

The system provides:

* User authentication
* Project management
* Task management
* Manual time logging
* Rich task documentation
* File attachments
* Audit history
* Reporting
* Excel export

The system is intentionally limited in scope and should not attempt to become a Jira, Azure DevOps, or OpenProject replacement.

---

## 1.2 MVP Philosophy

The MVP focuses on:

* Simplicity
* Fast implementation
* Easy maintenance
* IIS deployment
* Replacing Excel workflows

The system should remain lightweight and easy for non-technical users.

---

# 2. Technology Stack

## Backend

* ASP.NET Core 8 MVC
* Entity Framework Core
* ASP.NET Core Identity

## Frontend

* Razor Views
* Bootstrap 5
* Minimal JavaScript

## Database

* SQL Server

## File Storage

* Local disk storage

## Hosting

* IIS
* Windows Server

## Excel Export

* ClosedXML

## Rich Text Editor

Recommended:

* TinyMCE

Alternative:

* CKEditor

---

# 3. Roles

## Admin

Permissions:

* Manage users (grant/revoke Admin role at `/Admin/Users`)
* Manage projects
* Assign project members
* View all projects
* View all tasks
* View all time entries
* View audit history
* Export reports

---

## User

Permissions:

* Access assigned projects
* Create tasks
* Edit own tasks
* Create time entries
* Upload attachments
* View reports
* Export reports

---

## User Management (Admin only)

Admins can manage other users at `GET /Admin/Users`:

* List all registered users (Email, Full Name, Joined date, Admin/User role badge)
* Grant Admin role to any user via "Make Admin" button
* Revoke Admin role from any user via "Remove Admin" button
* Self-demotion is blocked (cannot remove own Admin role)
* Nav link under Admin section in sidebar

---

# 4. Core Domain Model

```text
Project
    └── Task
            └── TimeEntry

Task
    └── Attachment

TimeEntry
    └── Attachment

Task
    └── History

TimeEntry
    └── History
```

---

# 5. Project Management

## Description

A Project represents a product, application, service, or initiative.

Examples:

* Reco WebApp
* VietIQ Backend
* Internal Support
* Homelab

---

## Fields

| Field       | Type   | Required |
| ----------- | ------ | -------- |
| Name        | string | Yes      |
| Code        | string | Yes      |
| Description | string | No       |
| Status      | enum   | Yes      |

---

## Status

```text
Active
Paused
Completed
Archived
```

---

## Rules

* Only Active projects accept new tasks.
* Archived projects are read-only.
* Project Code must be unique.
* Project Name must be unique.

---

# 6. Project Membership

## Purpose

Controls which users can access a project.

---

## Roles

### Owner

Can:

* Manage project
* Manage members
* Manage all project tasks

### Member

Can:

* Create tasks
* Log time
* Upload files

### Viewer

Can:

* View only

---

# 7. Task Management

## Description

Task represents a unit of work.

A task can exist for multiple days.

A task can contain multiple time entries.

---

## Fields

| Field           | Type    | Required |
| --------------- | ------- | -------- |
| ProjectId       | FK      | Yes      |
| Title           | string  | Yes      |
| DescriptionHtml | html    | Yes      |
| Status          | enum    | Yes      |
| Priority        | enum    | Yes      |
| AssigneeId      | FK      | Yes      |
| EstimatedHours  | decimal | No       |
| DueDate         | date    | No       |

---

## Status

```text
Todo
InProgress
Pending
Blocked
Done
Cancelled
```

---

## Status Definition

### Todo

Task has not started.

### InProgress

Task is actively being worked on.

### Pending

Waiting for another party.

Examples:

* Waiting for QA
* Waiting for Client
* Waiting for DevOps

### Blocked

Cannot continue due to blocker.

Examples:

* Missing requirements
* Environment issue
* Permission issue

### Done

Task completed.

### Cancelled

Task cancelled.

---

## Priority

```text
Low
Medium
High
Critical
```

---

## Rules

* Project is required.
* Assignee is required.
* Status is required.
* Deleted tasks are soft deleted.
* Completed tasks remain editable.
* Every update must generate history.

---

# 8. Time Entry

## Description

Represents actual work performed.

Users log hours manually.

Timer functionality is excluded from MVP.

---

## Fields

| Field         | Type    | Required |
| ------------- | ------- | -------- |
| TaskId        | FK      | Yes      |
| WorkDate      | date    | Yes      |
| DurationHours | decimal | Yes      |
| WorkLogHtml   | html    | Yes      |

---

## Examples

### Example 1

```text
Task:
Fix D365 Retry Logic

Date:
2026-06-17

Hours:
2.5

Work Log:
Investigated retry failure.
Added retry handling.
Created integration tests.
```

---

### Example 2

```text
Task:
Implement Dashboard

Date:
2026-06-18

Hours:
3.0

Work Log:
Implemented statistics cards.
Added weekly report widget.
```

---

## Rules

* Duration > 0
* Decimal hours supported
* Internally stored as minutes
* Users can edit own entries
* Admin can edit all entries
* All changes generate history

---

# 9. Estimated vs Actual

## Purpose

Track estimation accuracy.

---

## Example

Task:

```text
Estimate: 4h
```

Time Entries:

```text
2h
1.5h
3h
```

Actual:

```text
6.5h
```

Variance:

```text
+2.5h
```

---

## Dashboard Metrics

Display:

* Estimated Hours
* Actual Hours
* Variance

---

# 10. Rich Text Editor

## Supported Features

* Bold
* Italic
* Underline
* Headings
* Lists
* Tables
* Links
* Code Block
* Image Upload

---

## Security

HTML must be sanitized.

Forbidden:

```html
<script>
onclick=
onload=
```

---

## Storage

Store:

```text
DescriptionHtml
DescriptionText
```

and

```text
WorkLogHtml
WorkLogText
```

Text version is used for searching.

---

# 11. Attachments

## Supported Types

### Images

```text
jpg
jpeg
png
gif
webp
```

### Documents

```text
pdf
docx
xlsx
txt
csv
```

### Archives

```text
zip
rar
7z
```

---

## Excluded from MVP

```text
mp4
mov
webm
```

---

## Storage

Path:

```text
/uploads/tasks/{taskId}/
```

and

```text
/uploads/timeentries/{timeEntryId}/
```

---

## Rules

* Default limit: 50MB
* Soft delete
* Authorization required

---

# 12. History Tracking

## Purpose

Maintain complete audit trail.

---

## Track Events

### Task

```text
Created
Updated
Deleted
Restored
StatusChanged
AssignedChanged
```

### TimeEntry

```text
Created
Updated
Deleted
Restored
```

### Attachment

```text
Added
Removed
```

---

## History Record

Stores:

* Entity
* EntityId
* ChangedBy
* ChangedAt
* OldValue
* NewValue

---

## Example

```text
Duration

Old:
2h

New:
2.5h
```

---

# 13. Search

## Supported Filters

* Project
* Assignee
* Status
* Priority
* Date Range

---

## Full Text Search

Search against:

* Task Title
* DescriptionText
* WorkLogText

---

# 14. Dashboard

Three read-only dashboards with server-side aggregation, no N+1 queries.

## 14.1 User Dashboard (`/Dashboard/User`)

KPI cards:
* Today's Hours
* Weekly Hours
* Monthly Hours
* Open Tasks
* Completed Tasks

Plus:
* Quick actions bar (New Task, Log Time)
* Recent Tasks table (last 10)
* Recent Time Entries table (last 10)

## 14.2 Project Dashboard (`/Dashboard/Project/{id}`)

KPI cards:
* Total Tasks / Open Tasks / Pending Tasks / Blocked Tasks
* Total Hours logged (all-time)
* Estimate vs Actual with progress bar (green/yellow/red thresholds)

Plus:
* Status breakdown bars
* Member load / time per member table
* Recent activity stream (last 20 history rows)

## 14.3 Admin Dashboard (`/Dashboard/Admin`)

Admin-only. KPI cards:
* Total Projects / Total Users / Total Tasks / Total Hours (month) / Active Projects

Plus:
* Recent activity stream (20 rows, all projects)
* Top Users this month (by hours logged)
* Paged Users table

---

# 15. Task Board (Kanban)

A lightweight Kanban-style board at `GET /Projects/{projectId}/Tasks?view=board`.

Features:
* Tasks grouped by status columns (Todo / InProgress / Pending / Blocked / Done / Cancelled)
* Drag-and-drop status changes via AJAX (`POST ChangeStatusAjax`)
* Concurrency check via row version ticks
* Blocked reason required when moving to Blocked status
* Filters respected (Assignee, Priority, Status, Date Range, Text)
* Truncated at 200 cards with notice

---

# 16. Activity Feed

## Purpose

Show project activity.

---

## Examples

```text
Andy created task:
Fix Retry Logic

Andy logged:
2.5h

Andy changed status:
InProgress → Done

Andy uploaded:
error-log.zip
```

---

# 17. Reports

## My Timesheet

Columns:

* Date
* Project
* Task
* Hours
* Status

---

## Team Timesheet

Columns:

* User
* Date
* Project
* Task
* Hours

---

## Project Summary

Columns:

* Project
* Tasks
* Hours

---

## User Summary

Columns:

* User
* Tasks
* Hours

---

# 18. Excel Export

## Supported Exports

### My Timesheet

### Team Timesheet

### Project Summary

### User Summary

---

## Requirements

* Respect filters
* Include totals
* Company format compatible

---

# 19. Database Tables

## Identity

AspNetUsers

AspNetRoles

---

## Business Tables

Projects

ProjectMembers

Tasks

TimeEntries

Attachments

Histories

---

# 20. Non Functional Requirements

## Performance

* <2s page load
* <1s search
* Support 50 concurrent users

---

## Security

* ASP.NET Identity
* RBAC
* CSRF protection
* HTML sanitization
* Secure file access

---

## Backup

Database:

* Daily backup

Uploads:

* Daily backup

---

# 21. Explicitly Out Of Scope

The MVP must NOT include:

* Sprint
* Epic
* Story Points
* Calendar View
* Timer Tracking
* Approval Workflow
* Notification System
* Email Reminder
* Mobile App
* Multi Company
* REST API
* Jira Integration
* Git Integration
* PDF Export
* Video Upload
* Task Template
* Recurring Task
* AI Features

---

# 22. MVP Deliverables

Authentication

User Management

Project Management

Project Membership

Task Management

Manual Time Logging

Estimated vs Actual

Rich Text Editor

Attachments

History Tracking

Search

Dashboard

Activity Feed

Reports

Excel Export

IIS Deployment
