[CmdletBinding()]
param(
    [string]$Image = 'somtoday2microsoftsds:wslc-test',
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version = '0.0.0.0',
    [string]$Revision,
    [ValidateSet('x86_64', 'aarch64')]
    [string]$ExpectedArchitecture,
    [switch]$Pull,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-WslcPath {
    $command = Get-Command wslc.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $installedPath = Join-Path $env:ProgramFiles 'WSL\wslc.exe'
    if (Test-Path -LiteralPath $installedPath -PathType Leaf) {
        return $installedPath
    }

    throw 'wslc.exe is niet gevonden. Werk WSL bij met "wsl --update", open daarna een nieuwe PowerShell-sessie en controleer met "wslc version".'
}

function Invoke-Wslc {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [int]$ExpectedExitCode = 0,
        [switch]$CaptureOutput
    )

    if ($CaptureOutput) {
        $output = & $script:WslcPath @Arguments 2>&1
    }
    else {
        & $script:WslcPath @Arguments
        $output = @()
    }

    $exitCode = $LASTEXITCODE
    if ($exitCode -ne $ExpectedExitCode) {
        $operation = $Arguments[0]
        $renderedOutput = ($output | Out-String).Trim()
        throw "wslc $operation eindigde met exitcode $exitCode; verwacht was $ExpectedExitCode.`n$renderedOutput"
    }

    return $output
}

function Assert-Contains {
    param(
        [Parameter(Mandatory)]
        [string]$Text,
        [Parameter(Mandatory)]
        [string]$Expected,
        [Parameter(Mandatory)]
        [string]$Description
    )

    if ($Text.IndexOf($Expected, [StringComparison]::Ordinal) -lt 0) {
        throw "$Description ontbreekt in de image-inspectie: $Expected"
    }
}

