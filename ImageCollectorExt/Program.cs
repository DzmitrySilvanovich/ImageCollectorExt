using Azure.Identity;
using Azure.Storage.Blobs;
using ImageCollectorExt.Repository;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Azure.Security.KeyVault.Secrets;

var builder = WebApplication.CreateBuilder(args);

var _configuration = builder.Configuration;
string? keyVaultTenantId = _configuration.GetValue<string>("SecretCredential:KeyVaultTenantId");
string? KeyVaultClientId = _configuration.GetValue<string>("SecretCredential:KeyVaultClientId");
string? KeyVaultClientSecret = _configuration.GetValue<string>("SecretCredential:KeyVaultClientSecret");
string? KeyVaultUri = _configuration.GetValue<string>("KeyVaultUri"); // read logDb connection 

var credential = new ClientSecretCredential(keyVaultTenantId, KeyVaultClientId, KeyVaultClientSecret);

var secretClient = new SecretClient(new Uri(KeyVaultUri), credential);

var connectionString = secretClient.GetSecret("DBConnectionString").Value.Value.ToString(); ;

string blobConnectionString = secretClient.GetSecret("BlobConnectionString").Value.Value.ToString();
string blobContainerName = secretClient.GetSecret("BlbContainerName").Value.Value.ToString();

BlobServiceClient blobServiceClient;
BlobContainerClient containerClient;

blobServiceClient = new BlobServiceClient(blobConnectionString);
containerClient = blobServiceClient.GetBlobContainerClient(blobContainerName);
containerClient.CreateIfNotExists();

builder.Services.AddSingleton(containerClient);

string subscriptionKey = secretClient.GetSecret("CvSubscriptionKey").Value.Value.ToString();
string endpoint = secretClient.GetSecret("CVEndPoint").Value.Value.ToString();

ComputerVisionClient computerVisionClient = new(new ApiKeyServiceClientCredentials(subscriptionKey))
{
    Endpoint = endpoint
};

builder.Services.AddSingleton(computerVisionClient);

builder.Services.AddDbContext<AppDbContext>(
    options => SqlServerDbContextOptionsExtensions.UseSqlServer(options, connectionString));

IConfigurationSection azureAdSection = builder.Configuration.GetSection("AzureAd");

azureAdSection.GetSection("Instance").Value = "https://login.microsoftonline.com/";
azureAdSection.GetSection("Domain").Value = "EPAM.onmicrosoft.com";
azureAdSection.GetSection("TenantId").Value = keyVaultTenantId;
azureAdSection.GetSection("ClientId").Value = KeyVaultClientId;
azureAdSection.GetSection("ClientSecret").Value = KeyVaultClientSecret;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
     .AddMicrosoftIdentityWebApp(azureAdSection, JwtBearerDefaults.AuthenticationScheme);

builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddControllersWithViews();

builder.Services.AddAuthorization(options => {
    options.AddPolicy("EpamersOnly", new AuthorizationPolicyBuilder()
      .RequireAuthenticatedUser()
      .RequireEmailDomain("epam.com")
      .Build());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

public static class AuthorizationPolicyExtensions
{
    public static AuthorizationPolicyBuilder RequireEmailDomain(
      this AuthorizationPolicyBuilder builder, string domain
    )
    {
        domain = domain.StartsWith("@") ? domain : $"@{domain}";
        return builder.RequireAssertion(ctx => {
            var email = ctx.User?.Identity?.Name ?? "";
            return email.EndsWith(domain);
        });
    }
}
