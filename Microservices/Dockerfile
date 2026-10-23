# One image per backend service. Build context: Microservices/
#   docker build -f Microservices/Dockerfile Microservices \
#     --build-arg PROJECT=src/Services/Orvanta.AuthService/Orvanta.AuthService.csproj \
#     --build-arg ASSEMBLY=Orvanta.AuthService -t orvanta-auth
# The Gateway (which also serves the Angular app) is built by the Dockerfile in the repo root.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN dotnet publish "$PROJECT" -c Release -o /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG ASSEMBLY
ENV ASSEMBLY=${ASSEMBLY} \
    ASPNETCORE_HTTP_PORTS=8080
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$ASSEMBLY.dll\""]
