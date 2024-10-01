

using AICustomerServiceAPI.Services;
using Microsoft.AspNetCore.Mvc;
using OpenAiRepository;
using OpenAiRepository.Models.Requests;
using System.Reflection;


var builder = WebApplication.CreateBuilder(args);

// Add Configurations

// Add OpenAiClient
builder.Services.AddOpenAiClient(opt =>
{
    opt.ApiKey = builder.Configuration.GetValue<string>("OpenAiClientSecrets:ApiKey");
    opt.ApiUrl = builder.Configuration.GetValue<string>("OpenAiClientSecrets:ApiUrl");
});

builder.Configuration.AddEnvironmentVariables()
    .AddUserSecrets(Assembly.GetExecutingAssembly(), true);

// Add services to the container.
builder.Services.AddScoped<ICustomerAssistantService, CustomerAssistantService>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();


app.MapControllers();

// Resolve the ICustomerAssistantService and call InitializeAssistant
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var openAiClient = services.GetRequiredService<ICustomerAssistantService>();
    await openAiClient.InitializeAssistant();
}

app.MapPost("/chat", async ([FromServices] ICustomerAssistantService customerAssistantService) =>
{
    return await customerAssistantService.OpenChatConnection();
});

app.MapDelete("/chat/{threadId}", async ([FromServices] ICustomerAssistantService customerAssistantService, string threadId) =>
{
    await customerAssistantService.CloseChatConnection(threadId);
    return Results.NoContent();
});

app.MapPost("/chat/{threadId}", async ([FromServices] ICustomerAssistantService customerAssistantService, string threadId, string question) =>
{
    return await customerAssistantService.SendUserMessage(threadId, question);
});

app.Run();

