[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$WindowsRoot
)

$ErrorActionPreference = 'Stop'

function Read-Utf8([string]$Path) {
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Write-Utf8([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

function Replace-Required([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    if (-not $Text.Contains($Old)) {
        throw "2.2.3 patch anchor not found: $Label"
    }
    return $Text.Replace($Old, $New)
}

$updaterPath = Join-Path $WindowsRoot 'QatFarm.Web\Data\DatabaseSchemaUpdater.cs'
$csprojPath = Join-Path $WindowsRoot 'QatFarm.Web\QatFarm.Web.csproj'
$qaPath = Join-Path $WindowsRoot 'Testing\Product-Readiness-QA.ps1'
$installerPath = Join-Path $WindowsRoot 'Installer\QatFarmSystem.iss'

# -----------------------------------------------------------------------------
# Database upgrade hardening: repair legacy JournalEntries before any index or
# EF query can reference columns that did not exist in older 2.x databases.
# -----------------------------------------------------------------------------
$updater = Read-Utf8 $updaterPath

if (-not $updater.Contains("COL_LENGTH(N'dbo.JournalEntries', N'IsAutomatic')")) {
    $statusConstraintAnchor = @'
        await ExecuteAsync(db, """
IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_JournalEntries_Status'
'@

    $legacyRepair = @'
        // 2.2.3: databases upgraded from early 2.x builds can have JournalEntries
        // without columns later used by indexes and EF. Repair every safe legacy
        // column before creating indexes; every ALTER is its own batch so SQL Server
        // compiles against the updated shape.
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'SourceHash') IS NULL
    ALTER TABLE dbo.JournalEntries ADD SourceHash nvarchar(64) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'IsAutomatic') IS NULL
    ALTER TABLE dbo.JournalEntries ADD IsAutomatic bit NOT NULL
        CONSTRAINT DF_JournalEntries_IsAutomatic DEFAULT(0) WITH VALUES;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'FarmId') IS NULL
    ALTER TABLE dbo.JournalEntries ADD FarmId bigint NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'ReversesEntryId') IS NULL
    ALTER TABLE dbo.JournalEntries ADD ReversesEntryId bigint NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'CreatedAt') IS NULL
    ALTER TABLE dbo.JournalEntries ADD CreatedAt datetime2(7) NOT NULL
        CONSTRAINT DF_JournalEntries_CreatedAt DEFAULT(SYSUTCDATETIME()) WITH VALUES;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'CreatedByUserId') IS NULL
    ALTER TABLE dbo.JournalEntries ADD CreatedByUserId nvarchar(450) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'UpdatedAt') IS NULL
    ALTER TABLE dbo.JournalEntries ADD UpdatedAt datetime2(7) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'UpdatedByUserId') IS NULL
    ALTER TABLE dbo.JournalEntries ADD UpdatedByUserId nvarchar(450) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'IsDeleted') IS NULL
    ALTER TABLE dbo.JournalEntries ADD IsDeleted bit NOT NULL
        CONSTRAINT DF_JournalEntries_IsDeleted DEFAULT(0) WITH VALUES;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'DeletedAt') IS NULL
    ALTER TABLE dbo.JournalEntries ADD DeletedAt datetime2(7) NULL;
""");
        await ExecuteAsync(db, """
IF COL_LENGTH(N'dbo.JournalEntries', N'RowVersion') IS NULL
    ALTER TABLE dbo.JournalEntries ADD RowVersion rowversion NOT NULL;
""");

'@

    if (-not $updater.Contains($statusConstraintAnchor)) {
        throw 'Unable to locate JournalEntries status-constraint anchor.'
    }
    $updater = $updater.Replace($statusConstraintAnchor, $legacyRepair + $statusConstraintAnchor)
}

# Repair duplicate SyncKey values left by interrupted/old synchronization before
# the unique index is created. Only duplicate identifiers are regenerated; rows
# and business data are never deleted.
$syncOld = @'
    SET @Sql = N'UPDATE dbo.' + QUOTENAME(@TableName) +
               N' SET SyncKey = LOWER(REPLACE(CONVERT(nvarchar(36), NEWID()), N''-'', N'''')) WHERE SyncKey IS NULL OR SyncKey = N'''';';
    EXEC sp_executesql @Sql;

    SET @IndexName = N'UX_' + @TableName + N'_SyncKey';
'@
$syncNew = @'
    SET @Sql = N'UPDATE dbo.' + QUOTENAME(@TableName) +
               N' SET SyncKey = LOWER(REPLACE(CONVERT(nvarchar(36), NEWID()), N''-'', N'''')) WHERE SyncKey IS NULL OR SyncKey = N'''';';
    EXEC sp_executesql @Sql;

    SET @Sql = N';WITH DuplicateKeys AS (' +
               N' SELECT Id, SyncKey, ROW_NUMBER() OVER (PARTITION BY SyncKey ORDER BY Id) AS rn' +
               N' FROM dbo.' + QUOTENAME(@TableName) +
               N' WHERE SyncKey IS NOT NULL AND SyncKey <> N'''' )' +
               N' UPDATE target SET SyncKey = LOWER(REPLACE(CONVERT(nvarchar(36), NEWID()), N''-'', N''''))' +
               N' FROM dbo.' + QUOTENAME(@TableName) + N' AS target' +
               N' INNER JOIN DuplicateKeys d ON d.Id = target.Id WHERE d.rn > 1;';
    EXEC sp_executesql @Sql;

    SET @IndexName = N'UX_' + @TableName + N'_SyncKey';
'@
if (-not $updater.Contains('DuplicateKeys AS')) {
    $updater = Replace-Required $updater $syncOld $syncNew 'SyncKey duplicate repair'
}

$schemaOld = @'
IF NOT EXISTS (SELECT 1 FROM dbo.QatFarmSchemaVersions WHERE Version = N'2.2.2')
    INSERT INTO dbo.QatFarmSchemaVersions(Version, Notes)
    VALUES(N'2.2.2', N'إصلاح ترقية قواعد البيانات القديمة وإضافة حالة القيود المحاسبية قبل إنشاء الفهارس.');
'@
$schemaNew = @'
IF NOT EXISTS (SELECT 1 FROM dbo.QatFarmSchemaVersions WHERE Version = N'2.2.2')
    INSERT INTO dbo.QatFarmSchemaVersions(Version, Notes)
    VALUES(N'2.2.2', N'إصلاح ترقية قواعد البيانات القديمة وإضافة حالة القيود المحاسبية قبل إنشاء الفهارس.');
IF NOT EXISTS (SELECT 1 FROM dbo.QatFarmSchemaVersions WHERE Version = N'2.2.3')
    INSERT INTO dbo.QatFarmSchemaVersions(Version, Notes)
    VALUES(N'2.2.3', N'إصدار الاستقرار النهائي: إصلاح كامل لجدول القيود القديمة ومعالجة مفاتيح المزامنة المكررة.');
'@
if (-not $updater.Contains("Version = N'2.2.3'")) {
    $updater = Replace-Required $updater $schemaOld $schemaNew 'schema version 2.2.3'
}

# Hard regression gates: the repair must exist before the source index.
$automaticRepair = $updater.IndexOf("COL_LENGTH(N'dbo.JournalEntries', N'IsAutomatic')")
$sourceHashRepair = $updater.IndexOf("COL_LENGTH(N'dbo.JournalEntries', N'SourceHash')")
$sourceIndex = $updater.IndexOf('CREATE INDEX IX_JournalEntries_Source')
if ($automaticRepair -lt 0 -or $sourceHashRepair -lt 0 -or $sourceIndex -le $automaticRepair -or $sourceIndex -le $sourceHashRepair) {
    throw "Legacy JournalEntries regression gate failed. IsAutomatic=$automaticRepair SourceHash=$sourceHashRepair Index=$sourceIndex"
}
if (-not $updater.Contains('DuplicateKeys AS')) {
    throw 'SyncKey duplicate regression gate failed.'
}
Write-Utf8 $updaterPath $updater

# -----------------------------------------------------------------------------
# Product version and QA expectations.
# -----------------------------------------------------------------------------
$csproj = Read-Utf8 $csprojPath
$csproj = Replace-Required $csproj '<Version>2.2.2</Version>' '<Version>2.2.3</Version>' 'csproj Version'
$csproj = Replace-Required $csproj '<AssemblyVersion>2.2.2.0</AssemblyVersion>' '<AssemblyVersion>2.2.3.0</AssemblyVersion>' 'csproj AssemblyVersion'
$csproj = Replace-Required $csproj '<FileVersion>2.2.2.0</FileVersion>' '<FileVersion>2.2.3.0</FileVersion>' 'csproj FileVersion'
Write-Utf8 $csprojPath $csproj

$qa = Read-Utf8 $qaPath
$qa = Replace-Required $qa "<Version>2.2.2</Version>" "<Version>2.2.3</Version>" 'QA product version'
Write-Utf8 $qaPath $qa

# -----------------------------------------------------------------------------
# Installer hardening.
# - 2.2.3 identity/output name
# - preserve old local config as a backup, then regenerate known-good settings
# - allow LAN sync on any Windows network profile but only from the local subnet
# - remove Inno Setup RunOnceId warning
# -----------------------------------------------------------------------------
$iss = Read-Utf8 $installerPath
$iss = Replace-Required $iss '#define MyAppVersion "2.2.2"' '#define MyAppVersion "2.2.3"' 'installer version'
$iss = Replace-Required $iss 'OutputBaseFilename=QatFarmSystem_Setup_2.2.2_x64' 'OutputBaseFilename=QatFarmSystem_Setup_2.2.3_x64' 'installer output name'
$iss = Replace-Required $iss 'profile=private"; Flags: runhidden waituntilterminated' 'profile=any remoteip=localsubnet"; Flags: runhidden waituntilterminated' 'LAN firewall rule'
$iss = Replace-Required $iss 'Filename: "{cmd}"; Parameters: "/C netsh advfirewall firewall delete rule name=""AWAD SOFT QatFarm WiFi Sync"""; Flags: runhidden waituntilterminated' 'Filename: "{cmd}"; Parameters: "/C netsh advfirewall firewall delete rule name=""AWAD SOFT QatFarm WiFi Sync"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveQatFarmSyncFirewall"' 'uninstall firewall RunOnceId'
$iss = Replace-Required $iss 'ConfigDir, ConfigPath, JsonText, ServerName: String;' 'ConfigDir, ConfigPath, ConfigBackupPath, JsonText, ServerName: String;' 'installer config variables'
$configOld = @'
  ForceDirectories(ConfigDir);
  if FileExists(ConfigPath) then
    Exit;
  ServerName := EscapeJson(GetSqlInstance(''));
'@
$configNew = @'
  ForceDirectories(ConfigDir);
  if FileExists(ConfigPath) then
  begin
    ConfigBackupPath := ConfigPath + '.pre-2.2.3.bak';
    if not FileCopy(ConfigPath, ConfigBackupPath, False) then
    begin
      MsgBox('تعذر إنشاء نسخة احتياطية من إعدادات الإصدار السابق: ' + ConfigBackupPath, mbError, MB_OK);
      Exit;
    end;
  end;
  ServerName := EscapeJson(GetSqlInstance(''));
'@
$iss = Replace-Required $iss $configOld $configNew 'installer stale configuration repair'
Write-Utf8 $installerPath $iss

Write-Host 'AWAD SOFT Windows 2.2.3 final stability patch applied successfully.' -ForegroundColor Green
