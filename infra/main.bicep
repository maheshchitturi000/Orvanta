// Orvanta on Azure free tiers. Deployed by .github/workflows/deploy.yml (see docs/DEPLOYMENT.md).
//
//   Azure Container Apps (Consumption)  gateway + Angular app (public), auth, food, jobs, ai, conversation (internal)
//                                       every app scales to zero; covered by the monthly free grant
//   Azure SQL Database free offer       one serverless database, pauses when the free monthly limit is used up
//   Azure Functions (Flex Consumption)  the job discovery / apply workers; covered by the monthly free grant
//   Storage account                     job queues + Functions deployment package (a few cents a month at most)
//   Log Analytics                       container logs, capped well inside the free 5 GB a month

targetScope = 'resourceGroup'

@description('Short lowercase prefix for every resource name.')
@maxLength(12)
param prefix string = 'orvanta'

param location string = resourceGroup().location

@description('Container image tag to deploy (the git commit SHA).')
param imageTag string

@description('Registry holding the images, e.g. ghcr.io/<github-user>.')
param imageRepository string

param registryServer string = 'ghcr.io'
param registryUsername string
@secure()
param registryPassword string

@description('Public URL of the app. Leave empty to use the Container Apps address.')
param publicUrl string = ''

param sqlAdminLogin string = 'orvantaadmin'
@secure()
param sqlAdminPassword string

@secure()
param jwtSigningKey string
@secure()
param jwtEncryptionKey string
@secure()
param passwordPepper string
@secure()
param otpHmacKey string
@secure()
param dataProtectionKey string
@secure()
param jobsSecretsKey string

param smtpHost string = ''
param smtpPort int = 587
param smtpUserName string = ''
@secure()
param smtpPassword string = ''
param smtpFromEmail string = ''

@description('"openai" for any OpenAI-compatible API (Groq, Gemini...), or "none" to run without AI.')
param llmProvider string = 'openai'
param llmBaseUrl string = 'https://api.groq.com/openai/v1/'
param llmModel string = 'llama-3.1-8b-instant'
@secure()
param llmApiKey string = ''

@secure()
param jsearchApiKey string = ''
param adzunaAppId string = ''
@secure()
param adzunaAppKey string = ''

@description('Azure Functions runtime version for the .NET isolated worker.')
param functionsDotnetVersion string = '10.0'

var suffix = uniqueString(resourceGroup().id)
var gatewayName = '${prefix}-gateway'
var appUrl = empty(publicUrl) ? 'https://${gatewayName}.${environment.properties.defaultDomain}' : publicUrl

// ---------- Logs ----------

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs-${suffix}'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: json('0.15') }
  }
}

// ---------- SQL (free offer) ----------

resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${prefix}-sql-${suffix}'
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Lets Azure services (Container Apps, Functions) reach the server. The deploy workflow adds and removes
// a rule for its own runner while it applies the schema.
resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sql
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource db 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: 'Orvanta'
  location: location
  sku: { name: 'GP_S_Gen5_2', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
  }
}

// ConnectRetryCount/Interval + a long timeout ride out the ~1 minute resume after the database pauses.
var sqlConnection = 'Server=tcp:${sql.properties.fullyQualifiedDomainName},1433;Database=${db.name};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=True;TrustServerCertificate=False;Connection Timeout=90;ConnectRetryCount=6;ConnectRetryInterval=10'

// ---------- Storage (job queues + Functions package) ----------

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take('${prefix}st${suffix}', 24)
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource deployContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobs
  name: 'functions-package'
}

var storageConnection = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${az.environment().suffixes.storage}'

// ---------- Container Apps ----------

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var sharedSecrets = [
  { name: 'sql-connection', value: sqlConnection }
  { name: 'jwt-signing-key', value: jwtSigningKey }
  { name: 'jwt-encryption-key', value: jwtEncryptionKey }
]

var sharedEnv = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  // Trust X-Forwarded-For/Proto from the Gateway (client IP in logs and audit). Only the Gateway can reach these apps.
  { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
  { name: 'ConnectionStrings__Orvanta', secretRef: 'sql-connection' }
  { name: 'Jwt__SigningKey', secretRef: 'jwt-signing-key' }
  { name: 'Jwt__EncryptionKey', secretRef: 'jwt-encryption-key' }
]

var registry = {
  registryServer: registryServer
  registryUsername: registryUsername
}

