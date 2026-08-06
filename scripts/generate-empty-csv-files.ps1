[CmdletBinding()]
param(
    [ValidateSet('1', '2', 'all')]
    [string]$Version,

    [ValidateSet('J', 'N', 'with', 'without', 'both')]
    [string]$Guardians,

    [string]$OutputPath = (Get-Location).Path
)

function Get-CsvDefinition {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('1', '2')]
        [string]$Version,

        [Parameter(Mandatory = $true)]
        [bool]$WithGuardians
    )

    if ($Version -eq '1') {
        $files = [ordered]@{
            'School.csv'            = 'SIS ID,Name'
            'Section.csv'           = 'SIS ID,School SIS ID,Section Name,Section Number,Course Name,Course Description'
            'Student.csv'           = 'SIS ID,School SIS ID,Username'
            'StudentEnrollment.csv' = 'Section SIS ID,SIS ID'
            'Teacher.csv'           = 'SIS ID,School SIS ID,Username'
            'TeacherRoster.csv'     = 'Section SIS ID,SIS ID'
        }

        if ($WithGuardians) {
            $files['Guardianrelationship.csv'] = 'SIS ID,Email,Role'
            $files['User.csv'] = 'Email,First Name,Last Name,Phone,SIS ID'
        }

        return $files
    }

    $files = [ordered]@{
        'classes.csv'     = 'sourcedId,orgSourcedId,title,sessionSourcedIds,courseSourcedId'
        'enrollments.csv' = 'classSourcedId,userSourcedId,role'
        'orgs.csv'        = 'sourcedId,name,type,parentSourcedId'
        'roles.csv'       = 'userSourcedId,orgSourcedId,role'
        'users.csv'       = 'sourcedId,username,givenName,familyName,password,activeDirectoryMatchId,email,phone,sms'
    }

    if ($WithGuardians) {
        $files['relationships.csv'] = 'userSourcedId,relationshipUserSourcedId,relationshipRole'
    }

    return $files
}

function Get-SetName {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('1', '2')]
        [string]$Version,

        [Parameter(Mandatory = $true)]
        [bool]$WithGuardians
    )

    $guardianSuffix = if ($WithGuardians) { 'with-guardians' } else { 'no-guardians' }
    return "v$Version-$guardianSuffix"
}

function Resolve-GuardiansChoice {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $normalized = $Value.Trim().ToLowerInvariant()
    switch ($normalized) {
        'j' { return 'with' }
        'with' { return 'with' }
        'n' { return 'without' }
        'without' { return 'without' }
        'both' { return 'both' }
        default { throw "Ongeldige waarde voor guardians: '$Value'. Gebruik J/N, with/without, of both." }
    }
}

if (-not $Version) {
    do {
        $Version = (Read-Host 'Versie kiezen (1 of 2)').Trim().ToLowerInvariant()
    } while ($Version -notin @('1', '2'))
}

$guardiansChoice = $null
if ($Guardians) {
    $guardiansChoice = Resolve-GuardiansChoice -Value $Guardians
}

if (-not $guardiansChoice) {
    if ($Version -eq 'all') {
        $guardiansChoice = 'both'
    }
    else {
        do {
            $input = (Read-Host 'Inclusief ouders/verzorgers? J/N').Trim().ToUpperInvariant()
        } while ($input -notin @('J', 'N'))

        $guardiansChoice = Resolve-GuardiansChoice -Value $input
    }
}

$combinations = @()

$versions = if ($Version -eq 'all') { @('1', '2') } else { @($Version) }
foreach ($versionItem in $versions) {
    switch ($guardiansChoice) {
        'with' {
            $combinations += [pscustomobject]@{ Version = $versionItem; WithGuardians = $true }
        }
        'without' {
            $combinations += [pscustomobject]@{ Version = $versionItem; WithGuardians = $false }
        }
        'both' {
            $combinations += [pscustomobject]@{ Version = $versionItem; WithGuardians = $false }
            $combinations += [pscustomobject]@{ Version = $versionItem; WithGuardians = $true }
        }
    }
}

$baseOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Path $baseOutputPath -Force | Out-Null

$singleSetToBaseFolder = $combinations.Count -eq 1

foreach ($combination in $combinations) {
    $files = Get-CsvDefinition -Version $combination.Version -WithGuardians $combination.WithGuardians
    $setName = Get-SetName -Version $combination.Version -WithGuardians $combination.WithGuardians
    $targetDirectory = if ($singleSetToBaseFolder) { $baseOutputPath } else { Join-Path $baseOutputPath $setName }

    New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null

    foreach ($file in $files.GetEnumerator()) {
        $path = Join-Path $targetDirectory $file.Key
        Set-Content -Path $path -Value $file.Value -Encoding utf8
        Write-Host "Aangemaakt: $path"
    }
}
