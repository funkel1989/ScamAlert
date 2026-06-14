targetScope = 'resourceGroup'

@description('Environment name, e.g. staging or prod.')
param environmentName string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Short prefix used in resource names (letters and numbers only).')
@minLength(3)
@maxLength(12)
param namePrefix string = 'scamalert'

@description('SQL Server administrator login (not "admin").')
param sqlAdminLogin string = 'scamalertadmin'

@secure()
@description('SQL Server administrator password.')
param sqlAdminPassword string

@description('App Service plan SKU name.')
param appServicePlanSku string = 'B1'

@description('Optional custom domain hostname (without https://). Leave empty to use the default azurewebsites.net URL.')
param customDomainHost string = ''

@description('Email address to receive monitoring alerts.')
param alertEmailAddress string

@description('Stripe Price ID for the pro plan (e.g. price_abc123). Get this from the Stripe Dashboard.')
param stripePriceIdPro string = 'price_REPLACE_799_USD_MONTHLY'

var uniqueSuffix = uniqueString(resourceGroup().id, environmentName, location)
var tags = {
  application: 'ScamAlert'
  environment: environmentName
}

// --- Monitoring ---
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${namePrefix}-${environmentName}-${uniqueSuffix}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${namePrefix}-${environmentName}-${uniqueSuffix}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
}

// --- Storage (installer blobs, Phase 3+) ---
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take(toLower('st${namePrefix}${environmentName}${uniqueSuffix}'), 24)
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource installersContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'installers'
  properties: {
    publicAccess: 'None'
  }
}

// --- Key Vault ---
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: take('kv-${namePrefix}-${environmentName}-${uniqueSuffix}', 24)
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enabledForTemplateDeployment: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

// --- SQL ---
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${namePrefix}-${environmentName}-${uniqueSuffix}'
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'ScamAlert'
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: 2147483648
  }
}

var webAppName = 'app-${namePrefix}-${environmentName}-${uniqueSuffix}'
var webAppDefaultHostName = '${webAppName}.azurewebsites.net'
var webAppPublicBaseUrl = empty(customDomainHost) ? 'https://${webAppDefaultHostName}' : 'https://${customDomainHost}'

var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabase.name};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

resource secretSqlConnection 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ConnectionStrings--ScamAlertDb'
  properties: {
    value: sqlConnectionString
  }
}

// Placeholders — set real values in Key Vault after first deploy (see infra/README.md).
resource secretJwtSigningKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Authentication--Jwt--SigningKey'
  properties: {
    value: 'REPLACE_WITH_32_PLUS_CHAR_RANDOM_SECRET_BEFORE_GO_LIVE'
  }
}

resource secretStripeSecretKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Stripe--SecretKey'
  properties: {
    value: 'REPLACE_WITH_STRIPE_SECRET_KEY'
  }
}

resource secretStripeWebhookSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Stripe--WebhookSecret'
  properties: {
    value: 'REPLACE_WITH_STRIPE_WEBHOOK_SECRET'
  }
}

resource secretTwilioAccountSid 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Twilio--AccountSid'
  properties: {
    value: 'REPLACE_WITH_TWILIO_ACCOUNT_SID'
  }
}

resource secretTwilioAuthToken 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Twilio--AuthToken'
  properties: {
    value: 'REPLACE_WITH_TWILIO_AUTH_TOKEN'
  }
}

resource secretTwilioFromPhone 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Twilio--FromPhoneNumber'
  properties: {
    value: 'REPLACE_WITH_TWILIO_FROM_PHONE'
  }
}

resource secretSendGridApiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'Email--SendGridApiKey'
  properties: {
    value: 'REPLACE_WITH_SENDGRID_API_KEY'
  }
}

