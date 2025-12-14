using FluentValidation;
using FluentValidation.AspNetCore;
using TaskSphere.Application.Validators.Identity;
using TaskSphere.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddFluentValidationClientsideAdapters();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterValidator>();

builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddSwaggerDocumentation();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

await app.SeedRolesAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerDocumentation();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();