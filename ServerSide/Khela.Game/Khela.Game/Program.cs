using Khela.Game.Database;
using Khela.Game.Database.Models;
using Khela.Game.Managers.SRHubs;
using Khela.Game.Models.Configs;
using Khela.Game.Services;
using Khela.Game.Services.Redis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.Text;

/**
* -----------------------------------------------------------------------------
*  File: Entry
*  Project: Khela.Game (Authoritative Multiplayer Server)
*  Author: Reza Sabir (CasualLab Interactive)
*  Description:
*      The GameEngine is the central authoritative tick loop responsible for
*      driving world updates, player state synchronization, food management, and
*      gameplay events across all active worlds.
* 
*      - Each world operates independently, running parallel ticks.
*      - All player movements, deaths, and food interactions are validated
*        server-side to prevent cheating and maintain deterministic gameplay.
*      - Event dispatching (OnWorldTickCompleted, OnFoodEaten, PlayerDied)
*        is fully async-safe, concurrent, and exception-tolerant.
*      - The engine maintains a fixed tick rate defined by WorldConfig.TickRate,
*        using Redis as the real-time authoritative state backend.
* 
*  Key Features:
*      • Fully async-safe with distributed Redis locks per player/world.
*      • Parallelized tick processing for multiple worlds.
*      • Deterministic physics & score logic for anti-cheat enforcement.
*      • Decoupled event model for Broadcast / AI / Analytics modules.
* 
*  Notes:
*      - The engine does not directly handle network transport; it relies on
*        SignalR or other services to propagate delta updates to connected clients.
*      - All gameplay state is persisted in Redis for durability and scalability.
* 
*  License: Proprietary © SiliconBangla LLC. All rights reserved.
* -----------------------------------------------------------------------------
*/


var builder = WebApplication.CreateBuilder(args);

// --- Database ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// --- Identity ---
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// --- JWT ---
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();
builder.Services.AddSingleton(jwtSettings);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = true;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

builder.Services.AddAuthorization();

// --- Controllers & Swagger ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- SignalR ---
builder.Services.AddSignalR()
    .AddNewtonsoftJsonProtocol(options =>
    {
        options.PayloadSerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
        options.PayloadSerializerSettings.Formatting = Newtonsoft.Json.Formatting.None;
    });

// --- Redis ---
var redisString = !builder.Environment.IsDevelopment()
    ? builder.Configuration.GetConnectionString("RedisConnection")
    : builder.Configuration.GetConnectionString("RedisConnectionDevelopment");

builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisString));
builder.Services.AddMemoryCache();

builder.Services.AddSingleton<IRedisService, RedisService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();

// --- Game Services ---
builder.Services.AddSingleton<WorldManagerService>();
builder.Services.AddSingleton<ArenaManagerService>();
builder.Services.AddSingleton<FoodService>();
builder.Services.AddSingleton<GameEngine>();
builder.Services.AddSingleton<GameBroadcastService>();
builder.Services.AddSingleton<AIService>();

builder.Services.AddHostedService(provider => provider.GetRequiredService<GameEngine>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<GameBroadcastService>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<AIService>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<ArenaManagerService>());
builder.Services.AddHostedService<GameStateSyncService>();

builder.Services.AddResponseCompression(opts =>
{
   opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
       new[] { "application/octet-stream" });
   opts.Providers.Add<BrotliCompressionProvider>();
});

var app = builder.Build();

// --- HTTP pipeline ---
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();                
app.UseResponseCompression();    
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<SnakeHub>("/snakehub");        
app.MapHub<CommunicationHub>("/comhub");

app.Run();
