[CmdletBinding()]
param(
    [string]$BicepExecutable = 'az',
    [switch]$StandaloneBicep
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$infraRoot = Join-Path $repositoryRoot 'infra'
$temporaryRoot = if ($env:RUNNER_TEMP) {
    [System.IO.Path]::GetFullPath($env:RUNNER_TEMP)
}
else {
    [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
}
$temporaryDirectory = Join-Path $temporaryRoot "somtoday2microsoftsds-infra-$([guid]::NewGuid().ToString('N'))"

function Invoke-Bicep {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    if ($StandaloneBicep) {
        & $BicepExecutable @Arguments
    }
    else {
        & $BicepExecutable bicep @Arguments
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Bicep command failed with exit code $LASTEXITCODE."
    }
}

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,
        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function ConvertTo-CanonicalValue {
    param(
        [AllowNull()]
        [object]$Value
    )

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [pscustomobject]) {
        $result = [ordered]@{}
        foreach ($property in ($Value.PSObject.Properties | Sort-Object Name)) {
            $result[$property.Name] = ConvertTo-CanonicalValue -Value $property.Value
        }

        return $result
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in ($Value.Keys | Sort-Object)) {
            $result[$key] = ConvertTo-CanonicalValue -Value $Value[$key]
        }

        return $result
    }

    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $items = @()
        foreach ($item in $Value) {
            $items += , (ConvertTo-CanonicalValue -Value $item)
        }

        return , $items
    }

    return $Value
}

function Get-CanonicalJson {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $document = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 100
    if ($null -ne $document.metadata) {
        $document.metadata.PSObject.Properties.Remove('_generator')
    }

    $canonicalDocument = ConvertTo-CanonicalValue -Value $document
    return ConvertTo-Json -InputObject $canonicalDocument -Depth 100 -Compress
}

function Assert-EnvironmentTemplate {
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Template,
        [Parameter(Mandatory)]
        [string]$TemplateName
    )

    $parameterNames = @($Template.parameters.PSObject.Properties.Name)
    Assert-Condition -Condition (@(Compare-Object -ReferenceObject @('environmentName', 'logAnalyticsName') -DifferenceObject $parameterNames).Count -eq 0) -Message "$TemplateName must expose only environmentName and logAnalyticsName."
    Assert-Condition -Condition (@($Template.resources | Where-Object type -EQ 'Microsoft.App/managedEnvironments').Count -eq 1) -Message "$TemplateName must create one Container Apps Environment."
    Assert-Condition -Condition (@($Template.resources | Where-Object type -EQ 'Microsoft.OperationalInsights/workspaces').Count -eq 1) -Message "$TemplateName must create one Log Analytics Workspace."
    Assert-Condition -Condition (@($Template.resources | Where-Object type -EQ 'Microsoft.Resources/tags').Count -eq 1) -Message "$TemplateName must store the Environment tag."
    Assert-Condition -Condition (@($Template.resources | Where-Object type -EQ 'Microsoft.Resources/deployments').Count -eq 0) -Message "$TemplateName must not deploy a sync Job."
    Assert-Condition -Condition (@($Template.resources | Where-Object type -Like 'Microsoft.Graph/*').Count -eq 0) -Message "$TemplateName must not deploy Microsoft Graph resources."
    Assert-Condition -Condition (@($Template.resources | Where-Object type -Like 'Microsoft.Storage/*').Count -eq 0) -Message "$TemplateName must not create Azure Storage resources."
}

function Assert-JobTemplate {
    param(
        [Parameter(Mandatory)]
        [pscustomobject]$Template
    )

    $parameterNames = @($Template.parameters.PSObject.Properties.Name)
    $forbiddenParameterNames = @('environmentName', 'logAnalyticsName', 'environmentMode', 'existingContainerAppsEnvironmentResourceId', 'containerAppsEnvironmentName', 'imageReference', 'cronExpression', 'replicaTimeoutSeconds', 'replicaRetryLimit')
    Assert-Condition -Condition ($Template.parameters.somtodayClientSecret.type -ieq 'secureString') -Message 'infra/deploy-sync-job.bicep must compile somtodayClientSecret as a secureString.'
    Assert-Condition -Condition (@(@('jobPrefix', 'schoolUuidsCsv', 'inboundFlowId', 'somtodayClientId', 'somtodayClientSecret') | Where-Object { $_ -notin $parameterNames }).Count -eq 0) -Message 'infra/deploy-sync-job.bicep is missing required Job parameters.'
    Assert-Condition -Condition (@($forbiddenParameterNames | Where-Object { $_ -in $parameterNames }).Count -eq 0) -Message 'infra/deploy-sync-job.bicep exposes an unsupported parameter.'
    Assert-Condition -Condition (@($Template.resources | Where-Object type -EQ 'Microsoft.Resources/deployments').Count -gt 0) -Message 'infra/deploy-sync-job.bicep must contain the Container Apps Job deployment module.'
}

New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
$previousClientSecret = $env:SOMTODAY_CLIENT_SECRET

