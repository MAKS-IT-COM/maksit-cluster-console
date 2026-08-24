#requires -Version 7.0
#requires -PSEdition Core

function Test-RepoUtilsUseVaultEnabled {
    param(
        [Parameter(Mandatory = $true)]
        $Settings
    )

    if ($null -eq $Settings -or -not ($Settings.PSObject.Properties.Name -contains 'useVault')) {
        return $false
    }

    $value = $Settings.useVault
    if ($value -is [bool]) {
        return $value
    }

    return [string]$value -eq 'true'
}

function ConvertFrom-VaultConnectionSecret {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Raw
    )

    $trimmed = $Raw.Trim()
    $separator = $trimmed.IndexOf('|')
    if ($separator -lt 1 -or $separator -ge ($trimmed.Length - 1)) {
        throw "Vault connection must be 'baseAddress|apiKey' (pipe separator). Example: http://172.16.0.14|your-key"
    }

    $baseAddress = $trimmed.Substring(0, $separator).Trim().TrimEnd('/')
    $apiKey = $trimmed.Substring($separator + 1).Trim()
    if ([string]::IsNullOrWhiteSpace($baseAddress) -or [string]::IsNullOrWhiteSpace($apiKey)) {
        throw "Vault connection is missing base address or API key."
    }

    return [pscustomobject]@{
        BaseAddress = $baseAddress
        ApiKey      = $apiKey
    }
}

function Get-RepoUtilsVaultScope {
    param(
        [Parameter(Mandatory = $true)]
        $Settings
    )

    $organization = [Environment]::GetEnvironmentVariable('MAKSIT_VAULT_ORGANIZATION')
    if ([string]::IsNullOrWhiteSpace($organization) -and ($Settings.PSObject.Properties.Name -contains 'vaultOrganization')) {
        $organization = [string]$Settings.vaultOrganization
    }

    $application = [Environment]::GetEnvironmentVariable('MAKSIT_VAULT_APPLICATION')
    if ([string]::IsNullOrWhiteSpace($application) -and ($Settings.PSObject.Properties.Name -contains 'vaultApplication')) {
        $application = [string]$Settings.vaultApplication
    }

    $organization = if ($null -eq $organization) { '' } else { $organization.Trim() }
    $application = if ($null -eq $application) { '' } else { $application.Trim() }

    if ([string]::IsNullOrWhiteSpace($organization) -or [string]::IsNullOrWhiteSpace($application)) {
        throw "useVault is true but vault organization/application are not set. Set vaultOrganization/vaultApplication in scriptSettings.json or MAKSIT_VAULT_ORGANIZATION / MAKSIT_VAULT_APPLICATION."
    }

    return [pscustomobject]@{
        Organization = $organization
        Application  = $application
    }
}

function Get-RepoUtilsVaultConnectionSecretName {
    param(
        [Parameter(Mandatory = $true)]
        $Settings
    )

    if (($Settings.PSObject.Properties.Name -contains 'vaultConnectionSecret') -and
        -not [string]::IsNullOrWhiteSpace([string]$Settings.vaultConnectionSecret)) {
        return ([string]$Settings.vaultConnectionSecret).Trim()
    }

    return 'MAKSIT_VAULT'
}

function Add-RepoUtilsSecretNamesFromObject {
    param(
        $Object,
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Names
    )

    if ($null -eq $Object -or $Object -is [string] -or $Object -is [ValueType]) {
        return
    }

    if ($Object -is [System.Collections.IDictionary]) {
        foreach ($key in @($Object.Keys)) {
            $name = [string]$key
            $value = $Object[$key]
            if ($name -like '*Secret') {
                $trimmed = ([string]$value).Trim()
                if (-not [string]::IsNullOrWhiteSpace($trimmed) -and
                    -not [string]::Equals($trimmed, 'WebhookSecret', [System.StringComparison]::Ordinal)) {
                    [void]$Names.Add($trimmed)
                }
            }
            else {
                Add-RepoUtilsSecretNamesFromObject -Object $value -Names $Names
            }
        }

        return
    }

    if ($Object -is [System.Collections.IEnumerable]) {
        foreach ($item in @($Object)) {
            Add-RepoUtilsSecretNamesFromObject -Object $item -Names $Names
        }

        return
    }

    if ($null -eq $Object.PSObject) {
        return
    }

    foreach ($property in $Object.PSObject.Properties) {
        if ($property.MemberType -notin @('NoteProperty', 'Property')) {
            continue
        }

        $value = $property.Value
        if ($property.Name -like '*Secret') {
            $trimmed = ([string]$value).Trim()
            if (-not [string]::IsNullOrWhiteSpace($trimmed) -and
                -not [string]::Equals($trimmed, 'WebhookSecret', [System.StringComparison]::Ordinal)) {
                [void]$Names.Add($trimmed)
            }

            continue
        }

        Add-RepoUtilsSecretNamesFromObject -Object $value -Names $Names
    }
}

