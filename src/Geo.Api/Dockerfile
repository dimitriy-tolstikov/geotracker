# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY GeoTracker.sln ./
COPY src/Geo.Domain/Geo.Domain.csproj src/Geo.Domain/
COPY src/Geo.Infrastructure/Geo.Infrastructure.csproj src/Geo.Infrastructure/
COPY src/Geo.Api/Geo.Api.csproj src/Geo.Api/
RUN dotnet restore src/Geo.Api/Geo.Api.csproj
COPY src/ src/
RUN dotnet publish src/Geo.Api/Geo.Api.csproj -c Release -o /app/publish --no-restore

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Geo.Api.dll"]
