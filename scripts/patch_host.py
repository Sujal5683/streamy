import os

def patch_host_service():
    path = 'src/EnterpriseRemote.Host.Service/Program.cs'
    with open(path, 'r', encoding='utf-8') as f:
        text = f.read().replace('\r\n', '\n')

    # Add using EnterpriseRemote.Common.Protocol if not present
    if 'using EnterpriseRemote.Common.Protocol;' not in text:
        text = 'using EnterpriseRemote.Common.Protocol;\n' + text

    # Main argument parsing:
    old_arg = '''            if (args[i].Equals("--server", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                serverUrl = args[i + 1];
            }'''
    new_arg = '''            if (args[i].Equals("--server", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                serverUrl = UrlUtils.NormalizeServerUrl(args[i + 1]);
            }'''
    if old_arg in text:
        text = text.replace(old_arg, new_arg)

    # EnrollDeviceAsync:
    old_enroll = '''    private static async Task<int> EnrollDeviceAsync(string serverUrl, string invitationCode)
    {'''
    new_enroll = '''    private static async Task<int> EnrollDeviceAsync(string serverUrl, string invitationCode)
    {
        serverUrl = UrlUtils.NormalizeServerUrl(serverUrl);'''
    if old_enroll in text:
        text = text.replace(old_enroll, new_enroll)

    # SaveIdentity:
    old_save = '''    private static void SaveIdentity(StoredDeviceIdentity identity)
    {
        string json = JsonSerializer.Serialize(identity, JsonOpts);

        // 1. CommonApplicationData (Service-level)
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(IdentityFile, json);
        }
        catch { }

        // 2. Local directory alongside executable
        try
        {
            string baseDirFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_identity.json");
            File.WriteAllText(baseDirFile, json);
        }
        catch { }'''

    new_save = '''    private static void SaveIdentity(StoredDeviceIdentity identity)
    {
        string json = JsonSerializer.Serialize(identity, JsonOpts);

        // 1. Local directory alongside executable (Portable & Isolated First)
        try
        {
            string baseDirFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_identity.json");
            File.WriteAllText(baseDirFile, json);
        }
        catch { }

        // 2. CommonApplicationData (Service-level fallback)
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(IdentityFile, json);
        }
        catch { }'''
    if old_save in text:
        text = text.replace(old_save, new_save)

    # LoadIdentity:
    old_load = '''        string[] candidates = new[]
        {
            IdentityFile,
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_identity.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "device_identity.json"),
            LocalFallbackIdentityFile
        };'''
    new_load = '''        string[] candidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_identity.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "device_identity.json"),
            IdentityFile,
            LocalFallbackIdentityFile
        };'''
    if old_load in text:
        text = text.replace(old_load, new_load)

    # RunServiceHostAsync:
    old_run = '''        using var http = new HttpClient { BaseAddress = new Uri(targetUrl), Timeout = TimeSpan.FromSeconds(5) };'''
    new_run = '''        targetUrl = UrlUtils.NormalizeServerUrl(targetUrl);
        using var http = new HttpClient { BaseAddress = new Uri(targetUrl), Timeout = TimeSpan.FromSeconds(5) };'''
    if old_run in text:
        text = text.replace(old_run, new_run)

    with open(path, 'w', encoding='utf-8') as f:
        f.write(text.replace('\n', '\r\n'))
    print('[+] Host.Service/Program.cs patched successfully')

def patch_user_agent():
    path = 'src/EnterpriseRemote.Host.UserAgent/Program.cs'
    with open(path, 'r', encoding='utf-8') as f:
        text = f.read().replace('\r\n', '\n')

    if 'using EnterpriseRemote.Common.Protocol;' not in text:
        text = 'using EnterpriseRemote.Common.Protocol;\n' + text

    # Remove if (false) unreachable code
    old_dead = '''                    // If LanServerUrl is present, prefer it if ControlPlaneUrl is a trycloudflare tunnel or empty
                    if (false)
                    {
                        serverUrl = ident.LanServerUrl;
                    }
                    else
                    {
                        serverUrl = !string.IsNullOrWhiteSpace(ident.ControlPlaneUrl) ? ident.ControlPlaneUrl : ident.LanServerUrl;
                    }'''
    new_dead = '''                    serverUrl = !string.IsNullOrWhiteSpace(ident.ControlPlaneUrl) ? ident.ControlPlaneUrl : ident.LanServerUrl;'''
    if old_dead in text:
        text = text.replace(old_dead, new_dead)

    with open(path, 'w', encoding='utf-8') as f:
        f.write(text.replace('\n', '\r\n'))
    print('[+] Host.UserAgent/Program.cs patched successfully')

if __name__ == '__main__':
    patch_host_service()
    patch_user_agent()

