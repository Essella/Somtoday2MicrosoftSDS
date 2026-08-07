[CmdletBinding()]
param(
    [string]$BicepExecutable = 'az',
    [switch]$StandaloneBicep,
    [string]$FormSchemaUri = 'https://schema.management.azure.com/schemas/2021-09-09/uiFormDefinition.schema.json'
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

New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
$previousClientSecret = $env:SOMTODAY_CLIENT_SECRET

try {
    $compiledTemplatePath = Join-Path $temporaryDirectory 'main.json'
    $mainParametersPath = Join-Path $temporaryDirectory 'main.parameters.json'
    $additionalJobParametersPath = Join-Path $temporaryDirectory 'additional-job.parameters.json'
    $formSchemaPath = Join-Path $temporaryDirectory 'uiFormDefinition.schema.json'

    Write-Host 'Compiling Bicep templates and example parameter files.'
    if ($StandaloneBicep) {
        Invoke-Bicep -Arguments @('build', (Join-Path $infraRoot 'main.bicep'), '--outfile', $compiledTemplatePath)
    }
    else {
        Invoke-Bicep -Arguments @('build', '--file', (Join-Path $infraRoot 'main.bicep'), '--outfile', $compiledTemplatePath)
    }

    $env:SOMTODAY_CLIENT_SECRET = 'validation-only-value'
    if ($StandaloneBicep) {
        Invoke-Bicep -Arguments @('build-params', (Join-Path $infraRoot 'main.example.bicepparam'), '--outfile', $mainParametersPath)
        Invoke-Bicep -Arguments @('build-params', (Join-Path $infraRoot 'additional-job.example.bicepparam'), '--outfile', $additionalJobParametersPath)
    }
    else {
        Invoke-Bicep -Arguments @('build-params', '--file', (Join-Path $infraRoot 'main.example.bicepparam'), '--outfile', $mainParametersPath)
        Invoke-Bicep -Arguments @('build-params', '--file', (Join-Path $infraRoot 'additional-job.example.bicepparam'), '--outfile', $additionalJobParametersPath)
    }

    $compiledTemplate = Get-Content -LiteralPath $compiledTemplatePath -Raw | ConvertFrom-Json -Depth 100
    $compiledResources = @($compiledTemplate.resources.PSObject.Properties.Value)
    $parameterNames = @($compiledTemplate.parameters.PSObject.Properties.Name)
    $environmentModes = @($compiledTemplate.parameters.environmentMode.allowedValues)

    Assert-Condition -Condition (@($compiledResources | Where-Object type -EQ 'Microsoft.Resources/deployments').Count -gt 0) -Message 'The compiled ARM template does not contain the Container Apps Job deployment module.'
    Assert-Condition -Condition ($compiledTemplate.parameters.somtodayClientSecret.type -ieq 'secureString') -Message 'somtodayClientSecret must compile to an ARM secureString parameter.'
    Assert-Condition -Condition ('cpu' -notin $parameterNames -and 'memory' -notin $parameterNames) -Message 'CPU and memory must remain fixed implementation details, not deployment parameters.'
    Assert-Condition -Condition ($environmentModes.Count -eq 2 -and $environmentModes[0] -eq 'new' -and $environmentModes[1] -eq 'existing') -Message 'environmentMode must allow exactly new and existing.'
    Assert-Condition -Condition ('existingContainerAppsEnvironmentResourceId' -in $parameterNames) -Message 'The existing Container Apps Environment resource ID parameter is missing.'
    Assert-Condition -Condition (@($compiledResources | Where-Object type -Like 'Microsoft.Storage/*').Count -eq 0) -Message 'The infrastructure template must not create Azure Storage resources.'
    Assert-Condition -Condition (@($compiledResources | Where-Object { $_.PSObject.Properties['identity'] -and $_.identity.type -eq 'UserAssigned' }).Count -eq 0) -Message 'The infrastructure template must not create a user-assigned identity.'

    $mainBicep = Get-Content -LiteralPath (Join-Path $infraRoot 'main.bicep') -Raw
    foreach ($requiredGraphRole in @(
            'IndustryData-InboundFlow.ReadWrite.All',
            'IndustryData-DataConnector.Upload',
            'IndustryData.ReadBasic.All'
        )) {
        Assert-Condition -Condition ($mainBicep.Contains($requiredGraphRole)) -Message "Required Microsoft Graph role '$requiredGraphRole' is missing from infra/main.bicep."
    }

    $jobBicep = Get-Content -LiteralPath (Join-Path $infraRoot 'job.bicep') -Raw
    $jobResourceCount = [regex]::Matches($jobBicep, "resource\s+job\s+'Microsoft\.App/jobs").Count
    Assert-Condition -Condition ($jobResourceCount -eq 1) -Message 'infra/job.bicep must contain exactly one Microsoft.App/jobs resource.'

    Write-Host 'Comparing the compiled template with infra/azuredeploy.json.'
    $compiledCanonical = Get-CanonicalJson -Path $compiledTemplatePath
    $trackedCanonical = Get-CanonicalJson -Path (Join-Path $infraRoot 'azuredeploy.json')
    Assert-Condition -Condition ($compiledCanonical -ceq $trackedCanonical) -Message 'infra/azuredeploy.json is stale. Compile infra/main.bicep and commit the generated ARM template.'

    Write-Host 'Validating the portal form against the official Azure schema.'
    Invoke-WebRequest -Uri $FormSchemaUri -OutFile $formSchemaPath
    $formPath = Join-Path $infraRoot 'uiFormDefinition.json'
    $formJson = Get-Content -LiteralPath $formPath -Raw
    Assert-Condition -Condition ($formJson | Test-Json -SchemaFile $formSchemaPath) -Message 'infra/uiFormDefinition.json does not satisfy the official Azure Form View schema.'

    $form = $formJson | ConvertFrom-Json -Depth 100
    $formParameterNames = @($form.view.outputs.parameters.PSObject.Properties.Name | Sort-Object)
    $compiledParameterNames = @($parameterNames | Sort-Object)
    $parameterDifference = @(Compare-Object -ReferenceObject $compiledParameterNames -DifferenceObject $formParameterNames)
    Assert-Condition -Condition ($parameterDifference.Count -eq 0) -Message 'The portal form outputs do not match the compiled ARM template parameters.'

    $environmentStep = @($form.view.properties.steps | Where-Object name -EQ 'environment')
    Assert-Condition -Condition ($environmentStep.Count -eq 1) -Message "The portal form must contain exactly one 'environment' step."
    $environmentMode = @($environmentStep[0].elements | Where-Object name -EQ 'environmentMode')
    Assert-Condition -Condition ($environmentMode.Count -eq 1 -and $environmentMode[0].defaultValue.value -eq 'existing') -Message "The portal form must default environmentMode to 'existing'."
    $environmentSelector = @($environmentStep[0].elements | Where-Object name -EQ 'existingEnvironment')
    Assert-Condition -Condition ($environmentSelector.Count -eq 1 -and $environmentSelector[0].type -eq 'Microsoft.Solutions.ResourceSelector' -and $environmentSelector[0].resourceType -eq 'Microsoft.App/managedEnvironments') -Message 'The portal form must select existing Microsoft.App/managedEnvironments resources natively.'

    $somtodayStep = @($form.view.properties.steps | Where-Object name -EQ 'somtoday')
    Assert-Condition -Condition ($somtodayStep.Count -eq 1) -Message "The portal form must contain exactly one 'somtoday' step."
    $secretControl = @($somtodayStep[0].elements | Where-Object name -EQ 'somtodayClientSecret')
    Assert-Condition -Condition ($secretControl.Count -eq 1 -and $secretControl[0].type -eq 'Microsoft.Common.PasswordBox') -Message 'The portal form must collect somtodayClientSecret with a PasswordBox.'

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
        throw "Refusing to remove unexpected temporary path '$resolvedTemporaryDirectory'."
    }

    if (Test-Path -LiteralPath $resolvedTemporaryDirectory) {
        Remove-Item -LiteralPath $resolvedTemporaryDirectory -Recurse -Force
    }
}
