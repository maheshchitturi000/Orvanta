# Public entry point: the Gateway, with the Angular app in wwwroot so the web app and the API share one
# origin (the refresh cookie is SameSite=Strict). Build context: repo root.
#   docker build -t orvanta-gateway .

FROM node:22-alpine AS web
WORKDIR /web
COPY package.json package-lock.json ./
RUN npm ci
COPY angular.json tsconfig.json tsconfig.app.json ./
COPY public ./public
COPY src ./src
# Every route renders in the browser, so a static build is all the Gateway needs to serve.
RUN npx ng build --output-mode static

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Microservices/ .
RUN dotnet publish src/Gateway/Orvanta.Gateway/Orvanta.Gateway.csproj -c Release -o /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ENV ASPNETCORE_HTTP_PORTS=8080
WORKDIR /app
COPY --from=build /app .
COPY --from=web /web/dist/orvanta-ai/browser ./wwwroot
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Orvanta.Gateway.dll"]