// --- App Service ---
resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'asp-${namePrefix}-${environmentName}-${uniqueSuffix}'
  location: location
  tags: tags
  sku: {
    name: appServicePlanSku
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  tags: tags
  dependsOn: [
    secretSqlConnection
    secretJwtSigningKey
    secretStripeSecretKey
    secretStripeWebhookSecret
    secretTwilioAccountSid
    secretTwilioAuthToken
    secretTwilioFromPhone
    secretSendGridApiKey
  ]
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/api/health'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environmentName == 'prod' ? 'Production' : 'Staging'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
        {
          name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE'
          value: 'false'
        }
        {
          name: 'Web__PublicBaseUrl'
          value: webAppPublicBaseUrl
        }
        {
          name: 'Web__InstallerDownloadUrl'
          value: '${storageAccount.properties.primaryEndpoints.blob}installers/'
        }
        {
          name: 'Twilio__WebhookPublicBaseUrl'
          value: webAppPublicBaseUrl
        }
        {
          name: 'Twilio__StatusCallbackBaseUrl'
          value: '${webAppPublicBaseUrl}/api/webhooks/twilio/status'
        }
        {
          name: 'AllowedHosts'
          value: empty(customDomainHost) ? webAppDefaultHostName : '${customDomainHost};${webAppDefaultHostName}'
        }
        {
          name: 'Email__FromAddress'
          value: 'noreply@scamalert.com'
        }
        {
          name: 'Email__FromDisplayName'
          value: 'ScamAlert'
        }
        {
          name: 'Web__LegalEntityName'
          value: 'ScamAlert'
        }
        {
          name: 'Web__SupportEmail'
          value: 'support@scamalert.com'
        }
        {
          name: 'Billing__Tiers__0__StripePriceId'
          value: stripePriceIdPro
        }
        {
          name: 'ConnectionStrings__ScamAlertDb'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=ConnectionStrings--ScamAlertDb)'
        }
        {
          name: 'Authentication__Jwt__SigningKey'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=Authentication--Jwt--SigningKey)'
        }
        {
          name: 'Stripe__SecretKey'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=Stripe--SecretKey)'
        }
        {
          name: 'Stripe__WebhookSecret'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=Stripe--WebhookSecret)'
        }
        {
          name: 'Twilio__AccountSid'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=Twilio--AccountSid)'
        }
        {
          name: 'Twilio__AuthToken'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=Twilio--AuthToken)'
        }
        {
          name: 'Twilio__FromPhoneNumber'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=Twilio--FromPhoneNumber)'
        }
        {
          name: 'Email__SendGridApiKey'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=Email--SendGridApiKey)'
        }
      ]
    }
  }
}

resource webAppKeyVaultAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, webApp.id, 'KeyVaultSecretsUser')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// --- Monitoring Alerts ---

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-${namePrefix}-${environmentName}'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'ScamAlert'
    enabled: true
    emailReceivers: [
      {
        name: 'ops-email'
        emailAddress: alertEmailAddress
        useCommonAlertSchema: true
      }
    ]
  }
}

resource alertHttp5xx 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-http5xx-${namePrefix}-${environmentName}'
  location: 'global'
  tags: tags
  properties: {
    description: 'Fires when HTTP 5xx errors exceed 10 in 5 minutes.'
    severity: 1
    enabled: true
    scopes: [webApp.id]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'Http5xxCriteria'
          metricName: 'Http5xx'
          operator: 'GreaterThan'
          threshold: 10
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

resource alertSlowResponse 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-slow-response-${namePrefix}-${environmentName}'
  location: 'global'
  tags: tags
  properties: {
    description: 'Fires when average response time exceeds 5 seconds over 10 minutes.'
    severity: 2
    enabled: true
    scopes: [webApp.id]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'ResponseTimeCriteria'
          metricName: 'AverageResponseTime'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

resource alertHealthCheckDown 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-health-down-${namePrefix}-${environmentName}'
  location: 'global'
  tags: tags
  properties: {
    description: 'Fires when the App Service health check reports the app as unavailable.'
    severity: 0
    enabled: true
    scopes: [webApp.id]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'HealthCheckCriteria'
          metricName: 'HealthCheckStatus'
          operator: 'LessThan'
          threshold: 1
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

resource alertEscalationErrors 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-escalation-errors-${namePrefix}-${environmentName}'
  location: location
  tags: tags
  properties: {
    description: 'Fires when the alert escalation worker logs errors, indicating SMS notifications may not be sent.'
    severity: 1
    enabled: true
    scopes: [logAnalytics.id]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: '''
            AppTraces
            | where Properties["OriginalFormat"] has "Alert escalation pass failed"
            | summarize ErrorCount = count()
          '''
          timeAggregation: 'Count'
          metricMeasureColumn: 'ErrorCount'
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}

resource alertAuthFailureSpike 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-auth-failures-${namePrefix}-${environmentName}'
  location: location
  tags: tags
  properties: {
    description: 'Fires when auth failures exceed 50 in 15 minutes — potential brute-force attack.'
    severity: 2
    enabled: true
    scopes: [logAnalytics.id]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: '''
            AppTraces
            | where Properties["OriginalFormat"] has "AUDIT auth failed"
            | summarize FailureCount = count()
          '''
          timeAggregation: 'Count'
          metricMeasureColumn: 'FailureCount'
          operator: 'GreaterThan'
          threshold: 50
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [actionGroup.id]
    }
  }
}

output resourceGroupName string = resourceGroup().name
output webAppName string = webApp.name
output webAppDefaultHostName string = webApp.properties.defaultHostName
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output keyVaultName string = keyVault.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output storageAccountName string = storageAccount.name
output installersContainerName string = installersContainer.name
