using Microsoft.AspNetCore.Mvc;
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
    if (!context.Request.Headers.TryGetValue("X-API-KEY", out var extractedApiKey))
    {
        context.Response.StatusCode = 401; // Unauthorized
        await context.Response.WriteAsync("API Key is missing");
        return;
    }
    var config = context.RequestServices.GetRequiredService<IConfiguration>();
    var apiKey = config.GetValue<string>("ApiKey");
    if (string.IsNullOrEmpty(apiKey) || !apiKey.Equals(extractedApiKey))
    {
        context.Response.StatusCode = 401; // Unauthorized
        await context.Response.WriteAsync("Unauthorized client");
        return;
    }
    await next();
});

// Endpoint to generate PDF from a URL
app.MapPost("/api/pdf/from-url", async (
    [FromServices] Task<IBrowser> browserTask,
    string url,
    string? watermarkText,
    IFormFile? stampImageFile) =>
{
    var browser = await browserTask;
    var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();

    // Navigate to the provided URL
    await page.GotoAsync(url);
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Add watermark and stamp via direct script injection, which is more reliable
    if (!string.IsNullOrEmpty(watermarkText))
    {
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
    await page.WaitForTimeoutAsync(500);

    // Generate and return the PDF
    var pdfStream = await page.PdfAsync(new PagePdfOptions
    {
        Format = PaperFormat.A4,
        PrintBackground = true,
        Margin = new() { Top = "1cm", Bottom = "1cm", Left = "1cm", Right = "1cm" }
    });

    return Results.File(pdfStream, "application/pdf", "generated.pdf");
}).DisableAntiforgery();

// Endpoint to generate PDF from full HTML content
app.MapPost("/api/pdf/from-html", async (
    [FromServices] Task<IBrowser> browserTask,
    string htmlContent,
    string? watermarkText,
    IFormFile? stampImageFile) =>
{
    var browser = await browserTask;
    var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();

    // Set the page content with the provided HTML
    await page.SetContentAsync(htmlContent);
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

    // Add watermark directly via script for better reliability
    if (!string.IsNullOrEmpty(watermarkText))
    {
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
    await page.WaitForTimeoutAsync(500);

    // Generate and return the PDF
    var pdfStream = await page.PdfAsync(new PagePdfOptions
    {
        Format = PaperFormat.A4,
        PrintBackground = true,
        Margin = new() { Top = "1cm", Bottom = "1cm", Left = "1cm", Right = "1cm" }
    });

    return Results.File(pdfStream, "application/pdf", "generated.pdf");
}).DisableAntiforgery();

app.Run();

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
