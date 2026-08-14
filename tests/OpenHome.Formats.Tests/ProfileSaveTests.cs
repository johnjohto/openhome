using Microsoft.EntityFrameworkCore;
using OpenHome.Core;
using OpenHome.Core.Persistence;
using OpenHome.Formats;
using OpenHome.Formats.Profiles;
using PKHeX.Core;

namespace OpenHome.Formats.Tests;

/// <summary>
/// Ticket #10: a synthetic expansion-layout save (national-order species table) loads through
/// the profile-driven custom reader, a vanilla-internal-order save does not get claimed,
/// profiles load from a folder at startup, and deposit works against a matched save.
/// </summary>
public class ProfileSaveTests : IDisposable
{
    private readonly Gba3Profile _profile = Gba3Profile.Load(Gba3Fixture.ShippedProfilePath);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openhome-gba3-{Guid.NewGuid():N}");
    private readonly OpenHomeDbContext _db;
    private readonly SaveLibraryService _library;
    private readonly VaultService _vault;

    public ProfileSaveTests()
    {
        var options = new OpenHomeOptions(_root);
        options.EnsureDirectories();
        _db = OpenHomeDbContext.Create(options.DatabasePath);
        _db.Database.EnsureCreated();
        var backups = new BackupService(options);
        _library = new SaveLibraryService(_db, new SaveFileService(), backups, options);
        _vault = new VaultService(_db, _library, backups, new LegalityService(_db), options);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* SQLite may hold the file briefly on Windows */ }
    }

    [Fact]
    public void Shipped_default_profile_parses()
    {
        Assert.Equal("pokeemerald-expansion", _profile.Name);
        Assert.Equal(131072, _profile.SaveSize);
        Assert.Equal(0x08012025u, _profile.Footer.Signature);
        Assert.Equal(0x238, _profile.Party.Offset);
        Assert.Equal(14, _profile.Boxes.BoxCount);
        Assert.Equal(30, _profile.Boxes.SlotsPerBox);
        Assert.True(_profile.NationalSpeciesOrder);
    }

    [Fact]
    public void Expansion_save_loads_through_custom_reader()
    {
        var data = Gba3Fixture.BuildSave(_profile);
        var reader = new ProfileSaveReader(_profile);

        Assert.True(reader.IsRecognized(data.Length));
        Assert.True(reader.TryRead(data, out var result, null));

        var sav = Assert.IsType<ProfileSaveFile>(result);
        Assert.Equal("RED", sav.OT);
        Assert.Equal(0x5678, sav.TID16);
        Assert.Equal(0x1234, sav.SID16);
        Assert.Equal(1, sav.PlayedHours);
        Assert.Equal(14, sav.BoxCount);
        Assert.Equal(30, sav.BoxSlotCount);
        Assert.True(sav.ChecksumsValid, sav.ChecksumInfo);
        Assert.Equal("BOX 1", ((IBoxDetailName)sav).GetBoxName(0));

        // National-order species re-mapped into what PKHeX understands.
        Assert.Equal((ushort)25, sav.GetBoxSlotAtIndex(0, 0).Species);  // Pikachu
        Assert.Equal((ushort)252, sav.GetBoxSlotAtIndex(0, 1).Species); // Treecko (raw 252, not gen-3 internal 277)
        Assert.Equal(0, sav.GetBoxSlotAtIndex(0, 2).Species);           // empty
        Assert.Equal(1, sav.PartyCount);
        Assert.Equal((ushort)255, sav.GetPartySlotAtIndex(0).Species);  // Torchic
        Assert.Equal("RED", sav.GetBoxSlotAtIndex(0, 1).OriginalTrainerName);
        Assert.Equal(31, sav.GetBoxSlotAtIndex(0, 1).IV_HP);
        Assert.Equal(26, sav.GetBoxSlotAtIndex(0, 1).IV_SPD);
        Assert.Equal(252, sav.GetBoxSlotAtIndex(0, 1).EV_ATK);
    }

    [Fact]
    public void Vanilla_internal_order_save_is_not_claimed()
    {
        var data = Gba3Fixture.BuildSave(_profile, nationalOrder: false);
        Assert.False(new ProfileSaveReader(_profile).TryRead(data, out _, null));
    }

    [Fact]
    public void Profiles_load_from_folder_and_route_via_SaveUtil()
    {
        var dir = Path.Combine(_root, "profiles");
        Directory.CreateDirectory(dir);
        File.Copy(Gba3Fixture.ShippedProfilePath, Path.Combine(dir, "pokeemerald-expansion.json"));

        FormatsRegistration.Reset();
        FormatsRegistration.RegisterAll(dir);

        Assert.Contains("pokeemerald-expansion", FormatsRegistration.RegisteredProfiles);
        Assert.Empty(FormatsRegistration.ProfileErrors);

        var data = Gba3Fixture.BuildSave(_profile);
        var sav = SaveUtil.GetSaveFile(data, "save.sav");
        var matched = Assert.IsType<ProfileSaveFile>(sav);
        Assert.Equal("pokeemerald-expansion", matched.Profile.Name);
    }

    [Fact]
    public async Task Deposit_from_profile_save_preserves_core_fields()
    {
        FormatsRegistration.RegisterAll();
        var path = Path.Combine(_root, "expansion.sav");
        await File.WriteAllBytesAsync(path, Gba3Fixture.BuildSave(_profile));
        var record = await _library.RegisterAsync(path, "expansion.sav");

        var boxes = await _vault.ListSaveBoxesAsync(record.Id);
        Assert.Equal("BOX 1", boxes[0].Name);
        Assert.False(boxes[0].Slots[0].IsEmpty);
        Assert.Equal(252, boxes[0].Slots[1].Species);

        var deposited = await _vault.DepositAsync(record.Id, 0, 1); // Treecko
        Assert.Equal(252, deposited.Species);
        Assert.Equal("RED", deposited.OTName);

        var stored = await _db.StoredPokemon.SingleAsync();
        var pkh = new PKH(stored.Data);
        Assert.Equal((ushort)252, pkh.Species);
        Assert.Equal((ushort)45, pkh.Move1); // Growl
        Assert.Equal((ushort)73, pkh.Move2); // Leech Seed
        Assert.Equal(31, pkh.IV_HP);
        Assert.Equal(26, pkh.IV_SPD);
        Assert.Equal(252, pkh.EV_ATK);
        Assert.Equal(0x5678, pkh.TID16);

        // The save persisted: deposited slot cleared, other slot and checksums intact.
        var saveRecord = await _library.GetAsync(record.Id);
        var reloaded = Assert.IsType<ProfileSaveFile>(SaveUtil.GetSaveFile(await File.ReadAllBytesAsync(saveRecord.FilePath), saveRecord.FilePath));
        Assert.Equal(0, reloaded.GetBoxSlotAtIndex(0, 1).Species);
        Assert.Equal((ushort)25, reloaded.GetBoxSlotAtIndex(0, 0).Species);
        Assert.True(reloaded.ChecksumsValid, reloaded.ChecksumInfo);
    }

    [Fact]
    public async Task Withdraw_follows_HOME_no_backwards_transfer_semantics()
    {
        FormatsRegistration.RegisterAll();
        var path = Path.Combine(_root, "expansion.sav");
        await File.WriteAllBytesAsync(path, Gba3Fixture.BuildSave(_profile));
        var record = await _library.RegisterAsync(path, "expansion.sav");
        var deposited = await _vault.DepositAsync(record.Id, 0, 1);

        // PKH has no transfer route back to gen 3 (PKHeX EntityConverter: NoTransferRoute),
        // matching OpenHOME's HOME-parity rule; fangame-aware withdraw is ticket #12.
        await Assert.ThrowsAsync<UnsupportedConversionException>(() => _vault.WithdrawAsync(deposited.Id, record.Id, 0, 2));
    }
}