function Get-EnabledPluginSecretNames {
    param(
        [Parameter(Mandatory = $true)]
        $Plugins
    )

    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($plugin in @($Plugins)) {
        if ($null -eq $plugin) {
            continue
        }

        if (($plugin.PSObject.Properties.Name -contains 'enabled') -and ($plugin.enabled -eq $false)) {
            continue
        }

        Add-RepoUtilsSecretNamesFromObject -Object $plugin -Names $names
    }

    return @($names)
}

function Get-VaultNameFilterExpression {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $escaped = $Name.Replace('\', '\\').Replace('"', '\"')
    return "Name == `"$escaped`""
}

function Import-RepoUtilsVaultClientModule {
    $modulePath = [Environment]::GetEnvironmentVariable('MAKSIT_VAULT_MODULE_PATH')
    if (-not [string]::IsNullOrWhiteSpace($modulePath)) {
        Import-Module $modulePath -Force -ErrorAction Stop
        return
    }

    if (Get-Command Connect-Vault -ErrorAction SilentlyContinue) {
        return
    }

    Import-Module 'MaksIT.Vault.Client.PowerShell' -ErrorAction SilentlyContinue
}

function Find-RepoUtilsVaultSecretMatch {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OrganizationName,

        [Parameter(Mandatory = $true)]
        [string]$ApplicationName,

        [Parameter(Mandatory = $true)]
        [string]$SecretName,

        [Parameter(Mandatory = $true)]
        [pscustomobject]$Connection,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Cmdlet', 'Rest')]
        [string]$Mode
    )

    $orgFilter = Get-VaultNameFilterExpression -Name $OrganizationName
    $appFilter = Get-VaultNameFilterExpression -Name $ApplicationName
    $pageNumber = 1

    while ($true) {
        $items = @()
        $hasNext = $false

        if ($Mode -eq 'Cmdlet') {
            $response = Search-VaultSecrets -PageNumber $pageNumber -PageSize 100 `
                -OrganizationFilters $orgFilter -ApplicationFilters $appFilter
            if ($null -ne $response -and $null -ne $response.Items) {
                $items = @($response.Items)
            }
            if ($null -ne $response) {
                $hasNext = [bool]$response.HasNextPage
            }
        }
        else {
            $body = @{
                pageNumber          = $pageNumber
                pageSize            = 100
                organizationFilters = $orgFilter
                applicationFilters  = $appFilter
            } | ConvertTo-Json -Compress
            $uri = "$($Connection.BaseAddress)/api/vault/secrets"
            $headers = @{ 'X-API-KEY' = $Connection.ApiKey }
            $response = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -ContentType 'application/json' -Body $body
            if ($null -ne $response.items) {
                $items = @($response.items)
            }
            elseif ($null -ne $response.Items) {
                $items = @($response.Items)
            }
            $hasNext = [bool]($response.hasNextPage)
            if (-not $hasNext) {
                $hasNext = [bool]($response.HasNextPage)
            }
        }

        foreach ($item in $items) {
            $itemName = [string]$(if ($item.PSObject.Properties.Name -contains 'Name') { $item.Name } else { $item.name })
            if ([string]::Equals($itemName, $SecretName, [System.StringComparison]::Ordinal)) {
                return $item
            }
        }

        if (-not $hasNext) {
            break
        }

        $pageNumber++
    }

    return $null
}

function Get-RepoUtilsVaultSecretValue {
    param(
        [Parameter(Mandatory = $true)]
        $Match,

        [Parameter(Mandatory = $true)]
        [pscustomobject]$Connection,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Cmdlet', 'Rest')]
        [string]$Mode
    )

    $organizationId = if ($Match.PSObject.Properties.Name -contains 'OrganizationId') { $Match.OrganizationId } else { $Match.organizationId }
    $applicationId = if ($Match.PSObject.Properties.Name -contains 'ApplicationId') { $Match.ApplicationId } else { $Match.applicationId }
    $secretId = if ($Match.PSObject.Properties.Name -contains 'Id') { $Match.Id } else { $Match.id }

    if ($Mode -eq 'Cmdlet') {
        $version = Get-VaultSecret -OrganizationId $organizationId -ApplicationId $applicationId -SecretId $secretId -SecretVersion 'current'
        if ($null -eq $version -or [string]::IsNullOrWhiteSpace([string]$version.Value)) {
            return $null
        }
        return [string]$version.Value
    }

    $uri = "$($Connection.BaseAddress)/api/vault/organization/$organizationId/application/$applicationId/secret/$secretId" +
        '?secretVersion=current'
    $headers = @{ 'X-API-KEY' = $Connection.ApiKey }
    $version = Invoke-RestMethod -Method Get -Uri $uri -Headers $headers
    $value = if ($version.PSObject.Properties.Name -contains 'Value') { $version.Value } else { $version.value }
    if ([string]::IsNullOrWhiteSpace([string]$value)) {
        return $null
    }
    return [string]$value
}

function Resolve-RepoUtilsVaultSecretValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OrganizationName,

        [Parameter(Mandatory = $true)]
        [string]$ApplicationName,

        [Parameter(Mandatory = $true)]
        [string]$SecretName,

        [Parameter(Mandatory = $true)]
        [pscustomobject]$Connection,

        [Parameter(Mandatory = $true)]
        [string]$Mode
    )

    foreach ($application in @($ApplicationName, 'Shared')) {
        $match = Find-RepoUtilsVaultSecretMatch `
            -OrganizationName $OrganizationName `
            -ApplicationName $application `
            -SecretName $SecretName `
            -Connection $Connection `
            -Mode $Mode
        if ($null -eq $match) {
            continue
        }

        $value = Get-RepoUtilsVaultSecretValue -Match $match -Connection $Connection -Mode $Mode
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            Write-Log -Level 'INFO' -Message "Resolved Vault secret '$SecretName' from $OrganizationName/$application"
            return $value
        }
    }

    return $null
}

function Initialize-RepoUtilsVaultSecrets {
    param(
        [Parameter(Mandatory = $true)]
        $Settings,

        [Parameter(Mandatory = $true)]
        $Plugins
    )

    if (-not (Test-RepoUtilsUseVaultEnabled -Settings $Settings)) {
        return
    }

    $connectionName = Get-RepoUtilsVaultConnectionSecretName -Settings $Settings
    $raw = [Environment]::GetEnvironmentVariable($connectionName)
    if ([string]::IsNullOrWhiteSpace($raw)) {
        throw "useVault is true but environment variable '$connectionName' is not set (expected baseAddress|apiKey)."
    }

    $connection = ConvertFrom-VaultConnectionSecret -Raw $raw
    $scope = Get-RepoUtilsVaultScope -Settings $Settings
    $secretNames = @(Get-EnabledPluginSecretNames -Plugins $Plugins)

    Write-Log -Level 'INFO' -Message "Vault mode: loading $($secretNames.Count) plugin secret(s) for $($scope.Organization)/$($scope.Application)"

    $mode = 'Rest'
    try {
        Import-RepoUtilsVaultClientModule
        if (Get-Command Connect-Vault -ErrorAction SilentlyContinue) {
            Connect-Vault -BaseAddress $connection.BaseAddress -ApiKey $connection.ApiKey
            $mode = 'Cmdlet'
            Write-Log -Level 'INFO' -Message 'Connected to Vault with MaksIT.Vault.Client.PowerShell'
        }
        else {
            Write-Log -Level 'INFO' -Message 'MaksIT.Vault.Client.PowerShell not found; using Vault HTTP API'
        }
    }
    catch {
        Write-Log -Level 'INFO' -Message "Vault PowerShell module not loaded ($($_.Exception.Message)); using Vault HTTP API"
        $mode = 'Rest'
    }

    foreach ($secretName in $secretNames) {
        $value = Resolve-RepoUtilsVaultSecretValue `
            -OrganizationName $scope.Organization `
            -ApplicationName $scope.Application `
            -SecretName $secretName `
            -Connection $connection `
            -Mode $mode
        if ([string]::IsNullOrWhiteSpace($value)) {
            throw "Vault secret '$secretName' was not found for $($scope.Organization)/$($scope.Application) (or Shared) or has no current version."
        }

        [Environment]::SetEnvironmentVariable($secretName, $value, 'Process')
    }
}

Export-ModuleMember -Function @(
    'Test-RepoUtilsUseVaultEnabled',
    'ConvertFrom-VaultConnectionSecret',
    'Get-RepoUtilsVaultScope',
    'Get-RepoUtilsVaultConnectionSecretName',
    'Get-EnabledPluginSecretNames',
    'Initialize-RepoUtilsVaultSecrets'
)
