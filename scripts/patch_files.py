import os

def patch_control_plane():
    path = 'src/EnterpriseRemote.ControlPlane/Storage/IControlPlaneStore.cs'
    with open(path, 'r', encoding='utf-8') as f:
        text = f.read()

    target1 = '''    private static string GetDefaultPersistPath()
    {
        try
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(programData, "EnterpriseRemote");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "controlplane_store.json");
        }
        catch
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "controlplane_store.json");
        }
    }'''

    repl1 = '''    private static string GetDefaultPersistPath()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localPath = Path.Combine(baseDir, "controlplane_store.json");

            // If a local store file already exists, use it for isolated app portability
            if (File.Exists(localPath)) return localPath;

            try
            {
                string testFile = Path.Combine(baseDir, $".write_test_{Guid.NewGuid():N}");
                File.WriteAllText(testFile, "ok");
                File.Delete(testFile);
                return localPath;
            }
            catch { }

            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(programData, "EnterpriseRemote");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "controlplane_store.json");
        }
        catch
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "controlplane_store.json");
        }
    }'''

    target2 = '''            string[] possibleIdentityFiles = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_identity.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EnterpriseRemote", "device_identity.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "device_identity.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "device_identity.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "device_identity.json")
            };'''

    repl2 = '''            string[] possibleIdentityFiles = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device_identity.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "device_identity.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EnterpriseRemote", "device_identity.json")
            };'''

    text_norm = text.replace('\r\n', '\n')
    t1_norm = target1.replace('\r\n', '\n')
    t2_norm = target2.replace('\r\n', '\n')

    assert t1_norm in text_norm, 't1 not found'
    assert t2_norm in text_norm, 't2 not found'

    text_norm = text_norm.replace(t1_norm, repl1.replace('\r\n', '\n'))
    text_norm = text_norm.replace(t2_norm, repl2.replace('\r\n', '\n'))

    with open(path, 'w', encoding='utf-8') as f:
        f.write(text_norm.replace('\n', '\r\n'))
    print('[+] IControlPlaneStore.cs patched.')

def patch_host_service():
    path = 'src/EnterpriseRemote.Host.Service/Program.cs'
    with open(path, 'r', encoding='utf-8') as f:
        text = f.read()

    # 1. Normalize serverUrl in Main args
    # 2. In SaveIdentity, write to AppDomain.CurrentDomain.BaseDirectory FIRST
    # 3. In LoadIdentity, check BaseDirectory FIRST
    # 4. In EnrollDeviceAsync, normalize serverUrl
    text_norm = text.replace('\r\n', '\n')

    # In EnrollDeviceAsync:
    old_enroll = '''    private static async Task<int> EnrollDeviceAsync(string serverUrl, string invitationCode)
    {'''
    new_enroll = '''    private static async Task<int> EnrollDeviceAsync(string serverUrl, string invitationCode)
    {
        serverUrl = UrlUtils.NormalizeServerUrl(serverUrl);'''
    if old_enroll in text_norm:
        text_norm = text_norm.replace(old_enroll, new_enroll)

    # In Main:
    old_arg = '''            if (args[i].Equals("--server", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                serverUrl = args[i + 1];
            }'''
    new_arg = '''            if (args[i].Equals("--server", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                serverUrl = UrlUtils.NormalizeServerUrl(args[i + 1]);
            }'''
    if old_arg in text_norm:
        text_norm = text_norm.replace(old_arg, new_arg)

    # In SaveIdentity:
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
    if old_save in text_norm:
        text_norm = text_norm.replace(old_save, new_save)

    # In LoadIdentity:
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
    if old_load in text_norm:
        text_norm = text_norm.replace(old_load, new_load)

    # In RunServiceHostAsync: normalize targetUrl
    old_run = '''        using var http = new HttpClient { BaseAddress = new Uri(targetUrl), Timeout = TimeSpan.FromSeconds(5) };'''
    new_run = '''        targetUrl = UrlUtils.NormalizeServerUrl(targetUrl);
        using var http = new HttpClient { BaseAddress = new Uri(targetUrl), Timeout = TimeSpan.FromSeconds(5) };'''
    if old_run in text_norm:
        text_norm = text_norm.replace(old_run, new_run)

    with open(path, 'w', encoding='utf-8') as f:
        f.write(text_norm.replace('\n', '\r\n'))
    print('[+] Host.Service/Program.cs patched.')

