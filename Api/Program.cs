using System.Data;
using System.Text;
using Api.Hubs;
using Application.Interfaces;
using Application.Services;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// DbContext
builder.Services.AddDbContext<TicTacToeDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PostgresStr")));

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(x =>
{
    x.RequireHttpsMetadata = false;
    x.SaveToken = true;
    x.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(jwtKey)),
        ValidateAudience = false,
        ValidateIssuer = false,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("TicTacToe", policy =>
        policy.AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()
              .SetIsOriginAllowed(_ => true));
});

// Application services
builder.Services.AddSingleton<IUserConnectionService, UserConnectionService>();
builder.Services.AddSignalR();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TicTacToeDbContext>();
    var migrations = dbContext.Database.GetMigrations().ToList();
    var appliedMigrations = dbContext.Database.GetAppliedMigrations().ToList();

    if (migrations.Count > 0 &&
        appliedMigrations.Count == 0 &&
        TableExists(dbContext, "users") &&
        !TableExists(dbContext, "__EFMigrationsHistory"))
    {
        var initialMigration = migrations[0];

        dbContext.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) NOT NULL,
                "ProductVersion" character varying(32) NOT NULL,
                CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
            );
            """);

        dbContext.Database.ExecuteSqlInterpolated(
            $"""
            INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            SELECT {initialMigration}, {"10.0.7"}
            WHERE NOT EXISTS (
                SELECT 1
                FROM "__EFMigrationsHistory"
                WHERE "MigrationId" = {initialMigration}
            );
            """);
    }

    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger"));
}
else
{
    app.UseHttpsRedirection();
    app.MapGet("/", () => Results.Ok("TicTacToe API running"));
}

app.UseCors("TicTacToe");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ConnectionUserHub>("/hubs/connectionuser");

app.Run();

static bool TableExists(TicTacToeDbContext dbContext, string tableName)
{
    var connection = dbContext.Database.GetDbConnection();

    if (connection.State != ConnectionState.Open)
    {
        connection.Open();
    }

    using var command = connection.CreateCommand();
    command.CommandText =
        """
        SELECT EXISTS (
            SELECT 1
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name = @tableName
        );
        """;

    var parameter = command.CreateParameter();
    parameter.ParameterName = "@tableName";
    parameter.Value = tableName;
    command.Parameters.Add(parameter);

    return command.ExecuteScalar() as bool? ?? false;
}
