# PDF Generator API 🚀

[![CI/CD](https://github.com/your-username/pdf-generator-api/actions/workflows/ci.yml/badge.svg)](https://github.com/your-username/pdf-generator-api/actions/workflows/ci.yml)
[![Docker](https://img.shields.io/badge/docker-%230db7ed.svg?style=flat&logo=docker&logoColor=white)](https://docker.com)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)



# PDF Generation API with Watermark and Stamp using Playwright

## Overview

This lightweight .NET minimal API leverages [Microsoft.Playwright](https://playwright.dev/dotnet/) to generate PDFs from either a provided URL or full HTML content. It supports dynamic watermarking and stamping, allowing users to overlay custom watermark text and a stamp image on the PDF. The API is secured via API Key authentication and comes with integrated Swagger/OpenAPI documentation for easy testing and exploration.

## Features

- **PDF Generation from URL:** Render any webpage and generate a PDF.
- **PDF Generation from HTML:** Generate a PDF from complete HTML content.
- **Custom Watermark:**  
  - Optionally add watermark text with dynamic font sizing based on text length.
  - The watermark text is styled with an adjustable line height, rotated -45° for a diagonal appearance, and rendered with semi-transparency.
  - Includes logic to wrap long text to prevent overlapping.
- **Custom Stamp:**  
  - Optionally add a stamp image to the bottom-right corner.
  - The stamp is injected to the top layer with a high z-index and padded from the edges.
- **API Key Authentication:** Secures endpoints by requiring a valid API key provided in the request header.
- **Swagger/OpenAPI Integration:** Automatically generated API documentation and testing interface.

## Endpoints

### Generate PDF from URL

- **Endpoint:** `/api/pdf/from-url`
- **Method:** `POST`
- **Parameters:**
  - `url` (string, required): The URL of the webpage to render.
  - `watermarkText` (string, optional): Watermark text to overlay on the PDF.
  - `stampImageFile` (IFormFile, optional): An image file to use as a stamp.

### Generate PDF from HTML

- **Endpoint:** `/api/pdf/from-html`
- **Method:** `POST`
- **Parameters:**
  - `htmlContent` (string, required): The complete HTML content to render.
  - `watermarkText` (string, optional): Watermark text to overlay on the PDF.
  - `stampImageFile` (IFormFile, optional): An image file to use as a stamp.

## How It Works

1. **Rendering:**  
   The API uses Playwright to launch a headless Chromium browser. Depending on the endpoint, it either navigates to a given URL or sets the page content using the provided HTML.

2. **Watermark Injection:**  
   If watermark text is supplied, the API injects a DOM element via a JavaScript snippet. This element:
   - Is centered, rotated -45°, and rendered with a semi-transparent style.
   - Adjusts font size dynamically based on the length of the watermark text.
   - Uses line-height and text wrapping logic to ensure the text is fully visible without overlapping.

3. **Stamp Injection:**  
   If a stamp image is provided, it is converted to a Base64 data URL and injected into the page as a fixed element. The stamp is positioned at the bottom-right corner with appropriate padding and a high z-index to prevent being overlapped by other page elements.

4. **PDF Generation:**  
   Once the page is fully rendered with any injected elements, the API uses Playwright's PDF generation capabilities to create an A4 PDF with custom margins and background printing enabled.

## Setup and Running

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- Docker (if using Docker Compose)

### Local Setup

1. **Clone the Repository:**

   ```bash
   git clone <repository-url>
   cd <repository-folder>
   ```

2. **Configure API Key:**

   Set your API key in `appsettings.json` (or via environment variables) under the key `ApiKey`.

3. **Install Playwright Browsers:**

   The code automatically calls `Microsoft.Playwright.Program.Main(new string[] { "install" });` to install the required browsers.

4. **Run the Application:**

   ```bash
   dotnet run
   ```

5. **Access Swagger UI:**

   In development mode, navigate to `http://localhost:<port>/swagger` to explore and test the API endpoints.

### Running with Docker Compose

For a containerized setup, you can use the provided Dockerfile and docker-compose.yml files.

#### Dockerfile

Create a file named `Dockerfile` with the following content:

```dockerfile
# Use the official .NET 9 SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy the project files and restore dependencies
COPY . .
RUN dotnet restore

# Publish the project in Release configuration
RUN dotnet publish -c Release -o /app

# Use the official .NET 9 ASP.NET runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Copy the published app from the build stage
COPY --from=build /app .

# Expose port 80
EXPOSE 80

# Start the application
ENTRYPOINT ["dotnet", "YourProject.dll"]
```

*Note:* Replace `YourProject.dll` with the actual name of your published DLL.

#### docker-compose.yml

Create a file named `docker-compose.yml` with the following content:

```yaml
version: '3.8'
services:
  pdfgenerator:
    build: .
    ports:
      - "5000:80"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ApiKey=YOUR_API_KEY
```

*Note:* Replace `YOUR_API_KEY` with your actual API key. This configuration maps port 80 in the container to port 5000 on your host.

#### Running with Docker Compose

From the directory containing the `docker-compose.yml` file, run:

```bash
docker-compose up --build
```

The API will be available at [http://localhost:5000](http://localhost:5000).

## Client Test Command

You can test the API endpoints using curl. Below are example commands:

### Test PDF Generation from URL

```bash
curl -X POST "http://localhost:5000/api/pdf/from-url" \
  -H "X-API-KEY: YOUR_API_KEY" \
  -F "url=https://example.com" \
  -F "watermarkText=Confidential Document" \
  -F "stampImageFile=@/path/to/stamp.png" --output generated_from_url.pdf
```

### Test PDF Generation from HTML

```bash
curl -X POST "http://localhost:5000/api/pdf/from-html" \
  -H "X-API-KEY: YOUR_API_KEY" \
  -F "htmlContent=<html><body><h1>Hello, World!</h1></body></html>" \
  -F "watermarkText=Draft Version" \
  -F "stampImageFile=@/path/to/stamp.png" --output generated_from_html.pdf
```

*Notes:*

- Replace `YOUR_API_KEY` with your actual API key.
- Adjust the URL, HTML content, watermark text, and stamp image path as needed.
- The `--output` option saves the PDF file to your local machine.

## API Key Authentication

All endpoints are secured using API key authentication. Ensure that your requests include the API key in the header `X-API-KEY`.

## Additional Notes

- **Watermark Text Wrapping:**  
  A helper function is provided to insert line breaks into long watermark texts, ensuring proper display without overlapping. Adjustments such as line-height and white-space settings further enhance the text's appearance.

- **Customization:**  
  The inline JavaScript and CSS used for injecting watermark and stamp elements can be modified as needed to better match specific design requirements.

## 🛠️ DevOps & Deployment

### Automated Package Updates

The project includes automated dependency management:

- **Dependabot**: Automatically creates PRs for dependency updates weekly
- **Version Ranges**: Uses flexible version ranges (e.g., `1.47.*`) for minor updates
- **Update Script**: Run `./update-packages.sh` to manually update packages

### CI/CD Pipeline

GitHub Actions workflow includes:
- ✅ Automated builds and tests
- ✅ Docker image building and testing
- ✅ Security vulnerability scanning with Trivy
- ✅ Dependency vulnerability checks
- ✅ Health check validation

### Docker Best Practices

- 🏗️ **Multi-stage builds** for optimized image size
- 🔒 **Non-root user** for enhanced security
- 🏥 **Health checks** with `/health` endpoint
- 📋 **Proper labeling** for container management
- 🚀 **Security optimizations** (no-new-privileges, read-only root filesystem)

### Local Development

```bash
# Using Docker Compose
docker-compose up --build

# Direct Docker run
docker build -t pdf-generator-api .
docker run -p 8080:8080 pdf-generator-api
```

### Production Deployment

The application is optimized for cloud deployments (Northflank, Railway, Render, etc.):

```bash
# Build for production
docker build -t your-registry/pdf-generator-api:latest .
docker push your-registry/pdf-generator-api:latest
```

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `ApiKey` | API authentication key | `your-api-key-here` |
| `ASPNETCORE_ENVIRONMENT` | Environment mode | `Production` |
| `ASPNETCORE_URLS` | Server URLs | `http://+:8080` |

## 📊 Monitoring & Health

- **Health Endpoint**: `GET /health` returns JSON status
- **Docker Health Checks**: Automatic container health monitoring
- **Structured Logging**: Built-in ASP.NET Core logging

## 🧪 Testing

```bash
# Run tests
dotnet test

# Test health endpoint
curl http://localhost:8080/health

# Test with Docker
docker run --rm pdf-generator-api curl -f http://localhost:8080/health
```

## Dependencies

- **.NET 9.0** - Runtime framework
- **Microsoft.Playwright** - Browser automation
- **Swashbuckle.AspNetCore** - OpenAPI documentation

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

**Made with ❤️ using .NET 9 and Playwright**



