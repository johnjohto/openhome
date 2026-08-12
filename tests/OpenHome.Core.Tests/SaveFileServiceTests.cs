using OpenHome.Core;
using PKHeX.Core;

namespace OpenHome.Core.Tests;

public class SaveFileServiceTests
{
    // BlankSaveFile only round-trips through SaveUtil detection for some games
    // (blank saves carry a superset of blocks / incomplete sector data for others).
    // Full-generation coverage needs real fixture saves — drop them in tests/fixtures/.
    [Theory]
    [InlineData(GameVersion.B)]   // Gen 5 DS
    [InlineData(GameVersion.B2)]  // Gen 5 DS
    [InlineData(GameVersion.BD)]  // Gen 8 Switch
    public void Summarize_BlankSave_ReturnsGameAndBoxes(GameVersion version)
    {
        var path = Path.Combine(Path.GetTempPath(), $"openhome-test-{Guid.NewGuid():N}.sav");
        try
        {
            var blank = BlankSaveFile.Get(version, "TEST");
            blank.State.Edited = true;
            File.WriteAllBytes(path, blank.Write().ToArray());

            var summary = new SaveFileService().Summarize(path);

            Assert.Equal("TEST", summary.TrainerName);
            Assert.True(summary.BoxCount > 0);
            Assert.Equal(summary.BoxCount, summary.BoxNames.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Summarize_GarbageFile_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"openhome-test-{Guid.NewGuid():N}.sav");
        try
        {
            File.WriteAllBytes(path, new byte[128]);
            Assert.Throws<InvalidDataException>(() => new SaveFileService().Summarize(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
