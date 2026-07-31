[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [Guid]$SchoolUuid,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ClientId,

    [Parameter(Mandatory)]
    [ValidateNotNull()]
    [SecureString]$ClientSecret,

    [switch]$IncludeGuardians
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($SchoolUuid -eq [Guid]::Empty) {
    throw 'SchoolUuid mag niet leeg zijn.'
}

$plainClientSecret = [System.Net.NetworkCredential]::new('', $ClientSecret).Password
if ([string]::IsNullOrWhiteSpace($plainClientSecret)) {
    throw 'ClientSecret mag niet leeg zijn.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testEnvironment = @{
    SOMTODAY_INTEGRATION_TESTS = 'true'
    SOMTODAY_INTEGRATION_SCHOOL_UUID = $SchoolUuid.ToString()
    SOMTODAY_INTEGRATION_CLIENT_ID = $ClientId
    SOMTODAY_INTEGRATION_CLIENT_SECRET = $plainClientSecret
    SOMTODAY_INTEGRATION_INCLUDE_GUARDIANS = $IncludeGuardians.IsPresent.ToString()
}
$previousEnvironment = @{}
$assignedVariables = [System.Collections.Generic.List[string]]::new()

try {
    foreach ($name in $testEnvironment.Keys) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $testEnvironment[$name], 'Process')
        $assignedVariables.Add($name)
    }

    Push-Location $repositoryRoot
    try {
        & dotnet test Somtoday2MicrosoftSDS.Tests/Somtoday2MicrosoftSDS.Tests.csproj `
            --configuration Release `
            --logger 'console;verbosity=detailed' `
            --filter 'Category=SomtodayIntegration'

        if ($LASTEXITCODE -ne 0) {
            throw "De Somtoday OpenAPI-integratietests eindigden met exitcode $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    foreach ($name in $assignedVariables) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }

    $plainClientSecret = $null
    $testEnvironment['SOMTODAY_INTEGRATION_CLIENT_SECRET'] = $null
}