// Optional keys: Container Apps secrets can't be empty, so they are added only when provided.
var llmKeySecret = empty(llmApiKey) ? [] : [ { name: 'llm-api-key', value: llmApiKey } ]

module auth 'modules/container-app.bicep' = {
  name: 'app-auth'
  params: {
    name: '${prefix}-auth'
    location: location
    environmentId: environment.id
    image: '${imageRepository}/orvanta-auth:${imageTag}'
    registryServer: registry.registryServer
    registryUsername: registry.registryUsername
    registryPassword: registryPassword
    secrets: {
      items: concat(sharedSecrets, [
        { name: 'password-pepper', value: passwordPepper }
        { name: 'otp-hmac-key', value: otpHmacKey }
      ], empty(smtpPassword) ? [] : [ { name: 'smtp-password', value: smtpPassword } ])
    }
    env: concat(sharedEnv, [
      { name: 'AuthSecurity__PasswordPepper', secretRef: 'password-pepper' }
      { name: 'AuthSecurity__OtpHmacKey', secretRef: 'otp-hmac-key' }
      { name: 'Smtp__Host', value: smtpHost }
      { name: 'Smtp__Port', value: string(smtpPort) }
      { name: 'Smtp__UserName', value: smtpUserName }
      { name: 'Smtp__FromEmail', value: smtpFromEmail }
    ], empty(smtpPassword) ? [] : [ { name: 'Smtp__Password', secretRef: 'smtp-password' } ])
  }
}

module food 'modules/container-app.bicep' = {
  name: 'app-food'
  params: {
    name: '${prefix}-food'
    location: location
    environmentId: environment.id
    image: '${imageRepository}/orvanta-food:${imageTag}'
    registryServer: registry.registryServer
    registryUsername: registry.registryUsername
    registryPassword: registryPassword
    secrets: {
      items: concat(sharedSecrets, [ { name: 'data-protection-key', value: dataProtectionKey } ])
    }
    env: concat(sharedEnv, [
      { name: 'DataProtection__Key', secretRef: 'data-protection-key' }
      { name: 'FoodProviders__CallbackBaseUrl', value: '${appUrl}/api/integrations' }
      { name: 'FoodProviders__FrontendIntegrationsUrl', value: '${appUrl}/integrations' }
      { name: 'Ondc__LocalBaseUrl', value: appUrl }
    ])
  }
}

module jobs 'modules/container-app.bicep' = {
  name: 'app-jobs'
  params: {
    name: '${prefix}-jobs'
    location: location
    environmentId: environment.id
    image: '${imageRepository}/orvanta-jobs:${imageTag}'
    registryServer: registry.registryServer
    registryUsername: registry.registryUsername
    registryPassword: registryPassword
    secrets: {
      items: concat(sharedSecrets, [
        { name: 'jobs-secrets-key', value: jobsSecretsKey }
        { name: 'storage-connection', value: storageConnection }
      ], llmKeySecret,
        empty(jsearchApiKey) ? [] : [ { name: 'jsearch-api-key', value: jsearchApiKey } ],
        empty(adzunaAppKey) ? [] : [ { name: 'adzuna-app-key', value: adzunaAppKey } ])
    }
    env: concat(sharedEnv, [
      { name: 'Jobs__SecretsKey', secretRef: 'jobs-secrets-key' }
      { name: 'Jobs__QueueConnectionString', secretRef: 'storage-connection' }
      { name: 'Jobs__Llm__Provider', value: llmProvider }
      { name: 'Jobs__Llm__BaseUrl', value: llmBaseUrl }
      { name: 'Jobs__Llm__Model', value: llmModel }
      { name: 'Jobs__Adzuna__AppId', value: adzunaAppId }
    ], empty(llmApiKey) ? [] : [ { name: 'Jobs__Llm__ApiKey', secretRef: 'llm-api-key' } ],
      empty(jsearchApiKey) ? [] : [ { name: 'Jobs__JSearch__ApiKey', secretRef: 'jsearch-api-key' } ],
      empty(adzunaAppKey) ? [] : [ { name: 'Jobs__Adzuna__AppKey', secretRef: 'adzuna-app-key' } ])
  }
}