function Get-WslcContainerState {
    param(
        [Parameter(Mandatory)]
        [string]$ContainerName
    )

    $inspectJson = (Invoke-Wslc -Arguments @('container', 'inspect', $ContainerName) -CaptureOutput | Out-String)
    try {
        $inspectResult = $inspectJson | ConvertFrom-Json
    }
    catch {
        throw "WSLC gaf geen geldige JSON-inspectie terug voor container '$ContainerName'."
    }

    $container = @($inspectResult)[0]
    if ($null -eq $container -or $null -eq $container.State) {
        throw "WSLC gaf geen containerstatus terug voor '$ContainerName'."
    }

    return $container.State
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$script:WslcPath = Resolve-WslcPath

if ($SkipBuild -and $Pull) {
    throw '-Pull kan niet samen met -SkipBuild worden gebruikt.'
}

if ([string]::IsNullOrWhiteSpace($Revision)) {
    $Revision = (& git -C $repositoryRoot rev-parse --verify HEAD 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($Revision)) {
        $Revision = 'local'
    }
}

$nativePreferenceVariableExists = Test-Path Variable:PSNativeCommandUseErrorActionPreference
if ($nativePreferenceVariableExists) {
    $previousNativePreference = $PSNativeCommandUseErrorActionPreference
    $PSNativeCommandUseErrorActionPreference = $false
}

Push-Location $repositoryRoot
try {
    Write-Host "WSLC-runtime controleren: $script:WslcPath"
    Invoke-Wslc -Arguments @('version')

    $buildArguments = @(
        'build',
        '--tag', $Image,
        '--build-arg', "VERSION=$Version",
        '--build-arg', "REVISION=$Revision"
    )
    if ($Pull) {
        $buildArguments += '--pull'
    }
    $buildArguments += '.'

    if ($SkipBuild) {
        Write-Host "Bestaand containerimage testen: $Image"
    }
    else {
        Write-Host "Containerimage bouwen: $Image"
        Invoke-Wslc -Arguments $buildArguments
    }

    Write-Host 'OCI-labels controleren'
    $inspectText = (Invoke-Wslc -Arguments @('image', 'inspect', $Image) -CaptureOutput | Out-String)
    Assert-Contains -Text $inspectText -Expected 'org.opencontainers.image.licenses' -Description 'OCI-licentielabel'
    Assert-Contains -Text $inspectText -Expected 'AGPL-3.0-or-later' -Description 'OCI-licentiewaarde'
    Assert-Contains -Text $inspectText -Expected 'org.opencontainers.image.version' -Description 'OCI-versielabel'
    Assert-Contains -Text $inspectText -Expected $Version -Description 'OCI-versiewaarde'
    Assert-Contains -Text $inspectText -Expected 'org.opencontainers.image.revision' -Description 'OCI-revisielabel'
    Assert-Contains -Text $inspectText -Expected $Revision -Description 'OCI-revisiewaarde'
    Assert-Contains -Text $inspectText -Expected 'https://github.com/Essella/Somtoday2MicrosoftSDS' -Description 'OCI-bronlabel'

    Write-Host 'Linux-platform, native architectuur en non-root gebruiker controleren'
    $kernel = (Invoke-Wslc -Arguments @('run', '--rm', '--entrypoint', '/bin/uname', $Image, '-s') -CaptureOutput | Out-String).Trim()
    if ($kernel -ne 'Linux') {
        throw "Het image draait niet op Linux; uname rapporteerde '$kernel'."
    }

    $architecture = (Invoke-Wslc -Arguments @('run', '--rm', '--entrypoint', '/bin/uname', $Image, '-m') -CaptureOutput | Out-String).Trim()
    if ($architecture -notin @('x86_64', 'aarch64')) {
        throw "WSLC rapporteerde een niet-ondersteunde architectuur: '$architecture'."
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedArchitecture) -and $architecture -ne $ExpectedArchitecture) {
        throw "Het image heeft architectuur '$architecture'; verwacht was '$ExpectedArchitecture'."
    }
    Write-Host "Native WSLC-imagearchitectuur: $architecture"

    $userId = (Invoke-Wslc -Arguments @('run', '--rm', '--entrypoint', '/usr/bin/id', $Image, '-u') -CaptureOutput | Out-String).Trim()
    if ($userId -ne '1654') {
        throw "Het image draait niet als de verwachte non-root gebruiker 1654; id rapporteerde '$userId'."
    }

    Write-Host 'Fast-fail zonder Production-configuratie controleren'
    $secretMarker = "wslc-secret-marker-$([Guid]::NewGuid().ToString('N'))"
    $failureOutput = Invoke-Wslc -Arguments @(
        'run',
        '--rm',
        '--env', "Somtoday__ClientSecret=$secretMarker",
        $Image
    ) -ExpectedExitCode 1 -CaptureOutput
    $failureText = ($failureOutput | Out-String)
    if ($failureText.IndexOf($secretMarker, [StringComparison]::Ordinal) -ge 0) {
        throw 'De container logde de tijdelijke secretmarker.'
    }

    Write-Host 'SIGTERM-cancellation tijdens managed-identity tokenverwerving controleren'
    $containerName = "somtoday2microsoftsds-sigterm-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"
    $cancellationSecretMarker = "wslc-cancellation-secret-$([Guid]::NewGuid().ToString('N'))"
    try {
        Invoke-Wslc -Arguments @(
            'run',
            '--detach',
            '--name', $containerName,
            '--env', 'DOTNET_ENVIRONMENT=Development',
            '--env', 'Somtoday__ClientId=wslc-cancellation-client',
            '--env', "Somtoday__ClientSecret=$cancellationSecretMarker",
            '--env', 'Somtoday__SchoolUUID__0=11111111-1111-1111-1111-111111111111',
            '--env', 'SchoolDataSync__SourceName=Container test source',
            '--env', 'AZURE_TOKEN_CREDENTIALS=ManagedIdentityCredential',
            '--env', 'IDENTITY_ENDPOINT=http://192.0.2.1:10000/metadata/identity/oauth2/token',
            '--env', 'IDENTITY_HEADER=validation-only-header',
            $Image
        ) -CaptureOutput | Out-Null

        Start-Sleep -Seconds 2
        $runningState = Get-WslcContainerState -ContainerName $containerName
        if ($runningState.Running -ne $true) {
            throw "De SIGTERM-testcontainer stopte voordat het signaal kon worden verstuurd (exitcode $($runningState.ExitCode))."
        }

        Invoke-Wslc -Arguments @('container', 'stop', '--signal', 'SIGTERM', '--time', '10', $containerName) -CaptureOutput | Out-Null

        $stoppedState = Get-WslcContainerState -ContainerName $containerName
        if ($stoppedState.Running -ne $false) {
            throw 'De SIGTERM-testcontainer draait nog na de stopopdracht.'
        }
        if ([int]$stoppedState.ExitCode -ne 1) {
            throw "De SIGTERM-testcontainer eindigde met exitcode $($stoppedState.ExitCode); verwacht was 1."
        }

        $cancellationLogs = (Invoke-Wslc -Arguments @('container', 'logs', $containerName) -CaptureOutput | Out-String)
        if ($cancellationLogs.IndexOf('Application cancellation requested', [StringComparison]::Ordinal) -lt 0) {
            throw 'De SIGTERM-testcontainer logde de verwachte cancellationmelding niet.'
        }
        if ($cancellationLogs.IndexOf($cancellationSecretMarker, [StringComparison]::Ordinal) -ge 0) {
            throw 'De SIGTERM-testcontainer logde de tijdelijke secretmarker.'
        }
    }
    finally {
        & $script:WslcPath container remove --force $containerName 2>&1 | Out-Null
    }

    Write-Host "WSLC-containerintegratietests geslaagd voor $Image."
}
finally {
    Pop-Location
    if ($nativePreferenceVariableExists) {
        $PSNativeCommandUseErrorActionPreference = $previousNativePreference
    }
}
