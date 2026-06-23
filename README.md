# TTMS — Task & Time Management System

Lightweight internal web application for project, task, and timesheet tracking. Replaces Excel-based workflows.

## Tech Stack

- **ASP.NET Core 8** MVC
- **Entity Framework Core 8** (SQL Server)
- **ASP.NET Core Identity** (users & roles)
- **Bootstrap 5** + minimal JavaScript
- **TinyMCE** for rich text editing
- **ClosedXML** for Excel export
- **HtmlSanitizer** for XSS protection
- **IIS** on Windows Server (production)

## Project Structure

```
src/TTMS.Web/         # Main web project
  Controllers/        # MVC controllers
  Models/             # Entities, view models, enums
  Views/              # Razor views (.cshtml)
  Services/           # Business logic (History, Sanitization, Export, Files)
  Data/               # DbContext + migrations
  Areas/Identity/     # Identity UI
  wwwroot/            # Static assets (Bootstrap, jQuery, TinyMCE)
  uploads/            # Local file storage (gitignored)

docs/spec.md          # Authoritative MVP specification
.continue/rules/      # AI coding-assistant context
```

## Getting Started

### Prerequisites

- .NET 8 SDK
- SQL Server 2019+ (LocalDB works for dev)

### Setup

```bash
# Restore packages
dotnet restore

# Apply EF Core migrations
dotnet ef database update --project src/TTMS.Web

# Run the app
dotnet run --project src/TTMS.Web
```

Default admin user (from `appsettings.json` → `SeedSettings`):
- Email: `admin@ttms.local`
- Password: `ChangeMe!123`

**Change this password on first login.**

## MVP Scope

- Projects, Tasks, Time Entries
- Rich text descriptions + file attachments
- Audit history for every mutation
- 4 Excel reports: My Timesheet, Team Timesheet, Project Summary, User Summary
- Project membership: Owner / Member / Viewer

See `docs/spec.md` for the full specification.

## Out of Scope (explicitly NOT built)

Sprints, Kanban, timers, notifications, mobile app, REST API, AI features, PDF export, video upload, recurring tasks.