module ai 'modules/container-app.bicep' = {
  name: 'app-ai'
  params: {
    name: '${prefix}-ai'
    location: location
    environmentId: environment.id
    image: '${imageRepository}/orvanta-ai:${imageTag}'
    registryServer: registry.registryServer
    registryUsername: registry.registryUsername
    registryPassword: registryPassword
    secrets: {
      items: concat(sharedSecrets, llmKeySecret)
    }
    env: concat(sharedEnv, [
      { name: 'Llm__Provider', value: llmProvider }
      { name: 'Llm__BaseUrl', value: llmBaseUrl }
      { name: 'Llm__Model', value: llmModel }
      { name: 'Llm__TimeoutSeconds', value: '60' }
    ], empty(llmApiKey) ? [] : [ { name: 'Llm__ApiKey', secretRef: 'llm-api-key' } ])
  }
}

module conversation 'modules/container-app.bicep' = {
  name: 'app-conversation'
  params: {
    name: '${prefix}-conversation'
    location: location
    environmentId: environment.id
    image: '${imageRepository}/orvanta-conversation:${imageTag}'
    registryServer: registry.registryServer
    registryUsername: registry.registryUsername
    registryPassword: registryPassword
    secrets: { items: sharedSecrets }
    env: sharedEnv
  }
}

module gateway 'modules/container-app.bicep' = {
  name: 'app-gateway'
  params: {
    name: gatewayName
    location: location
    environmentId: environment.id
    image: '${imageRepository}/orvanta-gateway:${imageTag}'
    external: true
    registryServer: registry.registryServer
    registryUsername: registry.registryUsername
    registryPassword: registryPassword
    env: [
      { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
      { name: 'Cors__AllowedOrigins__0', value: appUrl }
      { name: 'ReverseProxy__Clusters__auth__Destinations__primary__Address', value: 'http://${prefix}-auth/' }
      { name: 'ReverseProxy__Clusters__food__Destinations__primary__Address', value: 'http://${prefix}-food/' }
      { name: 'ReverseProxy__Clusters__jobs__Destinations__primary__Address', value: 'http://${prefix}-jobs/' }
      { name: 'ReverseProxy__Clusters__ai__Destinations__primary__Address', value: 'http://${prefix}-ai/' }
      { name: 'ReverseProxy__Clusters__conversation__Destinations__primary__Address', value: 'http://${prefix}-conversation/' }
    ]
  }
  dependsOn: [ auth, food, jobs, ai, conversation ]
}

// ---------- Azure Functions (Flex Consumption) ----------

resource functionsPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${prefix}-functions-plan'
  location: location
  kind: 'functionapp'
  sku: { tier: 'FlexConsumption', name: 'FC1' }
  properties: { reserved: true }
}

resource functions 'Microsoft.Web/sites@2024-04-01' = {
  name: '${prefix}-jobs-functions-${suffix}'
  location: location
  kind: 'functionapp,linux'
  properties: {
    serverFarmId: functionsPlan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deployContainer.name}'
          authentication: { type: 'StorageAccountConnectionString', storageAccountConnectionStringName: 'DEPLOYMENT_STORAGE_CONNECTION_STRING' }
        }
      }
      scaleAndConcurrency: { maximumInstanceCount: 40, instanceMemoryMB: 512 }
      runtime: { name: 'dotnet-isolated', version: functionsDotnetVersion }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'AzureWebJobsStorage', value: storageConnection }
        { name: 'DEPLOYMENT_STORAGE_CONNECTION_STRING', value: storageConnection }
        { name: 'ConnectionStrings__Orvanta', value: sqlConnection }
        { name: 'Jobs__QueueConnectionString', value: storageConnection }
        { name: 'Jobs__SecretsKey', value: jobsSecretsKey }
        { name: 'Jobs__Llm__Provider', value: llmProvider }
        { name: 'Jobs__Llm__BaseUrl', value: llmBaseUrl }
        { name: 'Jobs__Llm__Model', value: llmModel }
        { name: 'Jobs__Llm__ApiKey', value: llmApiKey }
        { name: 'Jobs__JSearch__ApiKey', value: jsearchApiKey }
        { name: 'Jobs__Adzuna__AppId', value: adzunaAppId }
        { name: 'Jobs__Adzuna__AppKey', value: adzunaAppKey }
      ]
    }
  }
}

output appUrl string = appUrl
output sqlServerName string = sql.name
output sqlServerFqdn string = sql.properties.fullyQualifiedDomainName
output functionAppName string = functions.name
