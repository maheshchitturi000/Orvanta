// One Orvanta container app: scales to zero, probes /health on port 8080.

param name string
param location string
param environmentId string
param image string

@description('true only for the Gateway; every other service is reachable only inside the environment.')
param external bool = false

param registryServer string
param registryUsername string
@secure()
param registryPassword string

@description('{ items: [ { name, value } ] } - Container Apps secrets, referenced from env by secretRef.')
@secure()
param secrets object = {}

param env array = []

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  properties: {
    environmentId: environmentId
    configuration: {
      // Internal apps are called by the Gateway as http://<name>; the public one is HTTPS only.
      ingress: { external: external, targetPort: 8080, transport: 'auto', allowInsecure: !external }
      registries: [ { server: registryServer, username: registryUsername, passwordSecretRef: 'registry-password' } ]
      secrets: concat([ { name: 'registry-password', value: registryPassword } ], secrets.?items ?? [])
    }
    template: {
      containers: [
        {
          name: 'app'
          image: image
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: env
          probes: [
            { type: 'Startup', httpGet: { path: '/health', port: 8080 }, periodSeconds: 5, failureThreshold: 24 }
          ]
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 2 }
    }
  }
}

output fqdn string = app.properties.configuration.ingress.fqdn
