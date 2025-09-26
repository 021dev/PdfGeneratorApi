using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Microsoft.Playwright;

var builder = WebApplication.CreateBuilder(args);

// Register Playwright Chromium browser as a singleton service
builder.Services.AddSingleton(async _ =>
{
    var playwright = await Playwright.CreateAsync();
    return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
});

// Add Swagger/OpenAPI support with API Key security
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API Key Authentication",
        Name = "X-API-KEY",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "ApiKeyScheme"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" },
                Scheme = "ApiKeyScheme",
                Name = "X-API-KEY",
                In = ParameterLocation.Header,
            },
            new List<string>()
        }
    });
});

var app = builder.Build();

// Enable Swagger UI in development environment
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Simple API Key authentication middleware
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var requestId = Guid.NewGuid().ToString().Substring(0, 8);

    logger.LogInformation("[{RequestId}] Incoming request: {Method} {Path} from {RemoteIp}",
        requestId, context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress);

    // Skip authentication for health check endpoint
    if (context.Request.Path.StartsWithSegments("/health"))
    {
        logger.LogInformation("[{RequestId}] Health check request - skipping authentication", requestId);
        await next();
        logger.LogInformation("[{RequestId}] Health check completed", requestId);
        return;
    }

    // Check for API key header
    if (!context.Request.Headers.TryGetValue("X-API-KEY", out var extractedApiKey))
    {
        logger.LogWarning("[{RequestId}] API key missing in request headers. Available headers: {Headers}",
            requestId, string.Join(", ", context.Request.Headers.Keys));
        context.Response.StatusCode = 401; // Unauthorized
        await context.Response.WriteAsync("API Key is missing");
        return;
    }

    // Get configured API key
    var config = context.RequestServices.GetRequiredService<IConfiguration>();
    var configuredApiKey = config.GetValue<string>("ApiKey");

    // Mask API keys for logging (show first 4 and last 4 characters)
    var receivedMasked = MaskApiKey(extractedApiKey!);
    var configuredMasked = MaskApiKey(configuredApiKey ?? "");

    logger.LogInformation("[{RequestId}] API key validation - Received: {ReceivedKey}, Configured: {ConfiguredKey}",
        requestId, receivedMasked, configuredMasked);

    // Validate API key
    if (string.IsNullOrEmpty(configuredApiKey))
    {
        logger.LogError("[{RequestId}] No API key configured in application settings", requestId);
        context.Response.StatusCode = 500; // Internal Server Error
        await context.Response.WriteAsync("Server configuration error");
        return;
    }

    if (!configuredApiKey.Equals(extractedApiKey))
    {
        logger.LogWarning("[{RequestId}] API key mismatch - authentication failed. Keys match: {KeysMatch}",
            requestId, configuredApiKey.Equals(extractedApiKey, StringComparison.Ordinal));
        context.Response.StatusCode = 401; // Unauthorized
        await context.Response.WriteAsync("Unauthorized client");
        return;
    }

    logger.LogInformation("[{RequestId}] API key authentication successful", requestId);
    await next();
    logger.LogInformation("[{RequestId}] Request processing completed", requestId);
});

