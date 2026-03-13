using System.Text;
using HabiTechs.Core.Data;
using HabiTechs.Modules.Access.Hubs;
using HabiTechs.Modules.Identity.Services;
using HabiTechs.Services; 
using Microsoft.AspNetCore.Authentication.JwtBearer; 
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// --- 1. REGISTRO DE SERVICIOS ---

// Registramos el servicio de códigos de residente
builder.Services.AddScoped<HabiTechs.Modules.Users.Services.ResidentCodeService>();

builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(config.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<TokenService>();
builder.Services.AddSignalR();

builder.Services.AddHttpClient(); // para MercadoPago calls

// Registramos cliente Supabase
var supabaseUrl = config["Supabase:Url"];
var supabaseKey = config["Supabase:Key"];
if (!string.IsNullOrEmpty(supabaseUrl) && !string.IsNullOrEmpty(supabaseKey))
{
    var options = new Supabase.SupabaseOptions
    {
        AutoRefreshToken = true,
        AutoConnectRealtime = true
    };
    builder.Services.AddSingleton(provider => new Supabase.Client(supabaseUrl, supabaseKey, options));
}

// Registramos el servicio de storage de Supabase
builder.Services.AddScoped<SupabaseStorageService>();  

// --- INICIO DE LA SECCIÓN DE IDENTITY ---

// 1. Configurar Identity (SIN la UI por defecto que usa Cookies)
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        // Tu configuración de contraseña relajada (perfecta para el sprint)
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequiredLength = 4;
        
        // Configuración clave de Tokens (para que sepa que usamos JWT)
        options.Tokens.AuthenticatorTokenProvider = TokenValidationParameters.DefaultAuthenticationType;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders(); // Para resetear passwords, etc.

// 2. Configurar la Autenticación JWT (JwtBearer)
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        var jwtKey = config["JwtSettings:Key"];
        if (string.IsNullOrEmpty(jwtKey))
        {
            throw new InvalidOperationException("La clave JWT (JwtSettings:Key) no está configurada");
        }
        
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ValidateIssuer = true,
            ValidIssuer = config["JwtSettings:Issuer"],
            ValidateAudience = true, 
            ValidAudience = config["JwtSettings:Audience"]
        };
        
        // (Para SignalR) Permitir que el Hub reciba el token desde el query string
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/accesshub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();

// --- CONFIGURACIÓN SWAGGER CORREGIDA ---
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "HabiTechs.API (Monolito)", Version = "v1" });
    
    // 1. Evitar conflictos si hay clases con el mismo nombre en distintos módulos
    c.CustomSchemaIds(type => type.ToString()); 
    
    // 2. Habilitar la subida de archivos (IFormFile)
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        In = ParameterLocation.Header, Description = "JWT Authorization (ej: 'Bearer 12345...')"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            new string[] {}
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .SetIsOriginAllowed(origin => true) 
              .AllowCredentials(); // Requerido para SignalR
    });
});


// --- 2. CONSTRUIR LA APP ---
var app = builder.Build();

// --- 3. CONFIGURAR EL PIPELINE HTTP ---
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRouting(); 

app.UseCors("AllowAll");

app.UseAuthentication(); // 1ro: Quién eres
app.UseAuthorization();  // 2do: Qué puedes hacer

app.MapControllers();
app.MapHub<AccessHub>("/accesshub");

// Migración automática al iniciar
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Ocurrió un error al migrar la base de datos.");
    }
}

app.Run();