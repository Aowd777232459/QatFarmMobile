param([Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference='Stop'

$program=Join-Path $Root 'QatFarm.Web\Program.cs'
$text=Get-Content $program -Raw
$text=$text.Replace('options.Password.RequiredLength = 10;','options.Password.RequiredLength = 6;')
$text=$text.Replace('options.Password.RequireDigit = true;','options.Password.RequireDigit = false;')
$text=$text.Replace('options.Password.RequireLowercase = true;','options.Password.RequireLowercase = false;')
$text=$text.Replace('options.Password.RequireUppercase = true;','options.Password.RequireUppercase = false;')
$text=$text.Replace('options.Password.RequireNonAlphanumeric = true;','options.Password.RequireNonAlphanumeric = false;')
Set-Content $program $text -Encoding utf8

$dbinit=Join-Path $Root 'QatFarm.Web\Data\DbInitializer.cs'
$db=Get-Content $dbinit -Raw
$pattern='(?s)\s*var userManager = scope\.ServiceProvider\.GetRequiredService<UserManager<ApplicationUser>>\(\);.*?if \(!await userManager\.IsInRoleAsync\(admin, "Administrator"\)\)\s*\{.*?\}\s*\r?\n\s*var cultivationNames'
$replacement="`r`n`r`n        // Primary administrator is created interactively on first login.`r`n        var cultivationNames"
$patched=[regex]::Replace($db,$pattern,$replacement)
if($patched -eq $db){throw 'Could not remove the seeded administrator block.'}
Set-Content $dbinit $patched -Encoding utf8

$sourceFiles=Join-Path $PSScriptRoot 'QatFarm.Web'
Get-ChildItem $sourceFiles -Recurse -File | ForEach-Object {
    $relative=$_.FullName.Substring($sourceFiles.Length).TrimStart('\')
    $target=Join-Path (Join-Path $Root 'QatFarm.Web') $relative
    New-Item (Split-Path $target -Parent) -ItemType Directory -Force | Out-Null
    Copy-Item $_.FullName $target -Force
}

$iss=Join-Path $Root 'Installer\QatFarmSystem.iss'
$issText=Get-Content $iss -Raw
$issText=$issText.Replace('#define MyAppVersion "2.2.2"','#define MyAppVersion "2.3.0"')
$issText=$issText.Replace('OutputBaseFilename=QatFarmSystem_Setup_2.2.2_x64','OutputBaseFilename=AWAD-SOFT-QatFarm-Windows-2.3.0-Setup')
Set-Content $iss $issText -Encoding utf8

Write-Host 'WINDOWS_REFERENCE_PATCH_OK'