// Endpoint to generate PDF from a URL
app.MapPost("/api/pdf/from-url", async (
    [FromServices] Task<IBrowser> browserTask,
    [FromServices] ILogger<Program> logger,
    string url,
    string? watermarkText,
    IFormFile? stampImageFile) =>
{
    var requestId = Guid.NewGuid().ToString().Substring(0, 8);
    logger.LogInformation("[{RequestId}] PDF generation from URL requested: {Url}", requestId, url);

    var browser = await browserTask;
    var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();

    try
    {
        // Navigate to the provided URL
        logger.LogDebug("[{RequestId}] Navigating to URL: {Url}", requestId, url);
        await page.GotoAsync(url);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        logger.LogDebug("[{RequestId}] Page loaded successfully", requestId);

        // Add watermark and stamp via direct script injection, which is more reliable
        if (!string.IsNullOrEmpty(watermarkText))
        {
            logger.LogDebug("[{RequestId}] Adding watermark: {WatermarkText}", requestId, watermarkText);
            await page.EvaluateAsync($@"
                // Create watermark element
                const watermark = document.createElement('div');
                watermark.style.position = 'fixed';
                watermark.style.top = '0';
                watermark.style.left = '0';
                watermark.style.width = '100%';
                watermark.style.height = '100%';
                watermark.style.display = 'flex';
                watermark.style.alignItems = 'center';
                watermark.style.justifyContent = 'center';
                watermark.style.pointerEvents = 'none';
                watermark.style.zIndex = '9999';

                // Add watermark text
                const watermarkText = document.createElement('div');
                watermarkText.innerText = '{watermarkText}';
                watermarkText.style.color = 'rgba(0, 0, 0, 0.15)';
                watermarkText.style.fontSize = '{(watermarkText.Length <= 10 ? "4.5rem" :
                                               watermarkText.Length <= 20 ? "4rem" :
                                               watermarkText.Length <= 30 ? "3.5rem" :
                                               watermarkText.Length <= 40 ? "3rem" : "2.5rem")}';
                watermarkText.style.fontWeight = 'bold';
                watermarkText.style.transform = 'rotate(-45deg)';
                watermarkText.style.whiteSpace = 'nowrap';
                watermarkText.style.textAlign = 'center';
                watermarkText.style.lineHeight = '1.2';

                watermark.appendChild(watermarkText);
                document.body.appendChild(watermark);
            ");
        }

        // Add stamp if provided
        if (stampImageFile != null)
        {
            logger.LogDebug("[{RequestId}] Adding stamp image: {FileName} ({Size} bytes)",
                requestId, stampImageFile.FileName, stampImageFile.Length);
            var imageUrl = await GetImageUrlAsync(stampImageFile);
            await page.EvaluateAsync($@"
                // Create stamp element
                const stamp = document.createElement('div');
                stamp.style.position = 'fixed';
                stamp.style.bottom = '30px';
                stamp.style.right = '30px';
                stamp.style.width = '150px';
                stamp.style.height = '150px';
                stamp.style.backgroundImage = 'url(""{imageUrl}"")';
                stamp.style.backgroundSize = 'contain';
                stamp.style.backgroundRepeat = 'no-repeat';
                stamp.style.backgroundPosition = 'center';
                stamp.style.pointerEvents = 'none';
                stamp.style.zIndex = '10000';

                document.body.appendChild(stamp);
            ");
        }

        // Wait a bit for the DOM changes to be processed
        logger.LogDebug("[{RequestId}] Waiting for DOM updates", requestId);
        await page.WaitForTimeoutAsync(500);

        // Generate and return the PDF
        logger.LogInformation("[{RequestId}] Generating PDF", requestId);
        var pdfStream = await page.PdfAsync(new PagePdfOptions
        {
            Format = PaperFormat.A4,
            PrintBackground = true,
            Margin = new() { Top = "1cm", Bottom = "1cm", Left = "1cm", Right = "1cm" }
        });

        logger.LogInformation("[{RequestId}] PDF generated successfully, size: {Size} bytes", requestId, pdfStream.Length);
        return Results.File(pdfStream, "application/pdf", "generated.pdf");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[{RequestId}] Error generating PDF from URL: {Url}", requestId, url);
        return Results.Problem("Error generating PDF", statusCode: 500);
    }
    finally
    {
        // Clean up browser context
        await context.DisposeAsync();
        logger.LogDebug("[{RequestId}] Browser context disposed", requestId);
    }
}).DisableAntiforgery();

