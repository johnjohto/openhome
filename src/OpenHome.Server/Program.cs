using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
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
builder.Services.AddScoped<LegalityService>();
builder.Services.AddScoped<VaultService>();
builder.Services.AddScoped<DexService>();
builder.Services.AddScoped<TradeService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<OpenHomeDbContext>().Database.EnsureCreated();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// M2: serve the built web UI (web/dist) when it exists, with SPA fallback to
// index.html. Skipped when the frontend hasn't been built — the API still works.
var webDist = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "web", "dist"));
if (Directory.Exists(webDist))
{
    var distFiles = new PhysicalFileProvider(webDist);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = distFiles });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = distFiles });
    app.MapFallback(async context =>
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(Path.Combine(webDist, "index.html"));
    });
}

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

app.MapGet("/api/vault/pokemon", (VaultService vault) => vault.ListStoredPokemonAsync())
    .WithName("ListVaultPokemon");

// Query endpoint over the denormalized columns. Legality ("valid"/"invalid") is
// computed lazily per row — no denormalized verdict column. The web UI filters
// client-side over GET /api/vault/pokemon instead (vault scale is small); this
// endpoint serves API consumers and larger vaults.
app.MapGet("/api/vault/pokemon/query", (
        int? species, int? minLevel, int? maxLevel, bool? shiny,
        string? originGame, string? legality, string? search,
        string? sortBy, bool sortDesc, VaultService vault) =>
    HandleErrors(() => vault.QueryStoredPokemonAsync(
        new VaultQueryFilter(species, minLevel, maxLevel, shiny, originGame, legality, search, sortBy, sortDesc))))
    .WithName("QueryVaultPokemon");

app.MapGet("/api/vault/pokemon/{id:guid}", (Guid id, VaultService vault) => HandleErrors(() => vault.GetStoredPokemonAsync(id)))
    .WithName("GetVaultPokemon");

// Legality is informational only — this endpoint reports, it never gates anything.
app.MapGet("/api/vault/pokemon/{id:guid}/legality", (Guid id, LegalityService legal) => HandleErrors(() => legal.AnalyzeStoredAsync(id)))
    .WithName("GetVaultPokemonLegality");

app.MapPost("/api/vault/boxes", (CreateBoxRequest? req, VaultService vault) => vault.CreateVaultBoxAsync(req?.Name))
    .WithName("CreateVaultBox");

app.MapPost("/api/vault/deposit", (DepositRequest req, VaultService vault) => HandleErrors(() => vault.DepositAsync(req.SaveId, req.Box, req.Slot)))
    .WithName("DepositPokemon");

app.MapPost("/api/vault/withdraw", (WithdrawRequest req, VaultService vault) => HandleErrors(() => vault.WithdrawAsync(req.PokemonId, req.SaveId, req.Box, req.Slot)))
    .WithName("WithdrawPokemon");

app.MapPost("/api/vault/move", (MoveRequest req, VaultService vault) => HandleErrors(() => vault.MovePokemonAsync(req.PokemonId, req.BoxId, req.Slot)))
    .WithName("MovePokemon");

app.MapPost("/api/vault/deposit/bulk", (BulkDepositRequest req, VaultService vault) => HandleErrors(() => vault.DepositManyAsync(req.SaveId, req.Slots)))
    .WithName("BulkDepositPokemon");

app.MapPost("/api/vault/move/bulk", (BulkMoveRequest req, VaultService vault) => HandleErrors(() => vault.MoveManyAsync(req.PokemonIds, req.BoxId)))
    .WithName("BulkMovePokemon");

// Release is permanent — the response reports exactly what was released.
app.MapPost("/api/vault/release", (ReleaseRequest req, VaultService vault) => HandleErrors(() => vault.ReleaseManyAsync(req.PokemonIds)))
    .WithName("ReleasePokemon");

// Local trade: swaps the Pokémon in two save slots (either save may be the same
// one) and applies trade evolution on receipt. Both saves are snapshotted first.
app.MapPost("/api/trades", (TradeRequest req, TradeService trades) =>
    HandleErrors(() => trades.TradeAsync(req.SaveAId, req.BoxA, req.SlotA, req.SaveBId, req.BoxB, req.SlotB)))
    .WithName("TradePokemon");

// Living national dex, computed from current vault contents.
app.MapGet("/api/dex/national", (DexService dex) => dex.GetNationalDexAsync())
    .WithName("GetNationalDex");

// Per-save dex: the save's own seen/caught data when it has a Pokédex, else box contents.
app.MapGet("/api/dex/saves/{id:guid}", (Guid id, DexService dex) => HandleErrors(() => dex.GetSaveDexAsync(id)))
    .WithName("GetSaveDex");

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
internal sealed record BulkDepositRequest(Guid SaveId, BoxSlotRef[] Slots);
internal sealed record BulkMoveRequest(Guid[] PokemonIds, Guid BoxId);
internal sealed record ReleaseRequest(Guid[] PokemonIds);
internal sealed record TradeRequest(Guid SaveAId, int BoxA, int SlotA, Guid SaveBId, int BoxB, int SlotB);
