# PDF Generator API - Multi-stage Docker build with Playwright support
# Optimized for DevOps with security, performance, and maintainability in mind

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS base
WORKDIR /app

RUN apt-get update && apt-get install -y \
    libnss3 libatk1.0-0 libatk-bridge2.0-0 libcups2 \
    libdrm2 libxkbcommon0 libxcomposite1 libxdamage1 \
    libxrandr2 libgbm1 libasound2 libpangocairo-1.0-0 \
    libx11-xcb1 libxcb1 fonts-noto-cjk \
    libgtk-3-0 libxshmfence1 curl

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["PdfGeneratorApi.csproj", "./"]
RUN dotnet restore "./PdfGeneratorApi.csproj"
COPY . .
RUN dotnet build "PdfGeneratorApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PdfGeneratorApi.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Copy all source files for Playwright installation
COPY . .

# Install Playwright CLI and browsers with dependencies
RUN dotnet tool install --global Microsoft.Playwright.CLI
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet build "PdfGeneratorApi.csproj" -c Release
RUN playwright install chromium

# Configure container
EXPOSE 8080

# Add health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

# Add metadata labels
LABEL org.opencontainers.image.title="PDF Generator API" \
      org.opencontainers.image.description="A .NET Web API for generating PDFs from URLs or HTML content" \
      org.opencontainers.image.version="1.0.0" \
      org.opencontainers.image.authors="Your Name" \
      org.opencontainers.image.source="https://github.com/yourusername/pdf-generator-api"

# Use non-root user for security
RUN groupadd -r appuser && useradd -r -g appuser appuser
RUN chown -R appuser:appuser /app
USER appuser

ENTRYPOINT ["dotnet", "PdfGeneratorApi.dll"]