// Endpoint to generate PDF from full HTML content
app.MapPost("/api/pdf/from-html", async (
    [FromServices] Task<IBrowser> browserTask,
    [FromServices] ILogger<Program> logger,
    string htmlContent,
    string? watermarkText,
    IFormFile? stampImageFile) =>
{
    var requestId = Guid.NewGuid().ToString().Substring(0, 8);
    logger.LogInformation("[{RequestId}] PDF generation from HTML requested: {ContentLength} chars",
        requestId, htmlContent?.Length ?? 0);

    var browser = await browserTask;
    var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();

    try
    {
        // Set the page content with the provided HTML
        logger.LogDebug("[{RequestId}] Setting HTML content", requestId);
        await page.SetContentAsync(htmlContent);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        logger.LogDebug("[{RequestId}] HTML content loaded successfully", requestId);

        // Add watermark directly via script for better reliability
        if (!string.IsNullOrEmpty(watermarkText))
        {
            logger.LogDebug("[{RequestId}] Adding watermark to HTML: {WatermarkText}", requestId, watermarkText);
        await page.EvaluateAsync($@"
            // Create watermark element
            const watermark = document.createElement('div');
            watermark.style.position = 'fixed';
            watermark.style.top = '0';
            watermark.style.left = '0';
            watermark.style.width = '100%';
            watermark.style.height = '100%';
            watermark.style.display = 'flex';
            watermark.style.alignItems = 'center';
            watermark.style.justifyContent = 'center';
            watermark.style.pointerEvents = 'none';
            watermark.style.zIndex = '9999';
            
            // Add watermark text
            const watermarkText = document.createElement('div');
            watermarkText.innerText = '{watermarkText}';
            watermarkText.style.color = 'rgba(0, 0, 0, 0.15)';
            watermarkText.style.fontSize = '{(watermarkText.Length <= 10 ? "4.5rem" :
                                           watermarkText.Length <= 20 ? "4rem" :
                                           watermarkText.Length <= 30 ? "3.5rem" :
                                           watermarkText.Length <= 40 ? "3rem" : "2.5rem")}';
            watermarkText.style.fontWeight = 'bold';
            watermarkText.style.transform = 'rotate(-45deg)';
            watermarkText.style.whiteSpace = 'nowrap';
            watermarkText.style.textAlign = 'center';
            watermarkText.style.lineHeight = '1.2';
            
            watermark.appendChild(watermarkText);
            document.body.appendChild(watermark);
        ");
    }

        // Add stamp if provided
        if (stampImageFile != null)
        {
            logger.LogDebug("[{RequestId}] Adding stamp to HTML: {FileName} ({Size} bytes)",
                requestId, stampImageFile.FileName, stampImageFile.Length);
            var imageUrl = await GetImageUrlAsync(stampImageFile);
        await page.EvaluateAsync($@"
            // Create stamp element
            const stamp = document.createElement('div');
            stamp.style.position = 'fixed';
            stamp.style.bottom = '30px';
            stamp.style.right = '30px';
            stamp.style.width = '180px';
            stamp.style.height = '180px';
            stamp.style.backgroundImage = 'url(""{imageUrl}"")';
            stamp.style.backgroundSize = 'contain';
            stamp.style.backgroundRepeat = 'no-repeat';
            stamp.style.backgroundPosition = 'center';
            stamp.style.pointerEvents = 'none';
            stamp.style.zIndex = '10000';
            
            document.body.appendChild(stamp);
        ");
    }

        // Wait a bit for the DOM changes to be processed
        logger.LogDebug("[{RequestId}] Waiting for DOM updates in HTML", requestId);
        await page.WaitForTimeoutAsync(500);

        // Generate and return the PDF
        logger.LogInformation("[{RequestId}] Generating PDF from HTML", requestId);
        var pdfStream = await page.PdfAsync(new PagePdfOptions
        {
            Format = PaperFormat.A4,
            PrintBackground = true,
            Margin = new() { Top = "1cm", Bottom = "1cm", Left = "1cm", Right = "1cm" }
        });

        logger.LogInformation("[{RequestId}] PDF generated successfully from HTML, size: {Size} bytes", requestId, pdfStream.Length);
        return Results.File(pdfStream, "application/pdf", "generated.pdf");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[{RequestId}] Error generating PDF from HTML: {ContentPreview}",
            requestId, htmlContent?.Substring(0, Math.Min(100, htmlContent?.Length ?? 0)) ?? "empty");
        return Results.Problem("Error generating PDF", statusCode: 500);
    }
    finally
    {
        // Clean up browser context
        await context.DisposeAsync();
        logger.LogDebug("[{RequestId}] Browser context disposed", requestId);
    }
}).DisableAntiforgery();

// Health check endpoint
app.MapGet("/health", (ILogger<Program> logger) =>
{
    logger.LogInformation("Health check requested from {RemoteIp}", "client");
    var response = new { status = "healthy", timestamp = DateTime.UtcNow };
    logger.LogInformation("Health check response: {Status}", response.status);
    return Results.Ok(response);
});

// Debug endpoint to check configuration precedence
app.MapGet("/debug/config", (IConfiguration config, ILogger<Program> logger) =>
{
    var apiKey = config.GetValue<string>("ApiKey");
    var maskedKey = MaskApiKey(apiKey ?? "");

    logger.LogInformation("Configuration debug - ApiKey: {MaskedKey}, Source: {Source}",
        maskedKey, apiKey == "your-api-key-here" ? "appsettings.json" : "environment/override");

    return Results.Ok(new {
        apiKeyConfigured = !string.IsNullOrEmpty(apiKey),
        apiKeyMasked = maskedKey,
        configurationSource = apiKey == "your-api-key-here" ? "appsettings.json" : "environment_override"
    });
});

app.Run();

// Helper function to mask API keys for secure logging
string MaskApiKey(string apiKey)
{
    if (string.IsNullOrEmpty(apiKey)) return "[empty]";
    if (apiKey.Length <= 8) return new string('*', apiKey.Length);

    return apiKey.Substring(0, 4) + new string('*', apiKey.Length - 8) + apiKey.Substring(apiKey.Length - 4);
}

// Helper function to convert an uploaded image file to a Base64 data URL
async Task<string> GetImageUrlAsync(IFormFile imageFile)
{
    using var memoryStream = new MemoryStream();
    await imageFile.CopyToAsync(memoryStream);
    var bytes = memoryStream.ToArray();
    var base64 = Convert.ToBase64String(bytes);
    var contentType = imageFile.ContentType;
    return $"data:{contentType};base64,{base64}";
}
