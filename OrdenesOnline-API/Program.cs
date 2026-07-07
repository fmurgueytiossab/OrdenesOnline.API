using Microsoft.EntityFrameworkCore;
using OrdenesOnline.Application.Services;
using OrdenesOnline.Domain.interfaces;
using OrdenesOnline.Infrastructure.Persistence;
using OrdenesOnline.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddDbContext<OpersabDbContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Opersab")
    );
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
        ClockSkew = TimeSpan.Zero // opcional, para que expire exacto a los 2 min
    };
});


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins(
                "https://localhost:4200",
                "https://ordenes.seminariosab.com.pe",
                "https://ordenestest.seminariosab.com.pe"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddScoped<IPropuestaRepository, PropuestaRepository>();
builder.Services.AddScoped<PropuestaService>();

builder.Services.AddScoped<IRepresentanteRepository, RepresentanteRepository>();
builder.Services.AddScoped<RepresentanteService>();

builder.Services.AddScoped<IValorRepository, ValorRepository>();
builder.Services.AddScoped<ValorService>();

builder.Services.AddHttpClient<ZapierService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<EmailService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


app.MapControllers();

app.Run();