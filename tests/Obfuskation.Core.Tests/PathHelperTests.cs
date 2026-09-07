namespace Obfuskation.Core.Tests;

public class PathHelperTests
{
    [Fact]
    public void ProfileDirectory_folgt_XDG_CONFIG_HOME()
    {
        var erwartet = Path.Combine(TestUmgebung.KonfigVerzeichnis, "obfuskation", "profile");
        Assert.Equal(erwartet, PathHelper.ProfileDirectory);
    }

    [Fact]
    public void Ohne_XDG_CONFIG_HOME_greift_das_Benutzerverzeichnis()
    {
        var vorher = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null);

            var erwartet = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "obfuskation", "profile");

            Assert.Equal(erwartet, PathHelper.ProfileDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", vorher);
        }
    }

    [Fact]
    public void DefaultProfilePath_entschaerft_den_Namen()
    {
        var pfad = PathHelper.DefaultProfilePath("kunden 2024/q3");

        Assert.Equal(Path.Combine(PathHelper.ProfileDirectory, "kunden_2024_q3.json"), pfad);
    }
}
