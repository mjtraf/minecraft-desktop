namespace Cave.Desktop;

internal static class AppStorage
{
    // Resolve once, before native integrations start. Never fall back to the working directory.
    internal static readonly string LocalRoot=Resolve("LOCALAPPDATA",Environment.SpecialFolder.LocalApplicationData);
    internal static readonly string RoamingRoot=Resolve("APPDATA",Environment.SpecialFolder.ApplicationData);
    private static string Resolve(string variable,Environment.SpecialFolder folder)
    {
        string? path=Environment.GetEnvironmentVariable(variable);
        if(string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))path=Environment.GetFolderPath(folder);
        if(string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))throw new IOException("Windows did not provide a valid "+variable+" folder. Existing saves have not been replaced.");
        return Path.GetFullPath(path);
    }
    internal static string Data=>Path.Combine(LocalRoot,"CozyCave");
}