def patch_user_agent():
    path = 'src/EnterpriseRemote.Host.UserAgent/Program.cs'
    with open(path, 'r', encoding='utf-8') as f:
        text = f.read()
    text_norm = text.replace('\r\n', '\n')

    # Normalize serverUrl
    old_ua_srv = '''        // Check for server_url.txt override first'''
    new_ua_srv = '''        serverUrl = UrlUtils.NormalizeServerUrl(serverUrl);
        // Check for server_url.txt override first'''
    if old_ua_srv in text_norm and 'UrlUtils.NormalizeServerUrl' not in text_norm:
        text_norm = text_norm.replace(old_ua_srv, new_ua_srv)

    # Remove if (false) dead code
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
    if old_dead in text_norm:
        text_norm = text_norm.replace(old_dead, new_dead)

    with open(path, 'w', encoding='utf-8') as f:
        f.write(text_norm.replace('\n', '\r\n'))
    print('[+] Host.UserAgent/Program.cs patched.')

def patch_remote_viewer():
    path = 'src/EnterpriseRemote.Controller/UI/RemoteViewerForm.cs'
    with open(path, 'r', encoding='utf-8') as f:
        text = f.read()
    text_norm = text.replace('\r\n', '\n')

    # PermanentTokensFilePath: check BaseDirectory first
    old_perm = '''    private static string PermanentTokensFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EnterpriseRemote",
        "permanent_keys.json");'''

    new_perm = '''    private static string PermanentTokensFilePath
    {
        get
        {
            string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "permanent_keys.json");
            if (File.Exists(local)) return local;
            try
            {
                string test = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $".wt_{Guid.NewGuid():N}");
                File.WriteAllText(test, "ok");
                File.Delete(test);
                return local;
            }
            catch { }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EnterpriseRemote",
                "permanent_keys.json");
        }
    }'''
    if old_perm in text_norm:
        text_norm = text_norm.replace(old_perm, new_perm)

    # In AuthenticateAndLoadDevicesAsync: auto start if local machine
    old_autostart = '''                // Only if the user specifically requested localhost / 127.0.0.1 and it's not up, attempt auto-start
                if (serverUrl.Contains("127.0.0.1") || serverUrl.Contains("localhost"))
                {
                    _lblStatus.Text = "Control Plane not running locally. Starting Control Plane background service...";
                    if (TryAutoStartControlPlane())'''

    new_autostart = '''                // If the user requested local machine (127.0.0.1 or local Wi-Fi/LAN IP) and it's not up, auto-start Control Plane
                if (serverUrl.Contains("127.0.0.1") || serverUrl.Contains("localhost") || LanDiscoveryUtils.IsLocalMachine(serverUrl))
                {
                    _lblStatus.Text = "Control Plane not running locally. Starting Control Plane background service...";
                    if (TryAutoStartControlPlane())'''
    if old_autostart in text_norm:
        text_norm = text_norm.replace(old_autostart, new_autostart)

    # Add [Use Wi-Fi IP] link button next to [Reset 127.0.0.1]
    old_links = '''        var lnkLocal = new LinkLabel
        {
            Text = "[Reset 127.0.0.1]",
            LinkColor = ThemeTokens.AccentCyan,
            ActiveLinkColor = ThemeTokens.AccentPrimary,
            AutoSize = true,
            Location = new Point(cardWidth - 128, 44),
            Font = ThemeTokens.FontCaption,
            BackColor = Color.Transparent
        };
        lnkLocal.LinkClicked += (_, _) =>
        {
            _txtServer.Text = "http://127.0.0.1:5293";
            CyberToast.Show(this, "Reset to Local", "Server URL reset to http://127.0.0.1:5293", ToastType.Info, 2000);
        };
        cardGateway.Controls.Add(lnkLocal);'''

    new_links = '''        var lnkWifi = new LinkLabel
        {
            Text = "[Wi-Fi IP]",
            LinkColor = ThemeTokens.AccentCyan,
            ActiveLinkColor = ThemeTokens.AccentPrimary,
            AutoSize = true,
            Location = new Point(cardWidth - 196, 44),
            Font = ThemeTokens.FontCaption,
            BackColor = Color.Transparent
        };
        lnkWifi.LinkClicked += (_, _) =>
        {
            string wifiIp = LanDiscoveryUtils.GetPrimaryLanIp();
            _txtServer.Text = $"http://{wifiIp}:5293";
            CyberToast.Show(this, "Wi-Fi Gateway Set", $"Gateway URL set to Wi-Fi IP: http://{wifiIp}:5293", ToastType.Info, 2500);
        };
        cardGateway.Controls.Add(lnkWifi);

        var lnkLocal = new LinkLabel
        {
            Text = "[Reset 127.0.0.1]",
            LinkColor = ThemeTokens.AccentCyan,
            ActiveLinkColor = ThemeTokens.AccentPrimary,
            AutoSize = true,
            Location = new Point(cardWidth - 128, 44),
            Font = ThemeTokens.FontCaption,
            BackColor = Color.Transparent
        };
        lnkLocal.LinkClicked += (_, _) =>
        {
            _txtServer.Text = "http://127.0.0.1:5293";
            CyberToast.Show(this, "Reset to Local", "Server URL reset to http://127.0.0.1:5293", ToastType.Info, 2000);
        };
        cardGateway.Controls.Add(lnkLocal);'''
    if old_links in text_norm:
        text_norm = text_norm.replace(old_links, new_links)

    with open(path, 'w', encoding='utf-8') as f:
        f.write(text_norm.replace('\n', '\r\n'))
    print('[+] RemoteViewerForm.cs patched.')

