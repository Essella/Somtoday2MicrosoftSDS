[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$environmentTagName = 'Somtoday2MicrosoftSDS.environment'
$jobTagName = 'Somtoday2MicrosoftSDS.instance'
$requiredRoleValues = @(
    'IndustryData-InboundFlow.ReadWrite.All'
    'IndustryData-DataConnector.Upload'
    'IndustryData.ReadBasic.All'
)

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

function Ensure-MicrosoftGraphAuthenticationModule {
    if ($null -eq (Get-Module -ListAvailable -Name Microsoft.Graph.Authentication)) {
        Write-Host 'Installing Microsoft.Graph.Authentication for the Graph permission assignment.'
        Install-Module -Name Microsoft.Graph.Authentication -Scope CurrentUser -Repository PSGallery -Force -AllowClobber
    }

    Import-Module Microsoft.Graph.Authentication -ErrorAction Stop
}

function Get-SomtodaySyncJobs {
    $resourceGroups = @(Invoke-Az -Arguments @('group', 'list', '--output', 'json') | Out-String | ConvertFrom-Json)
    $jobs = @(
        foreach ($resourceGroup in $resourceGroups) {
            if ($null -eq $resourceGroup.tags -or $null -eq $resourceGroup.tags.PSObject.Properties[$environmentTagName]) {
                continue
            }

            $resourceGroupJobs = @(Invoke-Az -Arguments @('containerapp', 'job', 'list', '--resource-group', $resourceGroup.name, '--output', 'json') | Out-String | ConvertFrom-Json)
            foreach ($job in $resourceGroupJobs) {
                $jobTag = $null -eq $job.tags ? $null : $job.tags.PSObject.Properties[$jobTagName]
                $principalId = [string]$job.identity.principalId
                if ($null -eq $jobTag -or [string]::IsNullOrWhiteSpace($principalId)) {
                    continue
                }

                [pscustomobject]@{
                    ResourceGroupName = $resourceGroup.name
                    JobName = $job.name
                    PrincipalId = $principalId.Trim()
                }
            }
        }
    )

    return $jobs
}

function Grant-JobGraphRoles {
    param(
        [Parameter(Mandatory)]
        [object[]]$Jobs
    )

    $createdGraphConnection = $null -eq (Get-MgContext)

    try {
        Connect-MgGraph -Scopes @('Application.Read.All', 'AppRoleAssignment.ReadWrite.All') -NoWelcome

        $graphServicePrincipals = @((Invoke-MgGraphRequest -Method GET -Uri "https://graph.microsoft.com/v1.0/servicePrincipals?`$filter=appId%20eq%20'00000003-0000-0000-c000-000000000000'&`$select=id,appRoles").value)
        if ($graphServicePrincipals.Count -ne 1) {
            throw 'Microsoft Graph service principal could not be resolved uniquely.'
        }

        $graphServicePrincipal = $graphServicePrincipals[0]
        $rolesByValue = @{}
        foreach ($roleValue in $requiredRoleValues) {
            $roles = @($graphServicePrincipal.appRoles | Where-Object { $_.value -eq $roleValue -and $_.allowedMemberTypes -contains 'Application' })
            if ($roles.Count -ne 1) {
                throw "Microsoft Graph must expose exactly one application role named '$roleValue'."
            }

            $rolesByValue[$roleValue] = $roles[0]
        }

        foreach ($job in $Jobs) {
            $existingAssignments = @((Invoke-MgGraphRequest -Method GET -Uri "https://graph.microsoft.com/v1.0/servicePrincipals/$($job.PrincipalId)/appRoleAssignments?`$select=appRoleId,resourceId").value)
            foreach ($roleValue in $requiredRoleValues) {
                $role = $rolesByValue[$roleValue]
                $isAssigned = $null -ne ($existingAssignments | Where-Object { $_.appRoleId -eq $role.id -and $_.resourceId -eq $graphServicePrincipal.id })
                if ($isAssigned) {
                    continue
                }

                $body = @{
                    principalId = $job.PrincipalId
                    resourceId = $graphServicePrincipal.id
                    appRoleId = $role.id
                } | ConvertTo-Json -Compress
                Invoke-MgGraphRequest -Method POST -Uri "https://graph.microsoft.com/v1.0/servicePrincipals/$($job.PrincipalId)/appRoleAssignments" -Body $body -ContentType 'application/json' | Out-Null
                Write-Host "Assigned '$roleValue' to '$($job.JobName)' in '$($job.ResourceGroupName)'."
            }
        }
    }
    finally {
        if ($createdGraphConnection -and $null -ne (Get-MgContext)) {
            Disconnect-MgGraph | Out-Null
        }
    }
}

Invoke-Az -Arguments @('account', 'show', '--output', 'none') | Out-Null
$jobs = @(Get-SomtodaySyncJobs)
if ($jobs.Count -eq 0) {
    throw "No Somtoday2MicrosoftSDS Jobs with the '$jobTagName' tag were found in visible resource groups with the '$environmentTagName' tag. Deploy a sync Job first."
}

Write-Host "Found $($jobs.Count) Somtoday2MicrosoftSDS Job(s):"
foreach ($job in $jobs) {
    Write-Host "- $($job.ResourceGroupName): $($job.JobName)"
}

Ensure-MicrosoftGraphAuthenticationModule
Grant-JobGraphRoles -Jobs $jobs
