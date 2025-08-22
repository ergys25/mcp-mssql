# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY src/MSSQL.MCP/MSSQL.MCP.csproj src/MSSQL.MCP/
COPY Directory.Build.props Directory.Build.props
COPY Directory.Packages.props Directory.Packages.props
COPY NuGet.Config NuGet.Config

# Restore dependencies
RUN dotnet restore src/MSSQL.MCP/MSSQL.MCP.csproj

# Copy all source code
COPY src/ src/
COPY scripts/ scripts/

# Build and publish the application
WORKDIR /src/src/MSSQL.MCP
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install curl for health checks (optional)
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=build /app/publish .

# Create non-root user for security
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

# Set environment variables
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV ASPNETCORE_URLS=http://+:8585

# Expose port for HTTP/SSE endpoint
EXPOSE 8585

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD dotnet --info || exit 1

# Entry point
ENTRYPOINT ["dotnet", "MSSQL.MCP.dll"]
