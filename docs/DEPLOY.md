# TTMS Deployment Guide

## Prerequisites

- Windows Server 2019+ with IIS
- .NET 8 Hosting Bundle (ASP.NET Core Runtime 8.x + IIS Module)
  - Download: https://dotnet.microsoft.com/en-us/download/dotnet/8.0
- SQL Server 2019+ (or LocalDB for dev)

## 1. Publish the Application

```bash
dotnet publish src/TTMS.Web --configuration Release --output ./publish
```

## 2. Configure IIS

### Create Application Pool
1. Open IIS Manager
2. Application Pools → Add Application Pool
   - Name: `TTMS`
   - .NET CLR Version: No Managed Code
   - Managed pipeline mode: Integrated

### Create Site
1. Sites → Add Website
   - Site name: `TTMS`
   - Physical path: `C:\inetpub\wwwroot\ttms` (copy `publish/` contents here)
   - Application pool: `TTMS`
   - Host name: `ttms.yourcompany.com` (or use IP/port)
   - Port: 80 or 443 (recommend HTTPS with a valid certificate)

### Set Permissions
- `IIS AppPool\TTMS` needs **Modify** access to:
  - `App_Data/` (for DataProtection keys)
  - `uploads/` (for file attachments)

## 3. Configure Connection String

In `appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=TTMS;Integrated Security=True;TrustServerCertificate=True;"
  }
}
```

Or set via environment variable:
```
ConnectionStrings__DefaultConnection=Server=...;Database=TTMS;...
```

## 4. Apply Database Migrations

Migrations are applied automatically on first run (see `Program.cs` line 123-126).
Alternatively, apply them manually:

```bash
dotnet ef database update --project src/TTMS.Web
```

## 5. Database Backup

### Daily Full Backup (SQL Server Agent Job)

```sql
BACKUP DATABASE [TTMS] TO DISK = N'E:\Backups\TTMS\TTMS_'
    + FORMAT(GETDATE(), 'yyyyMMdd') + N'.bak'
WITH COMPRESSION, INIT;
```

### Uploads Folder Backup

Add to your existing file backup schedule:
```
C:\inetpub\wwwroot\ttms\uploads\
```

### DataProtection Keys Backup

```
C:\inetpub\wwwroot\ttms\App_Data\DataProtectionKeys\
```

**Important:** If DataProtection keys are lost, all existing login sessions (cookies) become invalid.

## 6. Restore Procedure

1. Restore database from `.bak` file:
   ```sql
   RESTORE DATABASE [TTMS] FROM DISK = N'E:\Backups\TTMS\TTMS_20260630.bak'
   WITH REPLACE;
   ```
2. Restore `uploads/` folder
3. Restore `App_Data/DataProtectionKeys/` folder
4. Restart IIS application pool: `appcmd recycle apppool TTMS`

## 7. Verify

1. Browse to `https://ttms.yourcompany.com`
2. Log in with admin credentials
3. Verify all pages load
4. Run a test backup restore to staging monthly

## Environment Variables (Optional)

| Variable | Notes |
|----------|-------|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__DefaultConnection` | Override connection string |