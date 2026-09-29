# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Disable Husky git hooks during container builds
ENV HUSKY=0

# Copy solution and project file to leverage Docker layer caching for dependency restore
COPY AidlyPms.slnx ./
COPY src/Host/AidlyPms.Api/AidlyPms.Api.csproj ./src/Host/AidlyPms.Api/
RUN dotnet restore src/Host/AidlyPms.Api/AidlyPms.Api.csproj

# Copy source and publish release build
COPY src ./src
RUN dotnet publish src/Host/AidlyPms.Api/AidlyPms.Api.csproj -c Release -o /app/publish --no-restore

# Run stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for HEALTHCHECK and ensure non-root user with UID 1000 exists
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && (id -u 1000 >/dev/null 2>&1 || useradd -m -u 1000 user) \
    && mkdir -p /home/user /app \
    && chown -R 1000:1000 /home/user /app

# The user with UID 1000 is required for Hugging Face Spaces & security best practices
USER 1000

# Set home environment for the user
ENV HOME=/home/user \
    PATH=/home/user/.local/bin:$PATH

# Copy published application and ensure user (1000) owns it
COPY --from=build --chown=1000:1000 /app/publish ./

# Hugging Face Requirement: listen and expose port 7860
EXPOSE 7860
ENV ASPNETCORE_HTTP_PORTS=7860 \
    ASPNETCORE_URLS=http://+:7860 \
    ASPNETCORE_ENVIRONMENT=Production

# .NET runtime tuning for container memory limits
# DOTNET_GCHeapHardLimitPercent sizes the GC heap to 75% of the container's cgroup memory limit (analogous to Java's MaxRAMPercentage=75.0)
ENV DOTNET_GCHeapHardLimitPercent=75 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_RUNNING_IN_CONTAINER=true

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:7860/health || exit 1

# Shell form so environment variables expand. Starts ASP.NET Core API listening on port 7860.
ENTRYPOINT ["sh", "-c", "dotnet AidlyPms.Api.dll --urls http://+:7860"]
