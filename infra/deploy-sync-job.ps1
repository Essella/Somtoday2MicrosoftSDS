[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9._/-]+$')]
    [string]$RepositoryRef = 'main'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryOwner = 'Essella'
$repositoryName = 'Somtoday2MicrosoftSDS'
$environmentTagName = 'Somtoday2MicrosoftSDS.environment'
$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$temporaryDirectory = Join-Path $temporaryRoot "somtoday2microsoftsds-job-$([guid]::NewGuid().ToString('N'))"

function Invoke-Az {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $output = & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }

    return $output
}

function Read-RequiredValue {
    param(
        [Parameter(Mandatory)]
        [string]$Prompt
    )

    do {
        $value = Read-Host $Prompt
    }
    while ([string]::IsNullOrWhiteSpace($value))

    return $value.Trim()
}

function Read-ValueOrDefault {
    param(
        [Parameter(Mandatory)]
        [string]$Prompt,
        [Parameter(Mandatory)]
        [string]$DefaultValue
    )

    $value = Read-Host "$Prompt [$DefaultValue]"
    return [string]::IsNullOrWhiteSpace($value) ? $DefaultValue : $value.Trim()
}

function ConvertTo-BicepString {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    return "'$($Value.Replace("'", "''"))'"
}

function Select-EnvironmentResourceGroup {
    $groups = @(Invoke-Az -Arguments @('group', 'list', '--output', 'json') | Out-String | ConvertFrom-Json)
    $candidates = @(
        foreach ($group in $groups) {
            if ($null -eq $group.tags) {
                continue
            }

            $tag = $group.tags.PSObject.Properties[$environmentTagName]
            if ($null -ne $tag -and -not [string]::IsNullOrWhiteSpace([string]$tag.Value)) {
                [pscustomobject]@{
                    ResourceGroupName = $group.name
                    EnvironmentName = ([string]$tag.Value).Trim()
                }
            }
        }
    )

    if ($candidates.Count -eq 0) {
        throw "No visible resource group has the '$environmentTagName' tag. Deploy the Environment first."
    }

    Write-Host 'Select the resource group that contains the Container Apps Environment:'
    for ($index = 0; $index -lt $candidates.Count; $index++) {
        $candidate = $candidates[$index]
        Write-Host "[$($index + 1)] $($candidate.ResourceGroupName) ($($candidate.EnvironmentName))"
    }

    do {
        $selection = Read-Host "Enter a number from 1 through $($candidates.Count)"
        $selectedIndex = 0
        $validSelection = [int]::TryParse($selection, [ref]$selectedIndex) -and $selectedIndex -ge 1 -and $selectedIndex -le $candidates.Count
    }
    while (-not $validSelection)

    return $candidates[$selectedIndex - 1]
}

$previousClientSecret = $env:SOMTODAY_CLIENT_SECRET
$secretBstr = [IntPtr]::Zero

try {
    $account = Invoke-Az -Arguments @('account', 'show', '--output', 'json') | Out-String | ConvertFrom-Json
    Write-Host "Using subscription '$($account.name)'."

    $selectedEnvironment = Select-EnvironmentResourceGroup
    Invoke-Az -Arguments @('containerapp', 'env', 'show', '--resource-group', $selectedEnvironment.ResourceGroupName, '--name', $selectedEnvironment.EnvironmentName, '--output', 'none') | Out-Null
    Write-Host "Using Container Apps Environment '$($selectedEnvironment.EnvironmentName)' in resource group '$($selectedEnvironment.ResourceGroupName)'."

    $jobPrefix = Read-RequiredValue -Prompt 'Job prefix'
    $schoolUuidsCsv = Read-RequiredValue -Prompt 'Somtoday institution UUIDs, comma-separated'
    $inboundFlowId = Read-RequiredValue -Prompt 'SDS inbound-flow UUID'
    $somtodayClientId = Read-RequiredValue -Prompt 'Somtoday client ID'
    $somtodayEnvironment = Read-ValueOrDefault -Prompt 'Somtoday environment' -DefaultValue 'PROD'
    $includedLocationCodesCsv = Read-Host 'Included location codes, comma-separated (optional)'
    $excludedLocationCodesCsv = Read-Host 'Excluded location codes, comma-separated (optional)'
    $enableGuardianSync = (Read-ValueOrDefault -Prompt 'Enable guardian sync (true/false)' -DefaultValue 'false').ToLowerInvariant()
    if ($enableGuardianSync -notin @('true', 'false')) {
        throw 'Enable guardian sync must be true or false.'
    }

    $teacherUsernameFormat = Read-ValueOrDefault -Prompt 'Teacher username format' -DefaultValue 'Emailadres'
    $studentUsernameFormat = Read-ValueOrDefault -Prompt 'Student username format' -DefaultValue 'Emailadres'
    $clientSecret = Read-Host 'Somtoday client secret' -AsSecureString

    New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
    $baseUri = "https://raw.githubusercontent.com/$repositoryOwner/$repositoryName/$RepositoryRef/infra"
    foreach ($fileName in @('deploy-sync-job.bicep', 'sync-job.bicep', 'job.bicep', 'bicepconfig.json')) {
        Invoke-WebRequest -Uri "$baseUri/$fileName" -OutFile (Join-Path $temporaryDirectory $fileName)
    }

    $parametersPath = Join-Path $temporaryDirectory 'deploy-sync-job.bicepparam'
    @"
using './deploy-sync-job.bicep'

param jobPrefix = $(ConvertTo-BicepString -Value $jobPrefix)
param schoolUuidsCsv = $(ConvertTo-BicepString -Value $schoolUuidsCsv)
param inboundFlowId = $(ConvertTo-BicepString -Value $inboundFlowId)
param somtodayClientId = $(ConvertTo-BicepString -Value $somtodayClientId)
param somtodayEnvironment = $(ConvertTo-BicepString -Value $somtodayEnvironment)
param includedLocationCodesCsv = $(ConvertTo-BicepString -Value $includedLocationCodesCsv)
param excludedLocationCodesCsv = $(ConvertTo-BicepString -Value $excludedLocationCodesCsv)
param enableGuardianSync = $enableGuardianSync
param teacherUsernameFormat = $(ConvertTo-BicepString -Value $teacherUsernameFormat)
param studentUsernameFormat = $(ConvertTo-BicepString -Value $studentUsernameFormat)
param somtodayClientSecret = readEnvironmentVariable('SOMTODAY_CLIENT_SECRET')
"@ | Set-Content -LiteralPath $parametersPath -Encoding utf8NoBOM

    $secretBstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($clientSecret)
    $env:SOMTODAY_CLIENT_SECRET = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretBstr)
    Invoke-Az -Arguments @('deployment', 'group', 'create', '--resource-group', $selectedEnvironment.ResourceGroupName, '--parameters', $parametersPath, '--only-show-errors') | Out-Host
}
finally {
    if ($secretBstr -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretBstr)
    }

    if ($null -eq $previousClientSecret) {
        Remove-Item Env:SOMTODAY_CLIENT_SECRET -ErrorAction SilentlyContinue
    }
    else {
        $env:SOMTODAY_CLIENT_SECRET = $previousClientSecret
    }

    if (Test-Path -LiteralPath $temporaryDirectory) {
        $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        $resolvedTemporaryDirectory = [System.IO.Path]::GetFullPath($temporaryDirectory)
        $requiredPrefix = $resolvedTemporaryRoot + [System.IO.Path]::DirectorySeparatorChar
        if (-not $resolvedTemporaryDirectory.StartsWith($requiredPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected temporary path '$temporaryDirectory'."
        }

        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
