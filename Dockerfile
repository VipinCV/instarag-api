# ─── Build Stage ─────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files for efficient layer caching
COPY InstaRAG.sln .
COPY src/InstaRAG.Api/InstaRAG.Api.csproj src/InstaRAG.Api/
COPY src/InstaRAG.ImportProducts/InstaRAG.ImportProducts.csproj src/InstaRAG.ImportProducts/
COPY tests/InstaRAG.Tests/InstaRAG.Tests.csproj tests/InstaRAG.Tests/

# Restore NuGet packages
RUN dotnet restore

# Copy all source code and build
COPY . .
RUN dotnet publish src/InstaRAG.Api/InstaRAG.Api.csproj -c Release -o /app/publish --no-restore

# ─── Runtime Stage ───────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create non-root user for security
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Copy published output
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Expose the port Render expects
EXPOSE 8080

# Switch to non-root user
USER appuser

ENTRYPOINT ["dotnet", "InstaRAG.Api.dll"]
