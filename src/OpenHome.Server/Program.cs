using Microsoft.EntityFrameworkCore;
using OpenHome.Core;
using OpenHome.Core.Persistence;

var builder = WebApplication.CreateBuilder(args);

var dataRoot = Environment.GetEnvironmentVariable("OPENHOME_DATA")
    ?? Path.Combine(builder.Environment.ContentRootPath, "data");
var options = new OpenHomeOptions(dataRoot);
options.EnsureDirectories();

builder.Services.AddOpenApi();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<SaveFileService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddDbContext<OpenHomeDbContext>(o => o.UseSqlite($"Data Source={options.DatabasePath}"));
builder.Services.AddScoped<SaveLibraryService>();
builder.Services.AddScoped<VaultService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<OpenHomeDbContext>().Database.EnsureCreated();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Smoke endpoint: proves the server can exercise PKHeX via OpenHome.Core.
app.MapPost("/api/saves/summary", (IFormFile file, SaveFileService saves) =>
{
    var temp = Path.Combine(Path.GetTempPath(), $"openhome-upload-{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
    try
    {
        using (var stream = File.Create(temp))
            file.CopyTo(stream);
        return Results.Ok(saves.Summarize(temp));
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    finally
    {
        if (File.Exists(temp)) File.Delete(temp);
    }
})
.WithName("SummarizeSave")
.DisableAntiforgery();

app.MapPost("/api/saves", async (IFormFile file, SaveLibraryService library) =>
{
    var temp = Path.Combine(Path.GetTempPath(), $"openhome-upload-{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
    try
    {
        using (var stream = File.Create(temp))
            await file.CopyToAsync(stream);
        return Results.Ok(await library.RegisterAsync(temp, file.FileName));
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    finally
    {
        if (File.Exists(temp)) File.Delete(temp);
    }
})
.WithName("RegisterSave")
.DisableAntiforgery();

app.MapGet("/api/saves", (SaveLibraryService library) => library.ListAsync())
    .WithName("ListSaves");

app.MapGet("/api/saves/{id:guid}/boxes", (Guid id, VaultService vault) => HandleErrors(() => vault.ListSaveBoxesAsync(id)))
    .WithName("GetSaveBoxes");

app.MapGet("/api/vault/boxes", (VaultService vault) => vault.ListVaultBoxesAsync())
    .WithName("ListVaultBoxes");

app.MapPost("/api/vault/boxes", (CreateBoxRequest? req, VaultService vault) => vault.CreateVaultBoxAsync(req?.Name))
    .WithName("CreateVaultBox");

app.MapPost("/api/vault/deposit", (DepositRequest req, VaultService vault) => HandleErrors(() => vault.DepositAsync(req.SaveId, req.Box, req.Slot)))
    .WithName("DepositPokemon");

app.MapPost("/api/vault/withdraw", (WithdrawRequest req, VaultService vault) => HandleErrors(() => vault.WithdrawAsync(req.PokemonId, req.SaveId, req.Box, req.Slot)))
    .WithName("WithdrawPokemon");

app.MapPost("/api/vault/move", (MoveRequest req, VaultService vault) => HandleErrors(() => vault.MovePokemonAsync(req.PokemonId, req.BoxId, req.Slot)))
    .WithName("MovePokemon");

app.Run();

static async Task<IResult> HandleErrors<T>(Func<Task<T>> action)
{
    try
    {
        return Results.Ok(await action());
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (UnsupportedConversionException ex)
    {
        return Results.UnprocessableEntity(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}

internal sealed record CreateBoxRequest(string? Name);
internal sealed record DepositRequest(Guid SaveId, int Box, int Slot);
internal sealed record WithdrawRequest(Guid PokemonId, Guid SaveId, int Box, int Slot);
internal sealed record MoveRequest(Guid PokemonId, Guid BoxId, int Slot);