def patch_enroll_form():
    path = 'src/EnterpriseRemote.Controller/UI/EnrollRemoteDeviceForm.cs'
    with open(path, 'r', encoding='utf-8') as f:
        text = f.read()
    text_norm = text.replace('\r\n', '\n')

    # In DetectAndDisplayLanIp: only use tunnel if verified healthy
    old_tunnel_check = '''            // 2. If a public tunnel URL file exists, use it
            if (!string.IsNullOrEmpty(tunnelUrl))
            {
                _badgeLanIp.Text = $"Active Internet Tunnel: {tunnelUrl} (Connect from ANY network)";
                _badgeLanIp.Status = CyberBadgeStatus.Success;
                _txtServerUrl.Text = tunnelUrl;
                return;
            }'''

    new_tunnel_check = '''            // 2. If a public tunnel URL file exists AND is reachable, use it; otherwise delete stale file
            if (!string.IsNullOrEmpty(tunnelUrl))
            {
                if (IsTunnelReachable(tunnelUrl))
                {
                    _badgeLanIp.Text = $"Active Internet Tunnel: {tunnelUrl} (Connect from ANY network)";
                    _badgeLanIp.Status = CyberBadgeStatus.Success;
                    _txtServerUrl.Text = tunnelUrl;
                    return;
                }
                else
                {
                    TryCleanStaleTunnelFiles();
                }
            }'''
    if old_tunnel_check in text_norm:
        text_norm = text_norm.replace(old_tunnel_check, new_tunnel_check)

    # In export package, write server_url.txt
    old_export = '''            _badgeStatus.Text = $"Ready-to-copy package: {exportDir}";'''
    new_export = '''            try
            {
                File.WriteAllText(Path.Combine(exportDir, "server_url.txt"), targetServer);
                File.WriteAllText(Path.Combine(exportBinDir, "server_url.txt"), targetServer);
            }
            catch { }
            _badgeStatus.Text = $"Ready-to-copy package: {exportDir}";'''
    if old_export in text_norm:
        text_norm = text_norm.replace(old_export, new_export)

    with open(path, 'w', encoding='utf-8') as f:
        f.write(text_norm.replace('\n', '\r\n'))
    print('[+] EnrollRemoteDeviceForm.cs patched.')

if __name__ == '__main__':
    patch_control_plane()
    patch_host_service()
    patch_user_agent()
    patch_remote_viewer()
    patch_enroll_form()