try {
    $mainTemplatePath = Join-Path $temporaryDirectory 'main.json'
    $deploySyncJobTemplatePath = Join-Path $temporaryDirectory 'deploy-sync-job.json'
    $mainParametersPath = Join-Path $temporaryDirectory 'main.parameters.json'
    $deploySyncJobParametersPath = Join-Path $temporaryDirectory 'deploy-sync-job.parameters.json'

    Write-Host 'Compiling Bicep templates and example parameter files.'
    if ($StandaloneBicep) {
        Invoke-Bicep -Arguments @('build', (Join-Path $infraRoot 'main.bicep'), '--outfile', $mainTemplatePath)
        Invoke-Bicep -Arguments @('build', (Join-Path $infraRoot 'deploy-sync-job.bicep'), '--outfile', $deploySyncJobTemplatePath)
    }
    else {
        Invoke-Bicep -Arguments @('build', '--file', (Join-Path $infraRoot 'main.bicep'), '--outfile', $mainTemplatePath)
        Invoke-Bicep -Arguments @('build', '--file', (Join-Path $infraRoot 'deploy-sync-job.bicep'), '--outfile', $deploySyncJobTemplatePath)
    }

    $env:SOMTODAY_CLIENT_SECRET = 'validation-only-value'
    if ($StandaloneBicep) {
        Invoke-Bicep -Arguments @('build-params', (Join-Path $infraRoot 'main.example.bicepparam'), '--outfile', $mainParametersPath)
        Invoke-Bicep -Arguments @('build-params', (Join-Path $infraRoot 'deploy-sync-job.example.bicepparam'), '--outfile', $deploySyncJobParametersPath)
    }
    else {
        Invoke-Bicep -Arguments @('build-params', '--file', (Join-Path $infraRoot 'main.example.bicepparam'), '--outfile', $mainParametersPath)
        Invoke-Bicep -Arguments @('build-params', '--file', (Join-Path $infraRoot 'deploy-sync-job.example.bicepparam'), '--outfile', $deploySyncJobParametersPath)
    }

    $mainTemplate = Get-Content -LiteralPath $mainTemplatePath -Raw | ConvertFrom-Json -Depth 100
    $deploySyncJobTemplate = Get-Content -LiteralPath $deploySyncJobTemplatePath -Raw | ConvertFrom-Json -Depth 100

    Assert-EnvironmentTemplate -Template $mainTemplate -TemplateName 'infra/main.bicep'
    Assert-JobTemplate -Template $deploySyncJobTemplate

    $mainBicep = Get-Content -LiteralPath (Join-Path $infraRoot 'main.bicep') -Raw
    $deploySyncJobBicep = Get-Content -LiteralPath (Join-Path $infraRoot 'deploy-sync-job.bicep') -Raw
    $syncJobBicep = Get-Content -LiteralPath (Join-Path $infraRoot 'sync-job.bicep') -Raw
    $jobBicep = Get-Content -LiteralPath (Join-Path $infraRoot 'job.bicep') -Raw
    $bicepConfiguration = Get-Content -LiteralPath (Join-Path $infraRoot 'bicepconfig.json') -Raw | ConvertFrom-Json

    Assert-Condition -Condition ($bicepConfiguration.extensions.PSObject.Properties.Name -contains 'microsoftGraphV1') -Message 'infra/bicepconfig.json must configure the microsoftGraphV1 extension.'
    Assert-Condition -Condition ($mainBicep.Contains("resource installationTag 'Microsoft.Resources/tags")) -Message 'infra/main.bicep must store the Environment name in the resource-group tag.'
    Assert-Condition -Condition ($deploySyncJobBicep.Contains('resourceGroup().tags')) -Message 'infra/deploy-sync-job.bicep must read the Environment name from the resource-group tag.'
    Assert-Condition -Condition ($syncJobBicep.Contains("var imageReference = 'ghcr.io/essella/somtoday2microsoftsds:latest'")) -Message 'infra/sync-job.bicep must use the fixed production image.'
    Assert-Condition -Condition ($syncJobBicep.Contains('var cronMinute =')) -Message 'infra/sync-job.bicep must calculate a deterministic cron minute.'
    Assert-Condition -Condition ($syncJobBicep.Contains('filter(normalizedIncludedLocationCodes')) -Message 'infra/sync-job.bicep must remove empty included location-code values.'
    Assert-Condition -Condition ($syncJobBicep.Contains('filter(normalizedExcludedLocationCodes')) -Message 'infra/sync-job.bicep must remove empty excluded location-code values.'
    Assert-Condition -Condition ([regex]::Matches($jobBicep, "resource\s+job\s+'Microsoft\.App/jobs").Count -eq 1) -Message 'infra/job.bicep must contain exactly one Microsoft.App/jobs resource.'
    Assert-Condition -Condition ($jobBicep.Contains("type: 'SystemAssigned'")) -Message 'infra/job.bicep must use a system-assigned identity.'
    foreach ($requiredGraphRole in @('IndustryData-InboundFlow.ReadWrite.All', 'IndustryData-DataConnector.Upload', 'IndustryData.ReadBasic.All')) {
        Assert-Condition -Condition ($syncJobBicep.Contains($requiredGraphRole)) -Message "Required Microsoft Graph role '$requiredGraphRole' is missing from infra/sync-job.bicep."
    }

    Write-Host 'Comparing compiled templates with the tracked ARM templates.'
    Assert-Condition -Condition ((Get-CanonicalJson -Path $mainTemplatePath) -ceq (Get-CanonicalJson -Path (Join-Path $infraRoot 'azuredeploy.json'))) -Message 'infra/azuredeploy.json is stale. Compile infra/main.bicep and commit the generated ARM template.'

    Write-Host 'Infrastructure validation succeeded.'
}
finally {
    if ($null -eq $previousClientSecret) {
        Remove-Item Env:SOMTODAY_CLIENT_SECRET -ErrorAction SilentlyContinue
    }
    else {
        $env:SOMTODAY_CLIENT_SECRET = $previousClientSecret
    }

    $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $resolvedTemporaryDirectory = [System.IO.Path]::GetFullPath($temporaryDirectory)
    $requiredPrefix = $resolvedTemporaryRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTemporaryDirectory.StartsWith($requiredPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected temporary path '$temporaryDirectory'."
    }

    if (Test-Path -LiteralPath $resolvedTemporaryDirectory) {
        Remove-Item -LiteralPath $resolvedTemporaryDirectory -Recurse -Force
    }
}
